using System;
using UnityEngine;

namespace TPD.Arena
{
    public interface IBattleVideoEncoder
    {
        bool Begin(string outputPath, int width, int height, int fps, uint targetBitRate = 0);
        void AddFrame(Texture2D frame);
        void End();
    }

    public static class BattleVideoEncoderProvider
    {
        private static Func<IBattleVideoEncoder> factory;
        private static Action<string> exportCompletedHandler;

        public static void SetFactory(Func<IBattleVideoEncoder> encoderFactory)
        {
            factory = encoderFactory;
        }

        public static void SetExportCompletedHandler(Action<string> handler)
        {
            exportCompletedHandler = handler;
        }

        public static IBattleVideoEncoder Create() => factory?.Invoke();

        public static void NotifyExportCompleted(string path) => exportCompletedHandler?.Invoke(path);
    }
}
