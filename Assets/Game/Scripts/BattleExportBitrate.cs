using System;

namespace TPD.Arena
{
    internal static class BattleExportBitrate
    {
        /// <summary>
        /// Returns 0 to use the encoder default (keeps 1080p file sizes unchanged).
        /// Sub-1080p exports use a compact bitrate tuned for small files without blocking.
        /// </summary>
        public static uint Resolve(int width, int height, int fps, int configMbps)
        {
            if (configMbps > 0)
                return (uint)configMbps * 1_000_000u;

            const long fullHdPixels = 1920L * 1080L;
            long pixels = (long)width * height;
            if (pixels >= fullHdPixels)
                return 0;

            int safeFps = Math.Max(1, fps);
            // ~0.10 bpp/frame → ~800 kbps at 512²@30fps; enough for game UI + light VFX.
            uint bitrate = (uint)(pixels * safeFps * 0.10);
            return (uint)Math.Clamp((long)bitrate, 800_000L, 1_500_000L);
        }
    }
}
