using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.VoiceNext;
using System.Diagnostics;
using System;

namespace disboard
{
    public class SoundService
    {
        private readonly Dictionary<string, Sound> _sounds;
        private readonly DiscordClient _client;
        private readonly ConcurrentQueue<(DiscordGuild, DiscordUser, string)> _soundQueue;
        private readonly SemaphoreSlim _queueSemaphore;
        private readonly Dictionary<ulong, bool> _playingSounds;
        private bool _isPlaying;

        public SoundService(DiscordClient client)
        {
            _client = client;
            _sounds = new Dictionary<string, Sound>();
            _soundQueue = new ConcurrentQueue<(DiscordGuild, DiscordUser, string)>();
            _queueSemaphore = new SemaphoreSlim(1, 1);
            _playingSounds = new Dictionary<ulong, bool>();
            _isPlaying = false;
        }

        public void LoadSounds(string directory = "/sounds")
        {
            var soundFiles = Directory.GetFiles(directory, "*.mp3");

            foreach (var file in soundFiles)
            {
                var sound = new Sound(
                    Path.GetFileNameWithoutExtension(file),
                    file
                );
                _sounds[sound.Name] = sound;

                // Convert and cache the stream
                using (var ffmpeg = CreateStream(file))
                using (var output = ffmpeg.StandardOutput.BaseStream)
                {
                    output.CopyTo(sound.CachedStream);
                }
                sound.CachedStream.Position = 0; // Reset the stream position
            }
        }

        public string GetSoundPath(string soundName)
        {
            return _sounds.TryGetValue(soundName, out var sound) ? sound.Path : null;
        }

        public IEnumerable<Sound> GetAllSounds()
        {
            return _sounds.Values;
        }

        public IEnumerable<string> GetAllCategories()
        {
            return _sounds.Values.Select(sound => sound.Category).Distinct();
        }

        public IEnumerable<Sound> GetSoundsByCategory(string category)
        {
            return _sounds.Values.Where(sound => sound.Category == category);
        }

        public void EnqueueSound(DiscordGuild guild, DiscordUser user, string soundName)
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
                    await PlaySoundAsync(guild, user, soundName);
                }

                _isPlaying = false;
            }
            finally
            {
                _queueSemaphore.Release();
            }
        }

        private async Task PlaySoundAsync(DiscordGuild guild, DiscordUser user, string soundName)
        {
            var sound = _sounds[soundName];
            var voiceNext = _client.GetVoiceNext();
            var connection = voiceNext.GetConnection(guild);

            if (connection == null)
            {
                var member = await guild.GetMemberAsync(user.Id);
                var channel = member?.VoiceState?.Channel;
                if (channel != null)
                {
                    connection = await channel.ConnectAsync();
                    await Task.Delay(1000); // Wait for the connection to establish
                    connection = voiceNext.GetConnection(guild); // Re-check the connection
                }
            }

            if (connection != null)
            {
                var transmit = connection.GetTransmitSink();
                sound.CachedStream.Position = 0; // Reset the stream position
                await sound.CachedStream.CopyToAsync(transmit);
            }
        }

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

        public bool IsPlaying(DiscordGuild guild)
        {
            return _playingSounds.TryGetValue(guild.Id, out var isPlaying) && isPlaying;
        }

        public void SetPlaying(DiscordGuild guild, bool isPlaying)
        {
            _playingSounds[guild.Id] = isPlaying;
        }
    }
}