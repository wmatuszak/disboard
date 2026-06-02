using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace disboard
{
    public class AudioProcessingService
    {
        private readonly BotConfig _config;

        public AudioProcessingService(BotConfig config)
        {
            _config = config;
        }

        public bool ShouldNormalize => _config?.NormalizeOnImport ?? false;

        public int PlaybackVolumePercent => Math.Clamp(_config?.PlaybackVolumePercent ?? 100, 1, 1000);

        public string BackupDirectory => _config?.NormalizationBackupDirectory;

        public string BuildPlaybackFilter()
        {
            if (PlaybackVolumePercent == 100)
            {
                return null;
            }

            var multiplier = PlaybackVolumePercent / 100d;
            return $"volume={multiplier.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        }

        public async Task NormalizeInPlaceAsync(string inputPath, bool createBackup = true)
        {
            if (!ShouldNormalize)
            {
                return;
            }

            var directory = Path.GetDirectoryName(inputPath) ?? ".";
            var tempOutputPath = Path.Combine(
                directory,
                $"{Path.GetFileNameWithoutExtension(inputPath)}.normalized{Path.GetExtension(inputPath)}");

            try
            {
                await RunFfmpegAsync(inputPath, tempOutputPath, BuildNormalizationFilter());

                if (createBackup)
                {
                    BackupOriginal(inputPath);
                }

                File.Delete(inputPath);
                File.Move(tempOutputPath, inputPath);
            }
            catch
            {
                try
                {
                    if (File.Exists(tempOutputPath))
                    {
                        File.Delete(tempOutputPath);
                    }
                }
                catch
                {
                    // ignore cleanup failure
                }

                throw;
            }
        }

        public async Task NormalizeDirectoryAsync(string directory)
        {
            if (!Directory.Exists(directory))
            {
                throw new DirectoryNotFoundException($"Audio directory not found: {directory}");
            }

            foreach (var file in Directory.GetFiles(directory, "*.*"))
            {
                if (!file.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) &&
                    !file.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                await NormalizeInPlaceAsync(file);
            }
        }

        private void BackupOriginal(string inputPath)
        {
            if (string.IsNullOrWhiteSpace(BackupDirectory))
            {
                return;
            }

            Directory.CreateDirectory(BackupDirectory);

            var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
            var backupFileName = $"{Path.GetFileNameWithoutExtension(inputPath)}.{timestamp}{Path.GetExtension(inputPath)}";
            var backupPath = Path.Combine(BackupDirectory, backupFileName);
            File.Copy(inputPath, backupPath, overwrite: false);
        }

        private string BuildNormalizationFilter()
        {
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            return
                $"loudnorm=I={_config.LoudnessTarget.ToString(culture)}:" +
                $"LRA={_config.LoudnessRangeTarget.ToString(culture)}:" +
                $"TP={_config.TruePeakLimit.ToString(culture)}";
        }

        private static async Task RunFfmpegAsync(string inputPath, string outputPath, string filter)
        {
            var ffmpeg = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            ffmpeg.ArgumentList.Add("-y");
            ffmpeg.ArgumentList.Add("-i");
            ffmpeg.ArgumentList.Add(inputPath);
            ffmpeg.ArgumentList.Add("-vn");
            ffmpeg.ArgumentList.Add("-af");
            ffmpeg.ArgumentList.Add(filter);
            ffmpeg.ArgumentList.Add("-ac");
            ffmpeg.ArgumentList.Add("2");
            ffmpeg.ArgumentList.Add("-ar");
            ffmpeg.ArgumentList.Add("48000");
            ffmpeg.ArgumentList.Add(outputPath);

            using var process = Process.Start(ffmpeg);
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0 || !File.Exists(outputPath))
            {
                throw new InvalidOperationException($"ffmpeg normalization failed for '{inputPath}': {stderr}");
            }
        }
    }
}
