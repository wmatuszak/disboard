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
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error routing component interaction to proper action: {ex.Message}");
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
