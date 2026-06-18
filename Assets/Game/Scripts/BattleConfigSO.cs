using UnityEngine;



namespace TPD.Arena

{

    [CreateAssetMenu(fileName = "BattleConfig", menuName = "Battle/Battle Config")]

    public class BattleConfigSO : ScriptableObject

    {

        public int maxHealth = 100;

        [Header("Battle Request")]
        public BattleAbilityRegistry abilityRegistry;



        [Header("Video Export")]

        public int exportFps = 30;

        public int exportWidth = 1920;

        public int exportHeight = 1080;

        [Tooltip("Optional H.264 bitrate override in Mbps. 0 = auto (~0.8 Mbps at 512²). Raise to 1–2 if artifacts return.")]
        public int exportVideoBitrateMbps = 0;

        public string outputDirectory = "Exports";

        public string fileNamePattern = "battle_{0:yyyyMMdd_HHmmss}.mp4";

    }

}

