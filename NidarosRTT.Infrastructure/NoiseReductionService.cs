using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace NidarosRTT.Infrastructure;

public class NoiseReductionService
{
    private readonly string _rnnoisePath = "/app/rnnoise_demo";

    public async Task<string> CleanAudioAsync(string inputPath)
    {
        if (!File.Exists(inputPath))
            return inputPath;

        string outputPath = Path.Combine(
            Path.GetDirectoryName(inputPath)!,
            Path.GetFileNameWithoutExtension(inputPath) + "_clean.wav"
        );

        var psi = new ProcessStartInfo
        {
            FileName = _rnnoisePath,
            Arguments = $"{inputPath} {outputPath}",
            RedirectStandardError = true,  
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
            return inputPath;

        await process.WaitForExitAsync();
        
        //if RNNoise fails or output missing, keep original
        if (!File.Exists(outputPath))
            return inputPath;

        return outputPath;
    }
}