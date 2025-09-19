using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Audio;
using Discord.WebSocket;
using System.Diagnostics;
using System;
using Victoria;
// Lavalink (Victoria) removed; using native Discord.Net audio

namespace disboard
{
    public class SoundService
    {
        private readonly Dictionary<string, Sound> _sounds;
        private readonly DiscordSocketClient _client;
        private readonly ConcurrentQueue<(SocketGuild, SocketUser, string)> _soundQueue;
        private readonly SemaphoreSlim _queueSemaphore;
        private readonly Dictionary<ulong, bool> _playingSounds;
        private readonly ConcurrentDictionary<ulong, IAudioClient> _audioClients;
        private bool _isPlaying;
        private readonly SemaphoreSlim _voiceConnectSemaphore = new(1, 1);
        private readonly Victoria.LavaNode<Victoria.LavaPlayer<Victoria.LavaTrack>, Victoria.LavaTrack> _lavaNode;
        private readonly BotConfig _config;

        public SoundService(DiscordSocketClient client, Victoria.LavaNode<Victoria.LavaPlayer<Victoria.LavaTrack>, Victoria.LavaTrack> lavaNode = null, BotConfig config = null)
        {
            _client = client;
            _lavaNode = lavaNode;
            _config = config;
            _sounds = new Dictionary<string, Sound>();
            _soundQueue = new ConcurrentQueue<(SocketGuild, SocketUser, string)>();
            _queueSemaphore = new SemaphoreSlim(1, 1);
            _playingSounds = new Dictionary<ulong, bool>();
            _audioClients = new ConcurrentDictionary<ulong, IAudioClient>();
            _isPlaying = false;
        }

        public void LoadSounds(string directory = "/sounds")
        {
            var soundFiles = Directory.GetFiles(directory, "*.*")
                .Where(file => file.EndsWith(".mp3") || file.EndsWith(".wav"));

            foreach (var file in soundFiles)
            {
                LoadSound(file);
            }
        }

        public void LoadSound(string filePath)
        {
            var sound = new Sound(
                Path.GetFileNameWithoutExtension(filePath),
                filePath
            );
            lock (_sounds)
            {
                _sounds[sound.Name] = sound;
            }

            // Convert and cache the stream only for direct Discord.Net audio
            if (_lavaNode == null)
            {
                using (var ffmpeg = CreateStream(filePath))
                using (var output = ffmpeg.StandardOutput.BaseStream)
                {
                    output.CopyTo(sound.CachedStream);
                }
                sound.CachedStream.Position = 0; // Reset the stream position
            }
        }

        public string GetSoundPath(string soundName)
        {
            lock (_sounds)
            {
                return _sounds.TryGetValue(soundName, out var sound) ? sound.Path : null;
            }
        }

        public IEnumerable<Sound> GetAllSounds()
        {
            lock (_sounds)
            {
                return _sounds.Values.OrderBy(sound => sound.Name).ToList();
            }
        }

        public IEnumerable<string> GetAllCategories()
        {
            lock (_sounds)
            {
                return _sounds.Values.Select(sound => sound.Category).Distinct().OrderBy(category => category).ToList();
            }
        }

        public IEnumerable<Sound> GetSoundsByCategory(string category)
        {
            lock (_sounds)
            {
                return _sounds.Values.Where(sound => sound.Category == category).OrderBy(sound => sound.Name).ToList();
            }
        }

        public void EnqueueSound(SocketGuild guild, SocketUser user, string soundName)
        {
            if (_client == null || guild == null || user == null || !_sounds.ContainsKey(soundName))
                return;

            _soundQueue.Enqueue((guild, user, soundName));
            ProcessQueue();
        }

        private async void ProcessQueue()
        {
            await _queueSemaphore.WaitAsync();

            try
            {
                if (_isPlaying)
                    return;

                _isPlaying = true;

                while (_soundQueue.TryDequeue(out var item))
                {
                    var (guild, user, soundName) = item;
                    try
                    {
                        await PlaySoundAsync(guild, user, soundName);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"PlaySoundAsync error: {ex.Message}");
                    }
                }

                _isPlaying = false;
            }
            finally
            {
                _queueSemaphore.Release();
            }
        }

        private async Task PlaySoundAsync(SocketGuild guild, SocketUser user, string soundName)
        {
            var sound = _sounds[soundName];
            // Use Lavalink path if available
            if (_lavaNode != null)
            {
                try
                {
                    var member = guild.GetUser(user.Id) as SocketGuildUser ?? guild.GetUser(user.Id);
                    var voiceChannel = (member as SocketGuildUser)?.VoiceChannel;
                    if (voiceChannel == null) return;
                    if (!_lavaNode.IsConnected) return; // Lavalink connects on Ready; avoid double-starting WS

                    var player = await _lavaNode.JoinAsync(voiceChannel);

                    var fileName = Path.GetFileName(sound.Path);
                    var baseUrl = _config?.SoundBaseUrl?.TrimEnd('/') ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(baseUrl))
                    {
                        Console.WriteLine("SoundBaseUrl not configured; cannot play via Lavalink.");
                        return;
                    }

                    var trackUrl = $"{baseUrl}/{Uri.EscapeDataString(fileName)}";
                    var search = await _lavaNode.LoadTrackAsync(trackUrl);
                    if (search == null || search.Tracks == null || !search.Tracks.Any())
                    {
                        Console.WriteLine($"Lavalink could not load track: {trackUrl}");
                        return;
                    }

                    var track = search.Tracks.First();
                    SetPlaying(guild, true);
                    await player.PlayAsync(_lavaNode, track);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Lavalink play error: {ex.Message}");
                }
                finally
                {
                    SetPlaying(guild, false);
                }
                return;
            }
            IAudioClient audioClient = null;
            try
            {
                if (!_audioClients.TryGetValue(guild.Id, out audioClient) || audioClient == null || audioClient.ConnectionState != ConnectionState.Connected)
                {
                    await _voiceConnectSemaphore.WaitAsync();
                    try
                    {
                        var member = guild.GetUser(user.Id) as SocketGuildUser ?? guild.GetUser(user.Id);
                        var channel = (member as SocketGuildUser)?.VoiceChannel;
                        if (channel == null)
                            return; // user not in a voice channel

                        // Hard reset any stale session (leave channel if connected)
                        try { await (guild.CurrentUser as SocketGuildUser)?.VoiceChannel?.DisconnectAsync(); } catch { }
                        if (audioClient != null)
                        {
                            try { await audioClient.StopAsync(); } catch { }
                            _audioClients.TryRemove(guild.Id, out _);
                        }

                        // Try to connect with retries to handle transient 4006/invalid session
                        const int maxAttempts = 5;
                        Exception lastError = null;
                        for (int attempt = 1; attempt <= maxAttempts; attempt++)
                        {
                            try
                            {
                                // Small delay before trying to join to avoid racing prior disconnect
                                await Task.Delay(500);
                                audioClient = await channel.ConnectAsync(selfDeaf: true, selfMute: false);
                                // Wait briefly for stable connection
                                var sw = System.Diagnostics.Stopwatch.StartNew();
                                while (audioClient.ConnectionState != ConnectionState.Connected && sw.Elapsed < TimeSpan.FromSeconds(15))
                                {
                                    await Task.Delay(100);
                                }
                                if (audioClient.ConnectionState == ConnectionState.Connected)
                                {
                                    _audioClients[guild.Id] = audioClient;
                                    break;
                                }
                                else
                                {
                                    throw new TimeoutException("Voice connect did not reach Connected state.");
                                }
                            }
                            catch (Exception ex)
                            {
                                lastError = ex;
                                Console.WriteLine($"Voice connect attempt {attempt} failed: {ex.Message}");
                                // Full reset between attempts (leave channel if connected)
                                try { await (guild.CurrentUser as SocketGuildUser)?.VoiceChannel?.DisconnectAsync(); } catch { }
                                if (audioClient != null)
                                {
                                    try { await audioClient.StopAsync(); } catch { }
                                    _audioClients.TryRemove(guild.Id, out _);
                                    audioClient = null;
                                }
                                try { await Task.Delay(1000 * attempt); } catch { }
                            }
                        }

                        if (audioClient == null || audioClient.ConnectionState != ConnectionState.Connected)
                        {
                            if (lastError != null) throw lastError;
                            throw new Exception("Unable to establish voice connection.");
                        }
                    }
                    finally
                    {
                        _voiceConnectSemaphore.Release();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Voice connection error: {ex.Message}");
                return;
            }

            if (audioClient != null)
            {
                try
                {
                    SetPlaying(guild, true);
                    using var pcm = audioClient.CreatePCMStream(AudioApplication.Music);
                    // Give a brief moment after connect to ensure UDP is ready
                    await Task.Delay(200);
                    sound.CachedStream.Position = 0; // Reset the stream position
                    await sound.CachedStream.CopyToAsync(pcm);
                    await pcm.FlushAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Audio send error: {ex.Message}");
                }
                finally
                {
                    SetPlaying(guild, false);
                }
            }
        }

        // Lavalink path removed in this version

        private Process CreateStream(string path)
        {
            return Process.Start(new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-i \"{path}\" -ac 2 -f s16le -ar 48000 pipe:1",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }

        public bool IsPlaying(SocketGuild guild)
        {
            return _playingSounds.TryGetValue(guild.Id, out var isPlaying) && isPlaying;
        }

        public void SetPlaying(SocketGuild guild, bool isPlaying)
        {
            _playingSounds[guild.Id] = isPlaying;
        }

        public async Task DisconnectFromGuildAsync(SocketGuild guild)
        {
            if (_lavaNode != null)
            {
                try
                {
                    var vc = (guild.CurrentUser as SocketGuildUser)?.VoiceChannel;
                    if (vc != null)
                    {
                        await _lavaNode.LeaveAsync(vc);
                    }
                }
                catch { }
            }
            else
            {
                if (_audioClients.TryRemove(guild.Id, out var client))
                {
                    try
                    {
                        await client.StopAsync();
                    }
                    catch
                    {
                        // ignore
                    }
                }
                try { await (guild.CurrentUser as SocketGuildUser)?.VoiceChannel?.DisconnectAsync(); } catch { }
            }
        }

        public async Task ClearAllAudioClientsAsync()
        {
            foreach (var kvp in _audioClients.ToArray())
            {
                if (_audioClients.TryRemove(kvp.Key, out var client))
                {
                    try { await client.StopAsync(); } catch { }
                }
            }
        }

        // --- File management helpers ---

        private static string NormalizeInputName(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            var trimmed = input.Trim();
            // Remove extension if present
            var nameOnly = Path.GetFileNameWithoutExtension(trimmed);
            // Disallow path traversal / separators
            if (nameOnly.Contains('/') || nameOnly.Contains('\\')) return null;
            return nameOnly;
        }

        public bool TryResolveSound(string userInput, out Sound sound)
        {
            sound = null;
            var normalized = NormalizeInputName(userInput);
            if (string.IsNullOrWhiteSpace(normalized)) return false;

            List<Sound> snapshot;
            lock (_sounds)
            {
                snapshot = _sounds.Values.ToList();
            }

            // Exact name match first
            sound = snapshot.FirstOrDefault(s => string.Equals(s.Name, normalized, StringComparison.OrdinalIgnoreCase));
            if (sound != null) return true;

            // If user omitted category prefix, match by suffix (unique)
            var suffixMatches = snapshot.Where(s => s.Name.EndsWith("_" + normalized, StringComparison.OrdinalIgnoreCase)
                                                 || string.Equals(Path.GetFileNameWithoutExtension(s.Path), normalized, StringComparison.OrdinalIgnoreCase)).ToList();
            if (suffixMatches.Count == 1)
            {
                sound = suffixMatches[0];
                return true;
            }

            return false;
        }

        public bool DeleteSound(Sound sound)
        {
            if (sound == null) return false;
            try
            {
                // Remove from index first
                lock (_sounds)
                {
                    _sounds.Remove(sound.Name);
                }
                try { sound.CachedStream?.Dispose(); } catch { }
                // Delete file on disk
                if (File.Exists(sound.Path))
                {
                    File.Delete(sound.Path);
                }
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DeleteSound error: {ex.Message}");
                return false;
            }
        }

        public bool RenameSound(Sound sound, string newBaseName)
        {
            if (sound == null) return false;
            var normalized = NormalizeInputName(newBaseName);
            if (string.IsNullOrWhiteSpace(normalized)) return false;

            // Compute new target path/name
            var dir = Path.GetDirectoryName(sound.Path);
            var ext = Path.GetExtension(sound.Path);
            var newName = normalized;
            // Avoid accidental double extensions
            if (newName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                newName = Path.GetFileNameWithoutExtension(newName);
            var targetPath = Path.Combine(dir, newName + ext);

            // Ensure no conflict with existing indexed sounds
            lock (_sounds)
            {
                if (_sounds.ContainsKey(newName))
                {
                    return false; // duplicate name
                }
            }

            try
            {
                // Move file on disk
                if (File.Exists(targetPath))
                {
                    return false; // file exists
                }
                File.Move(sound.Path, targetPath);

                // Update index: remove old, load new
                lock (_sounds)
                {
                    _sounds.Remove(sound.Name);
                }
                try { sound.CachedStream?.Dispose(); } catch { }
                LoadSound(targetPath);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"RenameSound error: {ex.Message}");
                return false;
            }
        }
    }
}
