using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;

namespace disboard
{
    public class CommandHandler : ModuleBase<SocketCommandContext>
    {
        private readonly SoundService _soundService;
        private const int MaxCategoriesPerPage = 24;
        private const int MaxSoundsPerPage = 24;

        // --- Add flow state ---
        private enum AddMethod { None, Upload, YouTube }
        private enum AddStage { None, SelectingMethod, AwaitingAttachment, AwaitingNameForUpload, AwaitingYouTubeUrl, AwaitingStart, AwaitingEnd, ProcessingYouTube, AwaitingPreviewConfirmation, AwaitingNameForYouTube }
        private class AddFlow
        {
            public ulong UserId { get; init; }
            public AddMethod Method { get; set; } = AddMethod.None;
            public AddStage Stage { get; set; } = AddStage.None;
            public string TempFilePath { get; set; }
            public string YoutubeUrl { get; set; }
            public TimeSpan? Start { get; set; }
            public TimeSpan? End { get; set; }
            public string PendingExtension { get; set; }
        }
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<ulong, AddFlow> _addFlows = new();

        public CommandHandler(SoundService soundService)
        {
            _soundService = soundService;
        }

        [Command("soundboard"), Alias("sb", "sound")]
        public async Task MenuCommand()
        {
            await ShowCategoryPage(Context, 0);
        }

        public void RegisterComponentHandlers(DiscordSocketClient client)
        {
            client.ButtonExecuted += async component =>
            {
                try
                {
                    var id = component.Data.CustomId;
                    // --- Add flow buttons ---
                    if (id.StartsWith("yt_prev_ok:"))
                    {
                        var intendedUserId = ulong.Parse(id.Split(':')[1]);
                        if (component.User.Id != intendedUserId) return;
                        if (_addFlows.TryGetValue(component.User.Id, out var flow) && flow.Stage == AddStage.AwaitingPreviewConfirmation)
                        {
                            flow.Stage = AddStage.AwaitingNameForYouTube;
                            await component.UpdateAsync(m =>
                            {
                                m.Content = "Great! What should I name this sound? Use category_sound (no extension).";
                                m.Components = new ComponentBuilder().Build();
                            });
                        }
                        return;
                    }
                    if (id.StartsWith("yt_prev_redo:"))
                    {
                        var intendedUserId = ulong.Parse(id.Split(':')[1]);
                        if (component.User.Id != intendedUserId) return;
                        if (_addFlows.TryGetValue(component.User.Id, out var flow))
                        {
                            // Clean temp clip if exists
                            try { if (!string.IsNullOrEmpty(flow.TempFilePath) && System.IO.File.Exists(flow.TempFilePath)) System.IO.File.Delete(flow.TempFilePath); } catch { }
                            flow.TempFilePath = null;
                            flow.Stage = AddStage.AwaitingStart;
                            await component.UpdateAsync(m =>
                            {
                                m.Content = "No problem. Enter a new start time (seconds or HH:MM:SS[.ms]).";
                                m.Components = new ComponentBuilder().Build();
                            });
                        }
                        return;
                    }
                    if (id.StartsWith("yt_prev_cancel:"))
                    {
                        var intendedUserId = ulong.Parse(id.Split(':')[1]);
                        if (component.User.Id != intendedUserId) return;
                        CleanupFlow(component.User.Id);
                        await component.UpdateAsync(m =>
                        {
                            m.Content = "YouTube add cancelled.";
                            m.Components = new ComponentBuilder().Build();
                        });
                        return;
                    }
                    if (id.StartsWith("add_upload:"))
                    {
                        var intendedUserId = ulong.Parse(id.Split(':')[1]);
                        if (component.User.Id != intendedUserId) return;
                        var flow = _addFlows.AddOrUpdate(component.User.Id, uid => new AddFlow { UserId = uid }, (uid, existing) => existing);
                        flow.Method = AddMethod.Upload;
                        flow.Stage = AddStage.AwaitingAttachment;
                        await component.UpdateAsync(m =>
                        {
                            m.Content = "Please upload your MP3 or WAV as an attachment in this DM.";
                            m.Components = new ComponentBuilder().Build();
                        });
                        return;
                    }
                    if (id.StartsWith("add_youtube:"))
                    {
                        var intendedUserId = ulong.Parse(id.Split(':')[1]);
                        if (component.User.Id != intendedUserId) return;
                        var flow = _addFlows.AddOrUpdate(component.User.Id, uid => new AddFlow { UserId = uid }, (uid, existing) => existing);
                        flow.Method = AddMethod.YouTube;
                        flow.Stage = AddStage.AwaitingYouTubeUrl;
                        await component.UpdateAsync(m =>
                        {
                            m.Content = "Please send the YouTube link for the audio.";
                            m.Components = new ComponentBuilder().Build();
                        });
                        return;
                    }
                    if (id.StartsWith("category_"))
                    {
                        var category = id.Substring("category_".Length);
                        await HandleCategorySelection(component, category);
                    }
                    else if (id.StartsWith("sound_"))
                    {
                        var soundName = id.Substring("sound_".Length);
                        await HandleSoundSelection(component, soundName);
                    }
                    else if (id.StartsWith("more_categories_"))
                    {
                        var page = int.Parse(id.Substring("more_categories_".Length));
                        await UpdateCategoryPage(component, page);
                    }
                    else if (id.StartsWith("more_sounds_"))
                    {
                        var parts = id.Split('_');
                        var category = parts[2];
                        var page = int.Parse(parts.Last());
                        await UpdateSoundPage(component, category, page);
                    }
                    else if (id.StartsWith("del_start:"))
                    {
                        var intendedUserId = ulong.Parse(id.Split(':')[1]);
                        if (component.User.Id != intendedUserId) return;
                        var mb = new ModalBuilder()
                            .WithTitle("Delete Sound")
                            .WithCustomId($"del_modal:{intendedUserId}")
                            .AddTextInput(
                                "Sound name (no extension)",
                                "filename",
                                TextInputStyle.Short,
                                "e.g. memes_airhorn",
                                maxLength: 128,
                                required: true);
                        await component.RespondWithModalAsync(mb.Build());
                    }
                    else if (id.StartsWith("del_confirm:"))
                    {
                        var parts = id.Split(':');
                        var intendedUserId = ulong.Parse(parts[1]);
                        if (component.User.Id != intendedUserId) return;
                        // Name may contain ':' rarely; use join for remainder after 2nd part
                        var soundName = string.Join(":", parts.Skip(2));
                        if (_soundService.TryResolveSound(soundName, out var sound))
                        {
                            var ok = _soundService.DeleteSound(sound);
                            await component.UpdateAsync(m =>
                            {
                                m.Content = ok ? $"Deleted '{sound.Name}'." : $"Failed to delete '{sound.Name}'.";
                                m.Components = new ComponentBuilder().Build();
                            });
                        }
                        else
                        {
                            await component.UpdateAsync(m =>
                            {
                                m.Content = $"Sound not found.";
                                m.Components = new ComponentBuilder().Build();
                            });
                        }
                    }
                    else if (id.StartsWith("del_cancel:"))
                    {
                        var intendedUserId = ulong.Parse(id.Split(':')[1]);
                        if (component.User.Id != intendedUserId) return;
                        await component.UpdateAsync(m =>
                        {
                            m.Content = "Delete cancelled.";
                            m.Components = new ComponentBuilder().Build();
                        });
                    }
                    else if (id.StartsWith("ren_start:"))
                    {
                        var intendedUserId = ulong.Parse(id.Split(':')[1]);
                        if (component.User.Id != intendedUserId) return;
                        var mb = new ModalBuilder()
                            .WithTitle("Rename Sound — Pick File")
                            .WithCustomId($"ren_old_modal:{intendedUserId}")
                            .AddTextInput(
                                "Existing sound (no extension)",
                                "oldname",
                                TextInputStyle.Short,
                                "e.g. memes_airhorn",
                                maxLength: 128,
                                required: true);
                        await component.RespondWithModalAsync(mb.Build());
                    }
                    else if (id.StartsWith("ren_new_button:"))
                    {
                        // ren_new_button:{userId}:{resolvedName}
                        var parts = id.Split(':');
                        var intendedUserId = ulong.Parse(parts[1]);
                        if (component.User.Id != intendedUserId) return;
                        var resolvedName = string.Join(":", parts.Skip(2));
                        var mb = new ModalBuilder()
                            .WithTitle($"Rename '{resolvedName}'")
                            .WithCustomId($"ren_new_modal:{intendedUserId}:{resolvedName}")
                            .AddTextInput(
                                "New name (no extension)",
                                "newname",
                                TextInputStyle.Short,
                                "e.g. memes_airhorn2",
                                maxLength: 128,
                                required: true);
                        await component.RespondWithModalAsync(mb.Build());
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error routing component interaction to proper action: {ex.Message}");
                    Console.WriteLine(ex.StackTrace);
                }
            };

            client.ModalSubmitted += async modal =>
            {
                try
                {
                    var id = modal.Data.CustomId;
                    if (id.StartsWith("del_modal:"))
                    {
                        var intendedUserId = ulong.Parse(id.Split(':')[1]);
                        if (modal.User.Id != intendedUserId) return;
                        var filename = modal.Data.Components.First(c => c.CustomId == "filename").Value;
                        if (_soundService.TryResolveSound(filename, out var sound))
                        {
                            var builder = new ComponentBuilder()
                                .WithButton(new ButtonBuilder
                                {
                                    Label = "Confirm Delete",
                                    CustomId = $"del_confirm:{intendedUserId}:{sound.Name}",
                                    Style = ButtonStyle.Danger
                                })
                                .WithButton(new ButtonBuilder
                                {
                                    Label = "Cancel",
                                    CustomId = $"del_cancel:{intendedUserId}",
                                    Style = ButtonStyle.Secondary
                                });
                            await modal.RespondAsync($"Delete '{sound.Name}'?", components: builder.Build());
                        }
                        else
                        {
                            var retry = new ComponentBuilder()
                                .WithButton(new ButtonBuilder
                                {
                                    Label = "Enter file name",
                                    CustomId = $"del_start:{intendedUserId}",
                                    Style = ButtonStyle.Primary
                                });
                            await modal.RespondAsync("Sound not found. Try again:", components: retry.Build());
                        }
                    }
                    else if (id.StartsWith("ren_old_modal:"))
                    {
                        var intendedUserId = ulong.Parse(id.Split(':')[1]);
                        if (modal.User.Id != intendedUserId) return;
                        var oldname = modal.Data.Components.First(c => c.CustomId == "oldname").Value;
                        if (_soundService.TryResolveSound(oldname, out var sound))
                        {
                            var builder = new ComponentBuilder()
                                .WithButton(new ButtonBuilder
                                {
                                    Label = "Enter new name",
                                    CustomId = $"ren_new_button:{intendedUserId}:{sound.Name}",
                                    Style = ButtonStyle.Primary
                                })
                                .WithButton(new ButtonBuilder
                                {
                                    Label = "Cancel",
                                    CustomId = $"del_cancel:{intendedUserId}",
                                    Style = ButtonStyle.Secondary
                                });
                            await modal.RespondAsync($"Renaming '{sound.Name}'. Provide a new name:", components: builder.Build());
                        }
                        else
                        {
                            var retry = new ComponentBuilder()
                                .WithButton(new ButtonBuilder
                                {
                                    Label = "Pick file again",
                                    CustomId = $"ren_start:{intendedUserId}",
                                    Style = ButtonStyle.Primary
                                });
                            await modal.RespondAsync("Sound not found. Try again:", components: retry.Build());
                        }
                    }
                    else if (id.StartsWith("ren_new_modal:"))
                    {
                        // ren_new_modal:{userId}:{resolvedName}
                        var parts = id.Split(':');
                        var intendedUserId = ulong.Parse(parts[1]);
                        if (modal.User.Id != intendedUserId) return;
                        var resolvedName = string.Join(":", parts.Skip(2));
                        var newname = modal.Data.Components.First(c => c.CustomId == "newname").Value;

                        if (_soundService.TryResolveSound(resolvedName, out var sound))
                        {
                            var ok = _soundService.RenameSound(sound, newname);
                            await modal.RespondAsync(ok ? $"Renamed to '{System.IO.Path.GetFileNameWithoutExtension(sound.Path)}' → '{newname}'."
                                                       : "Rename failed (name in use or invalid).");
                        }
                        else
                        {
                            await modal.RespondAsync("Original sound no longer found.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Modal handler error: {ex.Message}");
                    Console.WriteLine(ex.StackTrace);
                }
            };

            // Handle DM messages for add flow
            client.MessageReceived += async rawMsg =>
            {
                try
                {
                    if (rawMsg is not SocketUserMessage msg) return;
                    if (msg.Author.IsBot) return;
                    if (msg.Channel is not IDMChannel) return;
                    var userId = msg.Author.Id;
                    if (!_addFlows.TryGetValue(userId, out var flow)) return;

                    // Ignore command invocations here; only flow responses
                    if (msg.Content != null && msg.Content.StartsWith("!")) return;

                    switch (flow.Stage)
                    {
                        case AddStage.AwaitingAttachment:
                        {
                            if (msg.Attachments == null || msg.Attachments.Count == 0)
                            {
                                await msg.Channel.SendMessageAsync("No attachment detected. Please upload an MP3 or WAV file.");
                                return;
                            }
                            var att = msg.Attachments.First();
                            var ext = System.IO.Path.GetExtension(att.Filename).ToLowerInvariant();
                            if (ext != ".mp3" && ext != ".wav")
                            {
                                await msg.Channel.SendMessageAsync("Unsupported file type. Please upload an MP3 or WAV file.");
                                return;
                            }
                            try
                            {
                                var tempPath = $"/tmp/addflow_{userId}{ext}";
                                using var http = new System.Net.Http.HttpClient();
                                var bytes = await http.GetByteArrayAsync(att.Url);
                                await File.WriteAllBytesAsync(tempPath, bytes);
                                flow.TempFilePath = tempPath;
                                flow.PendingExtension = ext;
                                flow.Stage = AddStage.AwaitingNameForUpload;
                                await msg.Channel.SendMessageAsync("Got it. What should I name this sound? Reminder: use category_soundname (no extension).");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Add upload download error: {ex.Message}");
                                await msg.Channel.SendMessageAsync("I couldn't download that attachment. Please try again.");
                            }
                            break;
                        }
                        case AddStage.AwaitingPreviewConfirmation:
                        {
                            await msg.Channel.SendMessageAsync("Please use the buttons above to confirm, redo times, or cancel.");
                            break;
                        }
                        case AddStage.AwaitingNameForUpload:
                        {
                            var name = (msg.Content ?? string.Empty).Trim();
                            if (!IsValidSoundName(name, out var reason))
                            {
                                await msg.Channel.SendMessageAsync($"Invalid name: {reason}. Please send a name like category_sound.");
                                return;
                            }
                            var finalPath = System.IO.Path.Combine("/sounds", name + flow.PendingExtension);
                            if (System.IO.File.Exists(finalPath))
                            {
                                await msg.Channel.SendMessageAsync("A sound with that name already exists. Choose a different name.");
                                return;
                            }
                            try
                            {
                                System.IO.File.Move(flow.TempFilePath, finalPath);
                                _soundService.LoadSound(finalPath);
                                CleanupFlow(userId);
                                await msg.Channel.SendMessageAsync($"Added sound '{name}'.");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Finalize upload error: {ex.Message}");
                                await msg.Channel.SendMessageAsync("Failed to save the file. Please try again.");
                            }
                            break;
                        }
                        case AddStage.AwaitingYouTubeUrl:
                        {
                            var url = (msg.Content ?? string.Empty).Trim();
                            if (!IsLikelyYoutubeUrl(url))
                            {
                                await msg.Channel.SendMessageAsync("Please send a valid YouTube URL.");
                                return;
                            }
                            flow.YoutubeUrl = url;
                            flow.Stage = AddStage.AwaitingStart;
                            await msg.Channel.SendMessageAsync("Start time? You can use seconds (e.g. 12.5) or HH:MM:SS(.ms)");
                            break;
                        }
                        case AddStage.AwaitingStart:
                        {
                            if (!TryParseTime(msg.Content?.Trim(), out var start))
                            {
                                await msg.Channel.SendMessageAsync("Couldn't parse that time. Use seconds or HH:MM:SS(.ms)");
                                return;
                            }
                            flow.Start = start;
                            flow.Stage = AddStage.AwaitingEnd;
                            await msg.Channel.SendMessageAsync("End time?");
                            break;
                        }
                        case AddStage.AwaitingEnd:
                        {
                            if (!TryParseTime(msg.Content?.Trim(), out var end))
                            {
                                await msg.Channel.SendMessageAsync("Couldn't parse that time. Use seconds or HH:MM:SS(.ms)");
                                return;
                            }
                            if (!flow.Start.HasValue || end <= flow.Start.Value)
                            {
                                await msg.Channel.SendMessageAsync("End must be greater than start.");
                                return;
                            }
                            flow.End = end;
                            flow.Stage = AddStage.ProcessingYouTube;
                            await msg.Channel.SendMessageAsync("Downloading and clipping... this may take a moment.");
                            _ = ProcessYouTubeAsync(msg, flow);
                            break;
                        }
                        case AddStage.AwaitingNameForYouTube:
                        {
                            var name = (msg.Content ?? string.Empty).Trim();
                            if (!IsValidSoundName(name, out var reason))
                            {
                                await msg.Channel.SendMessageAsync($"Invalid name: {reason}. Please send a name like category_sound.");
                                return;
                            }
                            var finalPath = System.IO.Path.Combine("/sounds", name + ".mp3");
                            if (System.IO.File.Exists(finalPath))
                            {
                                await msg.Channel.SendMessageAsync("A sound with that name already exists. Choose a different name.");
                                return;
                            }
                            try
                            {
                                System.IO.File.Move(flow.TempFilePath, finalPath);
                                _soundService.LoadSound(finalPath);
                                CleanupFlow(userId);
                                await msg.Channel.SendMessageAsync($"Added sound '{name}'.");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Finalize yt name error: {ex.Message}");
                                await msg.Channel.SendMessageAsync("Failed to save the snippet. Please try again.");
                            }
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Add flow DM handler error: {ex.Message}");
                }
            };
        }

        [Command("add")]
        public async Task AddSoundCommand()
        {
            if (Context.Message.Attachments.Count == 0)
            {
                // Start DM-based add flow
                var dm = await Context.User.CreateDMChannelAsync();
                var flow = new AddFlow { UserId = Context.User.Id, Method = AddMethod.None, Stage = AddStage.SelectingMethod };
                _addFlows.AddOrUpdate(flow.UserId, flow, (uid, existing) => flow);

                var builder = new ComponentBuilder()
                    .WithButton(new ButtonBuilder
                    {
                        Label = "Upload a file",
                        CustomId = $"add_upload:{Context.User.Id}",
                        Style = ButtonStyle.Primary
                    })
                    .WithButton(new ButtonBuilder
                    {
                        Label = "Add from YouTube",
                        CustomId = $"add_youtube:{Context.User.Id}",
                        Style = ButtonStyle.Secondary
                    });

                await dm.SendMessageAsync("How would you like to add a sound?", components: builder.Build());
                if (Context.Guild != null)
                {
                    await ReplyAsync("I DM’d you to continue adding a sound.");
                }
                return;
            }

            var attachment = Context.Message.Attachments.First();
            var fileExtension = Path.GetExtension(attachment.Filename).ToLower();
            var soundName = Path.GetFileNameWithoutExtension(attachment.Filename).Trim();

            if (fileExtension != ".mp3" && fileExtension != ".wav")
            {
                await ReplyAsync("Unsupported file type. Please upload an MP3 or WAV file.");
                return;
            }

            if (!IsValidSoundName(soundName, out var reason))
            {
                await ReplyAsync($"Invalid file name: {reason}. Use a filename like `category_sound.mp3`.");
                return;
            }

            var filePath = Path.Combine("/sounds", soundName + fileExtension);
            if (System.IO.File.Exists(filePath))
            {
                await ReplyAsync("A sound with that name already exists. Choose a different file name.");
                return;
            }

            using (var client = new System.Net.Http.HttpClient())
            {
                var fileBytes = await client.GetByteArrayAsync(attachment.Url);
                await File.WriteAllBytesAsync(filePath, fileBytes);
            }

            _soundService.LoadSound(filePath);

            await ReplyAsync($"Sound {soundName} added successfully.");
        }

        [Command("delete"), Alias("rm", "remove")]
        public async Task DeleteSoundCommand()
        {
            var dm = await Context.User.CreateDMChannelAsync();
            var builder = new ComponentBuilder()
                .WithButton(new ButtonBuilder
                {
                    Label = "Enter file name",
                    CustomId = $"del_start:{Context.User.Id}",
                    Style = ButtonStyle.Primary
                });
            await dm.SendMessageAsync("Delete a sound. Click to enter the sound name (no extension).", components: builder.Build());
            if (Context.Guild != null)
            {
                await ReplyAsync("I messaged you to continue the delete.");
            }
        }

        // --- Helpers for add flow ---
        private static bool TryParseTime(string input, out TimeSpan value)
        {
            value = TimeSpan.Zero;
            if (string.IsNullOrWhiteSpace(input)) return false;
            input = input.Trim();
            // If simple number, interpret as seconds (can be float)
            if (double.TryParse(input, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var secs))
            {
                if (secs < 0) return false;
                value = TimeSpan.FromSeconds(secs);
                return true;
            }
            // Try HH:MM:SS(.ms) or MM:SS(.ms)
            var parts = input.Split(':');
            if (parts.Length == 2 || parts.Length == 3)
            {
                double h = 0, m = 0, s = 0;
                if (parts.Length == 3)
                {
                    if (!double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out h)) return false;
                    if (!double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out m)) return false;
                    if (!double.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out s)) return false;
                }
                else
                {
                    if (!double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out m)) return false;
                    if (!double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out s)) return false;
                }
                if (m < 0 || s < 0 || h < 0) return false;
                value = TimeSpan.FromSeconds(h * 3600 + m * 60 + s);
                return true;
            }
            return false;
        }

        private static bool IsLikelyYoutubeUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            return url.Contains("youtube.com/") || url.Contains("youtu.be/");
        }

        private static bool IsValidSoundName(string name, out string reason)
        {
            reason = null;
            if (string.IsNullOrWhiteSpace(name)) { reason = "empty"; return false; }
            if (!name.Contains('_')) { reason = "missing category_ prefix"; return false; }
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) { reason = "invalid characters"; return false; }
            if (name.Contains('/') || name.Contains('\\')) { reason = "invalid path separators"; return false; }
            return true;
        }

        private static void CleanupFlow(ulong userId)
        {
            if (_addFlows.TryRemove(userId, out var flow))
            {
                try
                {
                    if (!string.IsNullOrEmpty(flow.TempFilePath) && System.IO.File.Exists(flow.TempFilePath))
                    {
                        System.IO.File.Delete(flow.TempFilePath);
                    }
                }
                catch { }
            }
        }

        private async Task ProcessYouTubeAsync(SocketUserMessage msg, AddFlow flow)
        {
            try
            {
                var userId = flow.UserId;
                var basePath = $"/tmp/addflow_{userId}";
                var downloadPath = basePath + ".mp3";
                var clipPath = basePath + "_clip.mp3";

                // Download audio using yt-dlp
                var ytdlp = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "yt-dlp",
                    ArgumentList = {
                        "-x", "--audio-format", "mp3",
                        "-f", "bestaudio/best",
                        "--no-playlist",
                        "--no-progress",
                        "-R", "5", "--fragment-retries", "10",
                        "-o", downloadPath,
                        flow.YoutubeUrl
                    },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };
                var p1 = System.Diagnostics.Process.Start(ytdlp);
                var stderr1 = await p1.StandardError.ReadToEndAsync();
                await p1.WaitForExitAsync();
                if (p1.ExitCode != 0 || !System.IO.File.Exists(downloadPath))
                {
                    Console.WriteLine($"yt-dlp failed: {stderr1}");
                    await msg.Channel.SendMessageAsync("Failed to download audio from YouTube. Please check the link and try again.");
                    CleanupFlow(userId);
                    return;
                }

                // Clip with ffmpeg
                var ss = flow.Start.Value.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var to = flow.End.Value.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var ff = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    ArgumentList = { "-y", "-ss", ss, "-to", to, "-i", downloadPath, "-ac", "2", "-ar", "48000", "-b:a", "192k", clipPath },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };
                var p2 = System.Diagnostics.Process.Start(ff);
                var stderr2 = await p2.StandardError.ReadToEndAsync();
                await p2.WaitForExitAsync();
                if (p2.ExitCode != 0 || !System.IO.File.Exists(clipPath))
                {
                    Console.WriteLine($"ffmpeg clip failed: {stderr2}");
                    await msg.Channel.SendMessageAsync("Failed to clip the audio segment. Please try different timecodes.");
                    try { if (System.IO.File.Exists(downloadPath)) System.IO.File.Delete(downloadPath); } catch { }
                    CleanupFlow(userId);
                    return;
                }

                try { if (System.IO.File.Exists(downloadPath)) System.IO.File.Delete(downloadPath); } catch { }

                flow.TempFilePath = clipPath;
                flow.Stage = AddStage.AwaitingPreviewConfirmation;
                var buttons = new ComponentBuilder()
                    .WithButton(new ButtonBuilder
                    {
                        Label = "Approve",
                        CustomId = $"yt_prev_ok:{userId}",
                        Style = ButtonStyle.Success
                    })
                    .WithButton(new ButtonBuilder
                    {
                        Label = "Re-enter times",
                        CustomId = $"yt_prev_redo:{userId}",
                        Style = ButtonStyle.Primary
                    })
                    .WithButton(new ButtonBuilder
                    {
                        Label = "Cancel",
                        CustomId = $"yt_prev_cancel:{userId}",
                        Style = ButtonStyle.Secondary
                    });

                try
                {
                    await msg.Channel.SendFileAsync(clipPath, text: "Preview your clip. Approve or re-enter times:", components: buttons.Build());
                }
                catch (Exception attachEx)
                {
                    Console.WriteLine($"Send preview failed: {attachEx.Message}");
                    await msg.Channel.SendMessageAsync("Clipped! I couldn't attach the file (possibly too large). You can still Approve or Re-enter times:", components: buttons.Build());
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ProcessYouTube error: {ex.Message}");
                await msg.Channel.SendMessageAsync("An error occurred while processing the YouTube audio.");
                CleanupFlow(flow.UserId);
            }
        }

        [Command("rename"), Alias("mv")]
        public async Task RenameSoundCommand()
        {
            var dm = await Context.User.CreateDMChannelAsync();
            var builder = new ComponentBuilder()
                .WithButton(new ButtonBuilder
                {
                    Label = "Pick file",
                    CustomId = $"ren_start:{Context.User.Id}",
                    Style = ButtonStyle.Primary
                });
            await dm.SendMessageAsync("Rename a sound. Click to choose the file (no extension).", components: builder.Build());
            if (Context.Guild != null)
            {
                await ReplyAsync("I messaged you to continue the rename.");
            }
        }

        private async Task ShowCategoryPage(ICommandContext ctx, int page)
        {
            var categories = _soundService.GetAllCategories().ToList();
            var pagedCategories = categories.Skip(page * MaxCategoriesPerPage).Take(MaxCategoriesPerPage).ToList();

            var builder = new ComponentBuilder();
            for (int i = 0; i < pagedCategories.Count; i++)
            {
                var category = pagedCategories[i];
                builder.WithButton(new ButtonBuilder
                {
                    Label = category,
                    CustomId = $"category_{category}",
                    Style = ButtonStyle.Danger
                }, row: i / 5);
            }

            if (categories.Count > (page + 1) * MaxCategoriesPerPage)
            {
                builder.WithButton(new ButtonBuilder
                {
                    Label = "More",
                    CustomId = $"more_categories_{page + 1}",
                    Style = ButtonStyle.Secondary
                }, row: pagedCategories.Count / 5);
            }
            else if (categories.Count > MaxCategoriesPerPage || page > 0)
            {
                builder.WithButton(new ButtonBuilder
                {
                    Label = "More",
                    CustomId = "more_categories_0",
                    Style = ButtonStyle.Secondary
                }, row: pagedCategories.Count / 5);
            }

            await ctx.Channel.SendMessageAsync(text: "Select a category:", components: builder.Build());
        }

        private async Task UpdateCategoryPage(SocketMessageComponent component, int page)
        {
            var categories = _soundService.GetAllCategories().ToList();
            var pagedCategories = categories.Skip(page * MaxCategoriesPerPage).Take(MaxCategoriesPerPage).ToList();

            var builder = new ComponentBuilder();
            for (int i = 0; i < pagedCategories.Count; i++)
            {
                var category = pagedCategories[i];
                builder.WithButton(new ButtonBuilder
                {
                    Label = category,
                    CustomId = $"category_{category}",
                    Style = ButtonStyle.Danger
                }, row: i / 5);
            }

            if (categories.Count > (page + 1) * MaxCategoriesPerPage)
            {
                builder.WithButton(new ButtonBuilder
                {
                    Label = "More",
                    CustomId = $"more_categories_{page + 1}",
                    Style = ButtonStyle.Secondary
                }, row: pagedCategories.Count / 5);
            }
            else if (categories.Count > MaxCategoriesPerPage || page > 0)
            {
                builder.WithButton(new ButtonBuilder
                {
                    Label = "More",
                    CustomId = "more_categories_0",
                    Style = ButtonStyle.Secondary
                }, row: pagedCategories.Count / 5);
            }

            try
            {
                await component.UpdateAsync(msg =>
                {
                    msg.Content = "Select a category:";
                    msg.Components = builder.Build();
                });
            }
            catch (Exception)
            {
                // Ignore exceptions when sending the response.
                // Redundant controls will send redundant responses and fail causing needless exceptions.
            }
        }

        private async Task ShowSoundPage(SocketMessageComponent component, string category, int page)
        {
            var sounds = _soundService.GetSoundsByCategory(category).ToList();
            var pagedSounds = sounds.Skip(page * MaxSoundsPerPage).Take(MaxSoundsPerPage).ToList();

            var builder = new ComponentBuilder();
            for (int i = 0; i < pagedSounds.Count; i++)
            {
                var sound = pagedSounds[i];
                var soundName = sound.Name.Replace($"{category}_", "");
                builder.WithButton(new ButtonBuilder
                {
                    Label = soundName,
                    CustomId = $"sound_{sound.Name}",
                    Style = ButtonStyle.Primary
                }, row: i / 5);
            }

            bool hasMorePages = sounds.Count > (page + 1) * MaxSoundsPerPage;
            if (hasMorePages || page > 0)
            {
                builder.WithButton(new ButtonBuilder
                {
                    Label = "More",
                    CustomId = $"more_sounds_{category}_{(hasMorePages ? page + 1 : 0)}",
                    Style = ButtonStyle.Secondary
                }, row: pagedSounds.Count / 5);
            }

            try
            {
                await component.Channel.SendMessageAsync(text: $"Sounds in {category}:", components: builder.Build());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling interaction: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }

        private async Task UpdateSoundPage(SocketMessageComponent component, string category, int page)
        {
            var sounds = _soundService.GetSoundsByCategory(category).ToList();
            var pagedSounds = sounds.Skip(page * MaxSoundsPerPage).Take(MaxSoundsPerPage).ToList();

            var builder = new ComponentBuilder();
            for (int i = 0; i < pagedSounds.Count; i++)
            {
                var sound = pagedSounds[i];
                var soundName = sound.Name.Replace($"{category}_", "");
                builder.WithButton(new ButtonBuilder
                {
                    Label = soundName,
                    CustomId = $"sound_{sound.Name}",
                    Style = ButtonStyle.Primary
                }, row: i / 5);
            }

            bool hasMorePages = sounds.Count > (page + 1) * MaxSoundsPerPage;
            if (hasMorePages || page > 0)
            {
                builder.WithButton(new ButtonBuilder
                {
                    Label = "More",
                    CustomId = $"more_sounds_{category}_{(hasMorePages ? page + 1 : 0)}",
                    Style = ButtonStyle.Secondary
                }, row: pagedSounds.Count / 5);
            }

            try
            {
                await component.UpdateAsync(msg =>
                {
                    msg.Content = $"Sounds in {category}:";
                    msg.Components = builder.Build();
                });
            }
            catch (Exception)
            {
                // Ignore exceptions when sending the response.
                // Redundant controls will send redundant responses and fail causing needless exceptions.
            }
        }

        private async Task HandleCategorySelection(SocketMessageComponent component, string selectedCategory)
        {
            try
            {
                await component.DeferAsync();
                await ShowSoundPage(component, selectedCategory, 0);
            }
            catch (Exception)
            {
                // Ignore exceptions when sending the response.
                // Exceptions are often thrown when there are redundant controls present in the chat.
                // The page will still display if the valid controls are still present.
                // Redundant controls will send redundant responses and fail causing double messages.
            }
        }

        private async Task HandleSoundSelection(SocketMessageComponent component, string soundName)
        {
            var soundPath = _soundService.GetSoundPath(soundName);

            if (soundPath != null)
            {
                try
                {
                    await component.DeferAsync();
                    var guild = (component.Channel as SocketGuildChannel)?.Guild;
                    if (guild != null)
                    {
                        _soundService.EnqueueSound(guild, component.User, soundName);
                    }
                }
                catch (Exception)
                {
                    // Ignore exceptions when sending the response.
                    // Exceptions are often thrown when there are redundant controls present in the chat.
                    // The sound will still play if the valid controls are still present.
                    // Redundant controls will send redundant responses and fail causing double playback.
                }
            }
            else
            {
                try
                {
                    await component.RespondAsync("Sound not found.", ephemeral: true);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error handling sound selection: {ex.Message}");
                    Console.WriteLine(ex.StackTrace);
                }
            }
        }
    }
}
