using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        public SoundService(DiscordClient client)
        {
            _client = client;
            _sounds = new Dictionary<string, Sound>();
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

        public async Task PlaySoundAsync(DiscordGuild guild, DiscordUser user, string soundName)
        {
            if (_client == null || guild == null || user == null || !_sounds.ContainsKey(soundName))
                return;

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
    }
}