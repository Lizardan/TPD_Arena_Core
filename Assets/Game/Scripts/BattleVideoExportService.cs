using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace TPD.Arena
{
    public class BattleVideoExportService
    {
        public struct ExportRequest
        {
            public List<TimelineEvent> timelineEvents;
            public float battleDuration;
            public int maxHealth;
            public int p1MaxHealth;
            public int p2MaxHealth;
            public string outputPathOverride;
            public BattleConfigSO config;
            public BattleReplayer replayer;
            public Slider timelineSlider;
            public Camera captureCamera;
            public AudioListener audioListener;
            public Action<int, int> onProgress;
            public Action onExportBegin;
            public Action onExportEnd;
            public Action onBeforeFrameCapture;
            public Action onAfterFrameCapture;
        }

        public struct ExportResult
        {
            public bool success;
            public string outputPath;
            public string error;
        }

        public static string BuildOutputPath(BattleConfigSO config)
        {
            string fileName = string.Format(config.fileNamePattern, DateTime.Now);
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string directory = Path.IsPathRooted(config.outputDirectory)
                ? config.outputDirectory
                : Path.Combine(projectRoot, config.outputDirectory);
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, fileName);
        }

        public static ExportResult ExportBlocking(ExportRequest request)
        {
            var result = new ExportResult();

            if (request.config == null)
            {
                result.error = "Battle config is missing.";
                return result;
            }

            IBattleVideoEncoder encoder = BattleVideoEncoderProvider.Create();
            if (encoder == null)
            {
                result.error = "Video encoder is not available.";
                return result;
            }

            if (request.timelineEvents == null || request.timelineEvents.Count == 0)
            {
                result.error = "Battle timeline is empty.";
                return result;
            }

            Camera captureCamera = request.captureCamera != null
                ? request.captureCamera
                : Camera.main;
            if (captureCamera == null)
            {
                result.error = "Capture camera is missing.";
                return result;
            }

            int p1Max = request.p1MaxHealth > 0 ? request.p1MaxHealth : request.maxHealth;
            int p2Max = request.p2MaxHealth > 0 ? request.p2MaxHealth : request.maxHealth;
            if (p1Max <= 0) p1Max = 100;
            if (p2Max <= 0) p2Max = p1Max;

            int fps = Mathf.Max(1, request.config.exportFps);
            int width = Mathf.Max(16, request.config.exportWidth);
            int height = Mathf.Max(16, request.config.exportHeight);
            uint targetBitRate = BattleExportBitrate.Resolve(
                width, height, fps, request.config.exportVideoBitrateMbps);
            string outputPath = string.IsNullOrWhiteSpace(request.outputPathOverride)
                ? BuildOutputPath(request.config)
                : request.outputPathOverride;

            bool audioWasEnabled = request.audioListener != null && request.audioListener.enabled;
            if (request.audioListener != null)
                request.audioListener.enabled = false;

            var frameRenderer = new BattleFrameRenderer(
                request.replayer,
                request.timelineSlider,
                request.timelineEvents,
                p1Max,
                p2Max);

            int frameCount = Mathf.CeilToInt(request.battleDuration * fps);
            if (!encoder.Begin(outputPath, width, height, fps, targetBitRate))
            {
                RestoreCaptureState(request, audioWasEnabled);
                result.error = "Failed to start video encoder.";
                return result;
            }

            request.onExportBegin?.Invoke();
            Canvas.ForceUpdateCanvases();

            if (!frameRenderer.BeginExport(captureCamera, width, height))
            {
                frameRenderer.EndExport();
                encoder.End();
                RestoreCaptureState(request, audioWasEnabled);
                result.error = "Failed to start offscreen capture.";
                return result;
            }

            request.replayer.BeginExportAnimators();

            for (int i = 0; i <= frameCount; i++)
            {
                float time = Mathf.Min(i / (float)fps, request.battleDuration);
                frameRenderer.ApplyFrame(time, updateTimelineSlider: false);

                if (i == 0 || i == frameCount || i % 15 == 0)
                {
                    request.onProgress?.Invoke(i, frameCount);
                    BattleExportCaptureEnvironment.ReportProgress(i, frameCount);
                }

                request.onBeforeFrameCapture?.Invoke();
                Texture2D captured = frameRenderer.CaptureFrame();
                if (captured != null)
                    encoder.AddFrame(captured);
            }

            frameRenderer.EndExport();
            encoder.End();
            RestoreCaptureState(request, audioWasEnabled);

            result.success = true;
            result.outputPath = outputPath;
            return result;
        }

        public static IEnumerator ExportCoroutine(ExportRequest request, Action<ExportResult> onComplete)
        {
            ExportResult result = ExportBlocking(request);
            onComplete?.Invoke(result);
            yield break;
        }

        private static void RestoreCaptureState(ExportRequest request, bool audioWasEnabled)
        {
            request.replayer?.EndExportAnimators();
            BattleExportCaptureEnvironment.ClearProgress();
            request.onExportEnd?.Invoke();
            request.onAfterFrameCapture?.Invoke();

            if (request.audioListener != null)
                request.audioListener.enabled = audioWasEnabled;
        }
    }
}
