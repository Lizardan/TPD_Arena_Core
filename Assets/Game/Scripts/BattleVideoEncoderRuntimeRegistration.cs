using UnityEngine;

namespace TPD.Arena
{
    public static class BattleVideoEncoderRuntimeRegistration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
#if !UNITY_EDITOR
            BattleVideoEncoderProvider.SetFactory(() => new FFmpegVideoEncoder());
#endif
        }

        public static void RegisterFfmpegEncoder()
        {
            BattleVideoEncoderProvider.SetFactory(() => new FFmpegVideoEncoder());
        }
    }
}
