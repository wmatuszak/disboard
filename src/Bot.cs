using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.Interactivity;
using DSharpPlus.Interactivity.Extensions;
using DSharpPlus.VoiceNext;
using Microsoft.Extensions.DependencyInjection;

namespace disboard
{
    public class Bot
    {
        public DiscordClient Client { get; private set; }
        public CommandsNextExtension Commands { get; private set; }
        public SoundService SoundService { get; private set; }
        private BotConfig _config;

        public async Task RunAsync()
        {
            // Read configuration file
            var configJson = await File.ReadAllTextAsync("/config/config.json");
            _config = JsonSerializer.Deserialize<BotConfig>(configJson);

            var discordConfig = new DiscordConfiguration
            {
                Token = _config.Token,
                TokenType = TokenType.Bot,
                AutoReconnect = true,
                Intents = DiscordIntents.All
            };

            Client = new DiscordClient(discordConfig);
            Client.UseVoiceNext();

            Client.UseInteractivity(new InteractivityConfiguration
            {
                Timeout = TimeSpan.FromMinutes(1)
            });

            SoundService = new SoundService(Client);
            SoundService.LoadSounds("/sounds");

            var services = new ServiceCollection()
                .AddSingleton(SoundService)
                .AddSingleton<CommandHandler>()
                .BuildServiceProvider();

            var commandsConfig = new CommandsNextConfiguration
            {
                StringPrefixes = new[] { "!" },
                EnableDms = false,
                EnableMentionPrefix = true,
                Services = services
            };

            Commands = Client.UseCommandsNext(commandsConfig);

            // Register commands
            Commands.RegisterCommands<CommandHandler>();

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