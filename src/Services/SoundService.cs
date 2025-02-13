using System.Collections.Generic;
using System.IO;
using disboard;

namespace disboard
{
    public class SoundService
    {
        private readonly Dictionary<string, Sound> _sounds;

        public SoundService()
        {
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
    }
}