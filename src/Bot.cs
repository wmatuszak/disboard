using System;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.Interactivity;
using DSharpPlus.Interactivity.Extensions;
using DSharpPlus.VoiceNext;
using disboard;

namespace disboard
{
    public class Bot
    {
        public DiscordClient Client { get; private set; }
        public CommandsNextExtension Commands { get; private set; }
        public SoundService SoundService { get; private set; }

        public async Task RunAsync()
        {
            var config = new DiscordConfiguration
            {
                Token = "",
                TokenType = TokenType.Bot,
                AutoReconnect = true,
                Intents = DiscordIntents.All
            };

            Client = new DiscordClient(config);
            Client.UseVoiceNext();

            Client.UseInteractivity(new InteractivityConfiguration
            {
                Timeout = TimeSpan.FromMinutes(1)
            });

            var commandsConfig = new CommandsNextConfiguration
            {
                StringPrefixes = new[] { "!" },
                EnableDms = false,
                EnableMentionPrefix = true
            };

            Commands = Client.UseCommandsNext(commandsConfig);
            Commands.RegisterCommands<CommandHandler>();

            // Initialize and load sounds
            SoundService = new SoundService();
            SoundService.LoadSounds("/sounds");

            // Log loaded sounds
            foreach (var sound in SoundService.GetAllSounds())
            {
                Console.WriteLine($"Loaded sound: {sound.Name}, Category: {sound.Category}, Path: {sound.Path}, Duration: {sound.Duration}");
            }

            await Client.ConnectAsync();
            await Task.Delay(-1);
        }
    }
}