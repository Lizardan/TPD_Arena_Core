using System;
using System.Globalization;
using UnityEngine;

#if !UNITY_WEBGL
using System.Diagnostics;
using System.IO;

namespace TPD.Arena
{
    public class FFmpegVideoEncoder : IBattleVideoEncoder, IDisposable
    {
        private Process ffmpegProcess;
        private Stream stdin;
        private int width;
        private int height;
        private byte[] frameBuffer;
        private bool disposed;

        public bool Begin(string outputPath, int width, int height, int fps, uint targetBitRate = 0)
        {
            this.width = width;
            this.height = height;
            frameBuffer = new byte[width * height * 4];

            string directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            string bitrateArg = targetBitRate > 0
                ? $"-b:v {targetBitRate}"
                : string.Empty;

            string arguments = string.Format(
                CultureInfo.InvariantCulture,
                "-y -f rawvideo -pix_fmt rgba -s {0}x{1} -r {2} -i pipe:0 -c:v libx264 -preset fast -pix_fmt yuv420p -movflags +faststart {3} \"{4}\"",
                width,
                height,
                fps,
                bitrateArg,
                outputPath);

            try
            {
                ffmpegProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = ResolveFfmpegExecutable(),
                        Arguments = arguments,
                        UseShellExecute = false,
                        RedirectStandardInput = true,
                        RedirectStandardError = true,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };

                if (!ffmpegProcess.Start())
                {
                    UnityEngine.Debug.LogError("Failed to start ffmpeg process.");
                    CleanupProcess();
                    return false;
                }

                stdin = ffmpegProcess.StandardInput.BaseStream;
                return true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"FFmpeg init failed: {ex.Message}");
                CleanupProcess();
                return false;
            }
        }

        public void AddFrame(Texture2D frame)
        {
            if (stdin == null || frame == null || disposed)
                return;

            Color32[] pixels = frame.GetPixels32();
            int rowBytes = width * 4;
            for (int y = 0; y < height; y++)
            {
                int srcRow = (height - 1 - y) * width;
                int dstRow = y * rowBytes;
                for (int x = 0; x < width; x++)
                {
                    Color32 pixel = pixels[srcRow + x];
                    int index = dstRow + x * 4;
                    frameBuffer[index] = pixel.r;
                    frameBuffer[index + 1] = pixel.g;
                    frameBuffer[index + 2] = pixel.b;
                    frameBuffer[index + 3] = pixel.a;
                }
            }

            stdin.Write(frameBuffer, 0, frameBuffer.Length);
        }

        public void End()
        {
            if (disposed)
                return;

            try
            {
                stdin?.Flush();
                stdin?.Close();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"FFmpeg stdin close warning: {ex.Message}");
            }

            if (ffmpegProcess != null)
            {
                string stderr = ffmpegProcess.StandardError.ReadToEnd();
                ffmpegProcess.WaitForExit();
                if (ffmpegProcess.ExitCode != 0)
                    UnityEngine.Debug.LogError($"FFmpeg exited with code {ffmpegProcess.ExitCode}: {stderr}");
                else if (!string.IsNullOrWhiteSpace(stderr))
                    UnityEngine.Debug.Log(stderr);
            }

            CleanupProcess();
            disposed = true;
        }

        public void Dispose() => End();

        private static string ResolveFfmpegExecutable()
        {
            string overridePath = Environment.GetEnvironmentVariable("FFMPEG_PATH");
            if (!string.IsNullOrWhiteSpace(overridePath))
                return overridePath;

            return "ffmpeg";
        }

        private void CleanupProcess()
        {
            stdin = null;
            if (ffmpegProcess == null)
                return;

            if (!ffmpegProcess.HasExited)
            {
                try { ffmpegProcess.Kill(); }
                catch (Exception ex) { UnityEngine.Debug.LogWarning($"FFmpeg kill warning: {ex.Message}"); }
            }

            ffmpegProcess.Dispose();
            ffmpegProcess = null;
        }
    }
}
#endif
