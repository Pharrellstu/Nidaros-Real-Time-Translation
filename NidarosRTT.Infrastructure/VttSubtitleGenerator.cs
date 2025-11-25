using System;
using System.IO;
using System.Threading.Tasks;

namespace NidarosRTT.Infrastructure
{
    public class VttSubtitleGenerator
    {
        private readonly string _outputDirectory;

        public VttSubtitleGenerator(string outputDirectory)
        {
            _outputDirectory = outputDirectory;
            Directory.CreateDirectory(_outputDirectory);
        }

        public async Task<string> GenerateVttFileAsync(string text, double startPts, double endPts)
        {
            var startTimestamp = TimeSpan.FromSeconds(startPts).ToString(@"hh\:mm\:ss\.fff");
            var endTimestamp = TimeSpan.FromSeconds(endPts).ToString(@"hh\:mm\:ss\.fff");

            var vttContent = $"WEBVTT\n\n{startTimestamp} --> {endTimestamp}\n{text}\n";

            var fileName = $"caption_{DateTime.UtcNow.Ticks}.vtt";
            var filePath = Path.Combine(_outputDirectory, fileName);

            await File.WriteAllTextAsync(filePath, vttContent);

            return filePath;
        }
    }
}
