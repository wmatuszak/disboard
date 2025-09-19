using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Victoria;

namespace disboard
{
    public class Bot
    {
        private DiscordSocketClient _client;
        private CommandService _commands;
        public SoundService SoundService { get; private set; }
        private BotConfig _config;
        private IServiceProvider _services;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<ulong, System.Threading.CancellationTokenSource> _disconnectTimers = new();

        public async Task RunAsync()
        {
            // Read configuration file
            var configJson = await File.ReadAllTextAsync("/config/config.json");
            _config = JsonSerializer.Deserialize<BotConfig>(configJson);

            var socketConfig = new DiscordSocketConfig
            {
                GatewayIntents =
                    GatewayIntents.Guilds |
                    GatewayIntents.GuildMessages |
                    GatewayIntents.GuildMessageReactions |
                    GatewayIntents.DirectMessages |
                    GatewayIntents.DirectMessageReactions |
                    GatewayIntents.GuildVoiceStates |
                    GatewayIntents.GuildMembers |
                    GatewayIntents.MessageContent,
                AlwaysDownloadUsers = false,
                LogGatewayIntentWarnings = false
            };

            _client = new DiscordSocketClient(socketConfig);

            _commands = new CommandService(new CommandServiceConfig
            {
                CaseSensitiveCommands = false,
                DefaultRunMode = RunMode.Async,
                LogLevel = LogSeverity.Info
            });

            _services = new ServiceCollection()
                .AddSingleton(_client)
                .AddSingleton(_commands)
                .AddLogging()
                .AddSingleton(new Victoria.Configuration
                {
                    Hostname = _config.LavalinkHost,
                    Port = _config.LavalinkPort,
                    Authorization = _config.LavalinkPassword,
                    SelfDeaf = true
                })
                .AddSingleton<Victoria.LavaNode<Victoria.LavaPlayer<Victoria.LavaTrack>, Victoria.LavaTrack>>(sp =>
                    new Victoria.LavaNode<Victoria.LavaPlayer<Victoria.LavaTrack>, Victoria.LavaTrack>(
                        _client,
                        sp.GetRequiredService<Victoria.Configuration>(),
                        sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Victoria.LavaNode<Victoria.LavaPlayer<Victoria.LavaTrack>, Victoria.LavaTrack>>>()
                    ))
                .AddSingleton<SoundService>(sp => new SoundService(
                    _client,
                    sp.GetService<Victoria.LavaNode<Victoria.LavaPlayer<Victoria.LavaTrack>, Victoria.LavaTrack>>(),
                    _config))
                .AddSingleton<CommandHandler>()
                .BuildServiceProvider();

            SoundService = _services.GetRequiredService<SoundService>();
            SoundService.LoadSounds("/sounds");

            await _commands.AddModuleAsync<CommandHandler>(_services);

            // Log loaded sounds
            foreach (var sound in SoundService.GetAllSounds())
            {
                Console.WriteLine($"Loaded sound: {sound.Name}, Category: {sound.Category}, Path: {sound.Path}, Duration: {sound.Duration}");
            }

            _client.Log += message => { Console.WriteLine(message.ToString()); return Task.CompletedTask; };

            // Setup command handling for prefix commands
            _client.MessageReceived += HandleCommandAsync;

            // Handle ready: set activity and register interaction handlers
            _client.Ready += async () =>
            {
                if (!string.IsNullOrWhiteSpace(_config.Activity))
                {
                    await _client.SetGameAsync(_config.Activity);
                }

                try
                {
                    var lava = _services.GetRequiredService<Victoria.LavaNode<Victoria.LavaPlayer<Victoria.LavaTrack>, Victoria.LavaTrack>>();
                    if (!lava.IsConnected)
                        await lava.ConnectAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Lavalink connect error: {ex.Message}");
                }

                var commandHandler = _services.GetRequiredService<CommandHandler>();
                commandHandler.RegisterComponentHandlers(_client);
            };

            _client.Disconnected += async ex =>
            {
                try
                {
                    await SoundService.ClearAllAudioClientsAsync();
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error clearing audio clients on disconnect: {e.Message}");
                }
            };


            // Disconnect from voice channel after a period of inactivity (non-blocking)
            _client.UserVoiceStateUpdated += (user, before, after) =>
            {
                var guild = before.VoiceChannel?.Guild;
                if (guild != null && after.VoiceChannel == null)
                {
                    // Cancel any existing timer for this guild
                    if (_disconnectTimers.TryRemove(guild.Id, out var existingCts))
                    {
                        try { existingCts.Cancel(); } catch { }
                        existingCts.Dispose();
                    }

                    var cts = new System.Threading.CancellationTokenSource();
                    _disconnectTimers[guild.Id] = cts;

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(TimeSpan.FromMinutes(_config.VoiceChannelTimeoutMinutes), cts.Token);
                            if (!cts.IsCancellationRequested && !SoundService.IsPlaying(guild))
                            {
                                await SoundService.DisconnectFromGuildAsync(guild);
                            }
                        }
                        catch (TaskCanceledException) { }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Voice idle disconnect timer error: {ex.Message}");
                        }
                        finally
                        {
                            _disconnectTimers.TryRemove(guild.Id, out _);
                            cts.Dispose();
                        }
                    });
                }

                return Task.CompletedTask;
            };

            await _client.LoginAsync(TokenType.Bot, _config.Token);
            await _client.StartAsync();

            await Task.Delay(-1);
        }

        private async Task HandleCommandAsync(SocketMessage rawMessage)
        {
            if (rawMessage is not SocketUserMessage message) return;
            if (message.Source != MessageSource.User) return;

            int argPos = 0;
            var context = new SocketCommandContext(_client, message);

            if (message.HasStringPrefix(_config.CommandPrefix, ref argPos) ||
                message.HasMentionPrefix(_client.CurrentUser, ref argPos))
            {
                var result = await _commands.ExecuteAsync(context, argPos, _services);
                if (!result.IsSuccess)
                {
                    Console.WriteLine($"Command error: {result.ErrorReason}");
                }
            }
        }
    }
}
