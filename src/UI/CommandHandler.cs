using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.EventArgs;
using System;

namespace disboard
{
    public class CommandHandler : BaseCommandModule
    {
        private readonly SoundService _soundService;
        private const int MaxCategoriesPerPage = 24;
        private const int MaxSoundsPerPage = 24;

        public CommandHandler(SoundService soundService)
        {
            _soundService = soundService;
        }

        [Command("soundboard")]
        public async Task MenuCommand(CommandContext ctx)
        {
            await ShowCategoryPage(ctx, 0);

            ctx.Client.ComponentInteractionCreated += async (s, e) =>
            {
                try
                {
                    if (e.Id.StartsWith("category_"))
                    {
                        var category = e.Id.Substring("category_".Length);
                        await HandleCategorySelection(e.Interaction, category);
                    }
                    else if (e.Id.StartsWith("sound_"))
                    {
                        var soundName = e.Id.Substring("sound_".Length);
                        await HandleSoundSelection(e.Interaction, soundName);
                    }
                    else if (e.Id.StartsWith("more_categories_"))
                    {
                        var page = int.Parse(e.Id.Substring("more_categories_".Length));
                        await UpdateCategoryPage(e.Interaction, page);
                    }
                    else if (e.Id.StartsWith("more_sounds_"))
                    {
                        var parts = e.Id.Split('_');
                        var category = parts[2];
                        var page = int.Parse(parts.Last());
                        await UpdateSoundPage(e.Interaction, category, page);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error handling interaction: {ex.Message}");
                    Console.WriteLine(ex.StackTrace);
                }
            };
        }

        private async Task ShowCategoryPage(CommandContext ctx, int page)
        {
            var categories = _soundService.GetAllCategories().ToList();
            var pagedCategories = categories.Skip(page * MaxCategoriesPerPage).Take(MaxCategoriesPerPage).ToList();

            var categoryButtons = new List<DiscordButtonComponent>();
            foreach (var category in pagedCategories)
            {
                categoryButtons.Add(new DiscordButtonComponent(ButtonStyle.Danger, $"category_{category}", category));
            }

            if (categories.Count > (page + 1) * MaxCategoriesPerPage)
            {
                categoryButtons.Add(new DiscordButtonComponent(ButtonStyle.Secondary, $"more_categories_{page + 1}", "More"));
            }
            else if (categories.Count > MaxCategoriesPerPage || page > 0)
            {
                categoryButtons.Add(new DiscordButtonComponent(ButtonStyle.Secondary, $"more_categories_0", "More"));
            }

            var buttonRows = new List<DiscordActionRowComponent>();
            for (int i = 0; i < categoryButtons.Count; i += 5)
            {
                var rowButtons = categoryButtons.Skip(i).Take(5).ToArray();
                buttonRows.Add(new DiscordActionRowComponent(rowButtons));
            }

            var builder = new DiscordMessageBuilder()
                .WithContent("Select a category:")
                .AddComponents(buttonRows);

            await ctx.RespondAsync(builder);
        }

        private async Task UpdateCategoryPage(DiscordInteraction interaction, int page)
        {
            var categories = _soundService.GetAllCategories().ToList();
            var pagedCategories = categories.Skip(page * MaxCategoriesPerPage).Take(MaxCategoriesPerPage).ToList();

            var categoryButtons = new List<DiscordButtonComponent>();
            foreach (var category in pagedCategories)
            {
                categoryButtons.Add(new DiscordButtonComponent(ButtonStyle.Danger, $"category_{category}", category));
            }

            if (categories.Count > (page + 1) * MaxCategoriesPerPage)
            {
                categoryButtons.Add(new DiscordButtonComponent(ButtonStyle.Secondary, $"more_categories_{page + 1}", "More"));
            }
            else if (categories.Count > MaxCategoriesPerPage || page > 0)
            {
                categoryButtons.Add(new DiscordButtonComponent(ButtonStyle.Secondary, $"more_categories_0", "More"));
            }

            var buttonRows = new List<DiscordActionRowComponent>();
            for (int i = 0; i < categoryButtons.Count; i += 5)
            {
                var rowButtons = categoryButtons.Skip(i).Take(5).ToArray();
                buttonRows.Add(new DiscordActionRowComponent(rowButtons));
            }

            var builder = new DiscordInteractionResponseBuilder()
                .WithContent("Select a category:")
                .AddComponents(buttonRows);

            await interaction.CreateResponseAsync(InteractionResponseType.UpdateMessage, builder);
        }

        private async Task ShowSoundPage(DiscordInteraction interaction, string category, int page)
        {
            var sounds = _soundService.GetSoundsByCategory(category).ToList();
            var pagedSounds = sounds.Skip(page * MaxSoundsPerPage).Take(MaxSoundsPerPage).ToList();

            var soundButtons = new List<DiscordButtonComponent>();
            foreach (var sound in pagedSounds)
            {
                var soundName = sound.Name.Replace($"{category}_", "");
                soundButtons.Add(new DiscordButtonComponent(ButtonStyle.Primary, $"sound_{sound.Name}", soundName));
            }

            bool hasMorePages = sounds.Count > (page + 1) * MaxSoundsPerPage;

            if (hasMorePages || page > 0)
            {
                soundButtons.Add(new DiscordButtonComponent(ButtonStyle.Secondary, $"more_sounds_{category}_{(hasMorePages ? page + 1 : 0)}", "More"));
            }

            var buttonRows = new List<DiscordActionRowComponent>();
            for (int i = 0; i < soundButtons.Count; i += 5)
            {
                var rowButtons = soundButtons.Skip(i).Take(5).ToArray();
                buttonRows.Add(new DiscordActionRowComponent(rowButtons));
            }

            var builder = new DiscordMessageBuilder()
                .WithContent($"Sounds in {category}:")
                .AddComponents(buttonRows);

            try
            {
                await interaction.Channel.SendMessageAsync(builder);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling interaction: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }

        private async Task UpdateSoundPage(DiscordInteraction interaction, string category, int page)
        {
            var sounds = _soundService.GetSoundsByCategory(category).ToList();
            var pagedSounds = sounds.Skip(page * MaxSoundsPerPage).Take(MaxSoundsPerPage).ToList();

            var soundButtons = new List<DiscordButtonComponent>();
            foreach (var sound in pagedSounds)
            {
                var soundName = sound.Name.Replace($"{category}_", "");
                soundButtons.Add(new DiscordButtonComponent(ButtonStyle.Primary, $"sound_{sound.Name}", soundName));
            }

            bool hasMorePages = sounds.Count > (page + 1) * MaxSoundsPerPage;

            if (hasMorePages || page > 0)
            {
                soundButtons.Add(new DiscordButtonComponent(ButtonStyle.Secondary, $"more_sounds_{category}_{(hasMorePages ? page + 1 : 0)}", "More"));
            }

            var buttonRows = new List<DiscordActionRowComponent>();
            for (int i = 0; i < soundButtons.Count; i += 5)
            {
                var rowButtons = soundButtons.Skip(i).Take(5).ToArray();
                buttonRows.Add(new DiscordActionRowComponent(rowButtons));
            }

            var builder = new DiscordInteractionResponseBuilder()
                .WithContent($"Sounds in {category}:")
                .AddComponents(buttonRows);

            await interaction.CreateResponseAsync(InteractionResponseType.UpdateMessage, builder);
        }

        private async Task HandleCategorySelection(DiscordInteraction interaction, string selectedCategory)
        {
            try
            {
                await interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);
                await ShowSoundPage(interaction, selectedCategory, 0);
            }
            catch (Exception)
            {
                // Ignore exceptions when sending the response. The soundboard will still display.
                // Redundant controls will send redundant responses and fail causing double messages.
            }
        }

        private async Task HandleSoundSelection(DiscordInteraction interaction, string soundName)
        {
            var soundPath = _soundService.GetSoundPath(soundName);

            if (soundPath != null)
            {
                try
                {
                    await interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);
                    _soundService.EnqueueSound(interaction.Guild, interaction.User, soundName);
                }
                catch (Exception)
                {
                    // Ignore exceptions when sending the response. The sound will still play.
                    // Redundant controls will send redundant responses and fail causing double playback.
                }
            }
            else
            {
                try
                {
                    await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder()
                        .WithContent("Sound not found."));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error handling interaction: {ex.Message}");
                    Console.WriteLine(ex.StackTrace);
                }
            }
        }
    }
}