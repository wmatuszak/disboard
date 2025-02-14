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
        private const int MaxCategoriesPerPage = 4;
        private const int MaxSoundsPerPage = 25;

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
                    if (e.Id == "category_select")
                    {
                        var selectedCategory = e.Values[0];
                        await e.Interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder().WithContent($"You selected: {selectedCategory}"));
                        await HandleCategorySelection(e.Interaction, selectedCategory);
                    }
                    else if (e.Id.StartsWith("sound_"))
                    {
                        var soundName = e.Id.Substring(6); // Remove "sound_" prefix
                        await HandleSoundSelection(e.Interaction, soundName);
                    }
                    else if (e.Id.StartsWith("more_sounds_"))
                    {
                        var parts = e.Id.Split('_');
                        var category = parts[2];
                        var page = int.Parse(parts[3]);
                        await ShowSoundPage(e.Interaction, category, page);
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

            if (pagedCategories.Count == 0)
            {
                page = 0;
                pagedCategories = categories.Take(MaxCategoriesPerPage).ToList();
            }

            var options = pagedCategories.Select(category => new DiscordSelectComponentOption(category, category)).ToList();

            if (categories.Count > (page + 1) * MaxCategoriesPerPage)
            {
                options.Add(new DiscordSelectComponentOption("More", $"more_{page + 1}"));
            }
            else
            {
                options.Add(new DiscordSelectComponentOption("More", "more_0"));
            }

            var dropdown = new DiscordSelectComponent("category_select", "Pick a Soundboard Category:", options);

            var builder = new DiscordMessageBuilder()
                .WithContent("Select a category:")
                .AddComponents(dropdown);

            await ctx.RespondAsync(builder);
        }

        private async Task ShowCategoryPage(DiscordInteraction interaction, int page)
        {
            var categories = _soundService.GetAllCategories().ToList();
            var pagedCategories = categories.Skip(page * MaxCategoriesPerPage).Take(MaxCategoriesPerPage).ToList();

            if (pagedCategories.Count == 0)
            {
                page = 0;
                pagedCategories = categories.Take(MaxCategoriesPerPage).ToList();
            }

            var options = pagedCategories.Select(category => new DiscordSelectComponentOption(category, category)).ToList();

            if (categories.Count > (page + 1) * MaxCategoriesPerPage)
            {
                options.Add(new DiscordSelectComponentOption("More", $"more_{page + 1}"));
            }
            else
            {
                options.Add(new DiscordSelectComponentOption("More", "more_0"));
            }

            var dropdown = new DiscordSelectComponent("category_select", "Pick a Soundboard Category:", options);

            var builder = new DiscordFollowupMessageBuilder()
                .WithContent("Select a category:")
                .AddComponents(dropdown);

            await interaction.CreateFollowupMessageAsync(builder);
        }

        private async Task ShowSoundPage(DiscordInteraction interaction, string category, int page)
        {
            var sounds = _soundService.GetSoundsByCategory(category).ToList();
            var pagedSounds = sounds.Skip(page * MaxSoundsPerPage).Take(MaxSoundsPerPage).ToList();

            var soundButtons = new List<DiscordButtonComponent>();
            foreach (var sound in pagedSounds)
            {
                soundButtons.Add(new DiscordButtonComponent(ButtonStyle.Primary, $"sound_{sound.Name}", sound.Name));
            }

            if (sounds.Count > (page + 1) * MaxSoundsPerPage)
            {
                soundButtons.Add(new DiscordButtonComponent(ButtonStyle.Secondary, $"more_sounds_{category}_{page + 1}", "More"));
            }
            else
            {
                soundButtons.Add(new DiscordButtonComponent(ButtonStyle.Secondary, $"more_sounds_{category}_0", "More"));
            }

            var buttonRows = new List<DiscordActionRowComponent>();
            for (int i = 0; i < soundButtons.Count; i += 5)
            {
                var rowButtons = soundButtons.Skip(i).Take(5).ToArray();
                buttonRows.Add(new DiscordActionRowComponent(rowButtons));
            }

            var followUpBuilder = new DiscordFollowupMessageBuilder()
                .WithContent($"Sounds in {category}:")
                .AddComponents(buttonRows);

            await interaction.CreateFollowupMessageAsync(followUpBuilder);
        }

        private async Task HandleCategorySelection(DiscordInteraction interaction, string selectedCategory)
        {
            if (selectedCategory.StartsWith("more_"))
            {
                var page = int.Parse(selectedCategory.Substring(5));
                await ShowCategoryPage(interaction, page);
            }
            else
            {
                await ShowSoundPage(interaction, selectedCategory, 0);
            }
        }

        private async Task HandleSoundSelection(DiscordInteraction interaction, string soundName)
        {
            var soundPath = _soundService.GetSoundPath(soundName);

            if (soundPath != null)
            {
                await _soundService.PlaySoundAsync(interaction.Guild, interaction.User, soundName);
            }
        }
    }
}