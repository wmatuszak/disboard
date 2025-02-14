using System.IO;
using TagLib;

namespace disboard
{
    public class Sound
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public string Category { get; set; }
        public double Duration { get; set; } // Duration in seconds
        public long FileSize { get; set; } // File size in bytes
        public string Format { get; set; } // File format (e.g., mp3, wav)
        public MemoryStream CachedStream { get; set; } // Cached converted stream

        public Sound(string name, string path)
        {
            Name = name;
            Path = path;
            Category = ExtractCategory(name);
            FileSize = new FileInfo(path).Length;
            Format = System.IO.Path.GetExtension(path).TrimStart('.');
            Duration = GetAudioDuration(path);
            CachedStream = new MemoryStream();
        }

        private string ExtractCategory(string name)
        {
            var parts = name.Split('_');
            return parts.Length > 1 ? parts[0] : "Uncategorized";
        }

        private double GetAudioDuration(string path)
        {
            // Using TagLib# to get the actual duration
            try
            {
                var file = TagLib.File.Create(path);
                return file.Properties.Duration.TotalSeconds;
            }
            catch
            {
                // Handle exceptions (e.g., file not found, unsupported format)
                return 0.0;
            }
        }
    }
}