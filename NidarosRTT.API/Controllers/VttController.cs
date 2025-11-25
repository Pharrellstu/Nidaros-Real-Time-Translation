using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.Linq;

namespace NidarosRTT.API.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class VttController : ControllerBase
    {
        private readonly string _vttDirectory;

        public VttController()
        {
            _vttDirectory = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "vtt");
        }

        [HttpGet("list")]
        public IActionResult ListVttFiles()
        {
            if (!Directory.Exists(_vttDirectory))
            {
                return Ok(new string[0]);
            }

            var vttFiles = Directory.GetFiles(_vttDirectory, "*.vtt")
                                    .Select(Path.GetFileName)
                                    .OrderBy(f => f)
                                    .ToArray();

            return Ok(vttFiles);
        }

        [HttpGet("subtitles.m3u8")]
        public IActionResult GetSubtitlePlaylist()
        {
            if (!Directory.Exists(_vttDirectory))
            {
                return NotFound("VTT directory not found.");
            }

            var vttFiles = Directory.GetFiles(_vttDirectory, "*.vtt")
                                    .OrderBy(f => f)
                                    .ToArray();

            var playlistContent = "#EXTM3U\n";
            playlistContent += "#EXT-X-TARGETDURATION:10\n"; // A reasonable target duration
            playlistContent += "#EXT-X-VERSION:3\n";
            playlistContent += $"#EXT-X-MEDIA-SEQUENCE:{Math.Max(0, vttFiles.Length - 10)}\n"; // Start from a reasonable sequence

            foreach (var file in vttFiles)
            {
                // Assuming 5-second duration for each VTT file
                playlistContent += "#EXTINF:5.000,\n";
                playlistContent += $"/vtt/{Path.GetFileName(file)}\n";
            }
            
            // If you want a live playlist, you don't add EXT-X-ENDLIST until the stream is finished.
            // For a VOD-style playlist of all generated files, you would add it.
            // playlistContent += "#EXT-X-ENDLIST\n";

            return Content(playlistContent, "application/vnd.apple.mpegurl");
        }
    }
}
