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
                            .AddTextInput(new TextInputBuilder()
                                .WithCustomId("filename")
                                .WithLabel("Sound name (no extension)")
                                .WithStyle(TextInputStyle.Short)
                                .WithPlaceholder("e.g. memes_airhorn")
                                .WithRequired(true)
                                .WithMaxLength(128));
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
                            .AddTextInput(new TextInputBuilder()
                                .WithCustomId("oldname")
                                .WithLabel("Existing sound (no extension)")
                                .WithStyle(TextInputStyle.Short)
                                .WithPlaceholder("e.g. memes_airhorn")
                                .WithRequired(true)
                                .WithMaxLength(128));
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
                            .AddTextInput(new TextInputBuilder()
                                .WithCustomId("newname")
                                .WithLabel("New name (no extension)")
                                .WithStyle(TextInputStyle.Short)
                                .WithPlaceholder("e.g. memes_airhorn2")
                                .WithRequired(true)
                                .WithMaxLength(128));
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
        }

        [Command("add")]
        public async Task AddSoundCommand()
        {
            if (Context.Message.Attachments.Count == 0)
            {
                await ReplyAsync("Please attach a file.");
                return;
            }

            var attachment = Context.Message.Attachments.First();
            var fileExtension = Path.GetExtension(attachment.Filename).ToLower();

            if (fileExtension != ".mp3" && fileExtension != ".wav")
            {
                await ReplyAsync("Unsupported file type. Please upload an MP3 or WAV file.");
                return;
            }

            var filePath = Path.Combine("/sounds", attachment.Filename);

            using (var client = new System.Net.Http.HttpClient())
            {
                var fileBytes = await client.GetByteArrayAsync(attachment.Url);
                await File.WriteAllBytesAsync(filePath, fileBytes);
            }

            _soundService.LoadSound(filePath);

            await ReplyAsync($"Sound {attachment.Filename} added successfully.");
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
