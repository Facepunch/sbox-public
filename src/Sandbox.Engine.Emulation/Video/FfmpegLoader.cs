#if !ANDROID
using System;
using System.IO;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace Sandbox.Engine.Emulation.Video;

/// <summary>
/// Chargeur FFmpeg : positionne RootPath et force le chargement des libs embarquées.
/// </summary>
public static class FfmpegLoader
{
    private static bool _loaded;
    private static readonly object _lock = new();

    public static bool EnsureLoaded()
    {
        if (_loaded) return true;

        lock (_lock)
        {
            if (_loaded) return true;

            try
            {
                var baseDir = AppContext.BaseDirectory ?? "";
                // Cherche dans runtimes/{rid}/native
                var possiblePaths = new[]
                {
                    Path.Combine(baseDir, "runtimes", "linux-x64", "native"),
                    Path.Combine(baseDir, "runtimes", "win-x64", "native"),
                    Path.Combine(baseDir, "runtimes", "osx-x64", "native")
                };

                foreach (var p in possiblePaths)
                {
                    if (Directory.Exists(p))
                    {
                        ffmpeg.RootPath = p;
                        break;
                    }
                }

                // Charger une lib pour valider
                _ = ffmpeg.avformat_version();
                _loaded = true;
                Console.WriteLine($"[NativeAOT] FFmpeg loader initialized (RootPath={ffmpeg.RootPath})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NativeAOT] FFmpeg loader failed: {ex.Message}");
                _loaded = false;
            }
        }

        return _loaded;
    }
}
#endif

