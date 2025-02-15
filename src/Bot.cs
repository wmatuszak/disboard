using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.Interactivity;
using DSharpPlus.Interactivity.Extensions;
using DSharpPlus.VoiceNext;
using DSharpPlus.Entities;
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
                Timeout = TimeSpan.FromSeconds(_config.InactivityTimeoutSeconds)
            });

            SoundService = new SoundService(Client);
            SoundService.LoadSounds("/sounds");

            var services = new ServiceCollection()
                .AddSingleton(SoundService)
                .AddSingleton<CommandHandler>()
                .BuildServiceProvider();

            var commandsConfig = new CommandsNextConfiguration
            {
                StringPrefixes = new[] { _config.CommandPrefix },
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

            // Disconnect from voice channel after a period of inactivity
            Client.VoiceStateUpdated += async (s, e) =>
            {
                if (e.After.Channel == null && e.Before.Channel != null)
                {
                    await Task.Delay(TimeSpan.FromMinutes(_config.VoiceChannelTimeoutMinutes));
                    var voiceNext = Client.GetVoiceNext();
                    var connection = voiceNext.GetConnection(e.Guild);
                    if (connection != null && !SoundService.IsPlaying(e.Guild))
                    {
                        connection.Disconnect();
                    }
                }
            };

            Client.Ready += OnClientReady;

            await Client.ConnectAsync();
            await Task.Delay(-1);
        }

        private Task OnClientReady(DiscordClient sender, DSharpPlus.EventArgs.ReadyEventArgs e)
        {
            // Set the bot's activity
            return Client.UpdateStatusAsync(new DiscordActivity(_config.Activity));
        }
    }
}