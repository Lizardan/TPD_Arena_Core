using System;
using UnityEditor;
using UnityEditor.Media;
using UnityEngine;

namespace TPD.Arena.Editor
{
    public class MediaEncoderVideoEncoder : IBattleVideoEncoder
    {
        private MediaEncoder encoder;

        public bool Begin(string outputPath, int width, int height, int fps, uint targetBitRate = 0)
        {
            try
            {
                if (targetBitRate > 0)
                {
                    bool compactExport = (long)width * height < 1920L * 1080L;
                    var h264Attr = new H264EncoderAttributes
                    {
                        gopSize = (uint)Mathf.Max(1, fps * 2),
                        profile = compactExport
                            ? VideoEncodingProfile.H264Main
                            : VideoEncodingProfile.H264High
                    };
                    var videoAttrs = new VideoTrackEncoderAttributes(h264Attr)
                    {
                        width = (uint)width,
                        height = (uint)height,
                        frameRate = new MediaRational(fps),
                        includeAlpha = false,
                        targetBitRate = targetBitRate
                    };
                    encoder = new MediaEncoder(outputPath, videoAttrs);
                }
                else
                {
                    var videoAttrs = new VideoTrackAttributes
                    {
                        width = (uint)width,
                        height = (uint)height,
                        frameRate = new MediaRational(fps),
                        includeAlpha = false
                    };
                    encoder = new MediaEncoder(outputPath, videoAttrs);
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"MediaEncoder init failed: {ex.Message}");
                encoder = null;
                return false;
            }
        }

        public void AddFrame(Texture2D frame)
        {
            if (encoder == null || frame == null)
                return;

            encoder.AddFrame(frame);
        }

        public void End()
        {
            if (encoder == null)
                return;

            encoder.Dispose();
            encoder = null;
        }
    }

    public static class BattleExportEditorUtility
    {
        public static void RevealInFinder(string path)
        {
            if (string.IsNullOrEmpty(path))
                return;

            EditorUtility.RevealInFinder(path);
        }
    }

    [InitializeOnLoad]
    static class BattleVideoEncoderRegistration
    {
        static BattleVideoEncoderRegistration()
        {
            BattleVideoEncoderProvider.SetFactory(() => new MediaEncoderVideoEncoder());
            BattleVideoEncoderProvider.SetExportCompletedHandler(BattleExportEditorUtility.RevealInFinder);
            BattleExportCaptureEnvironment.SetApplyResolution(BattleExportGameViewHelper.TrySetResolution);
            BattleExportCaptureEnvironment.SetRestoreResolution(BattleExportGameViewHelper.Restore);
            BattleExportCaptureEnvironment.SetReportProgress(BattleExportGameViewHelper.ReportProgress);
            BattleExportCaptureEnvironment.SetClearProgress(BattleExportGameViewHelper.ClearProgress);
        }
    }
}
