using UnityEngine;

namespace TPD.Arena
{
    public static class BattleVideoEncoderRuntimeRegistration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
#if !UNITY_EDITOR && !UNITY_WEBGL
            BattleVideoEncoderProvider.SetFactory(() => new FFmpegVideoEncoder());
#endif
        }

        public static void RegisterFfmpegEncoder()
        {
#if !UNITY_WEBGL
            BattleVideoEncoderProvider.SetFactory(() => new FFmpegVideoEncoder());
#endif
        }
    }
}
