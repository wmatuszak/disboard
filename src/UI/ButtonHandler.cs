using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using System.Threading.Tasks;

namespace disboard
{
    public class ButtonHandler
    {
        public DiscordButtonComponent CreateButton(string label, string customId)
        {
            return new DiscordButtonComponent(ButtonStyle.Primary, customId, label);
        }

        public async Task HandleButtonClick(DiscordClient client, ComponentInteractionCreateEventArgs e)
        {
            // Logic to handle button click events
            await e.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);

            // Example: Play sound based on customId
            string soundId = e.Id;
            // Call SoundService to play the sound associated with soundId
        }
    }
}