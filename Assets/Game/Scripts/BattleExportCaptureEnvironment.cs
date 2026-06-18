using System;

namespace TPD.Arena
{
    public static class BattleExportCaptureEnvironment
    {
        private static Func<int, int, bool> applyResolution;
        private static Action restoreResolution;
        private static Action<int, int> reportProgress;
        private static Action clearProgress;

        public static void SetApplyResolution(Func<int, int, bool> apply) => applyResolution = apply;

        public static void SetRestoreResolution(Action restore) => restoreResolution = restore;

        public static void SetReportProgress(Action<int, int> report) => reportProgress = report;

        public static void SetClearProgress(Action clear) => clearProgress = clear;

        public static bool TryApplyResolution(int width, int height) =>
            applyResolution != null && applyResolution(width, height);

        public static void RestoreResolution() => restoreResolution?.Invoke();

        public static void ReportProgress(int current, int total) => reportProgress?.Invoke(current, total);

        public static void ClearProgress() => clearProgress?.Invoke();
    }
}
