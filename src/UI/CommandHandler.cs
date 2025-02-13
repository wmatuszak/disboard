using System.Collections.Generic;
using System.Threading.Tasks;
using DSharpPlus.Entities;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;

namespace disboard
{
    public class CommandHandler : BaseCommandModule
    {
        [Command("soundboard")]
        public async Task MenuCommand(CommandContext ctx)
        {
            // Create the options for the user to pick
            var options = new List<DiscordSelectComponentOption>()
            {
                new DiscordSelectComponentOption(
                    "Label, no description",
                    "label_no_desc"),

                new DiscordSelectComponentOption(
                    "Label, Description",
                    "label_with_desc",
                    "This is a description!"),

                new DiscordSelectComponentOption(
                    "Label, Description, Emoji",
                    "label_with_desc_emoji",
                    "This is a description!",
                    emoji: new DiscordComponentEmoji(854260064906117121)),

                new DiscordSelectComponentOption(
                    "Label, Description, Emoji (Default)",
                    "label_with_desc_emoji_default",
                    "This is a description!",
                    isDefault: true,
                    new DiscordComponentEmoji(854260064906117121))
            };

            // Make the dropdown
            var dropdown = new DiscordSelectComponent("dropdown", null, options, false, 1, 2);

            var builder = new DiscordMessageBuilder()
                .WithContent("Look, it's a dropdown!")
                .AddComponents(dropdown);
            
            await ctx.RespondAsync(builder);
        }
    }
}