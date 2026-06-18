using UnityEngine;

namespace TPD.Arena
{
    public enum AbilityType { Damage, Heal, Shield, Stun }

    [CreateAssetMenu(fileName = "NewAbility", menuName = "Abilities/Ability Data")]
    public class AbilityDataSO : ScriptableObject
    {
        public string abilityName = "New Ability";
        public AbilityType type = AbilityType.Damage;
        public float castTime = 1f;
        public float cooldown = 3f;

        public int damage = 10;
        public float stunDuration = 0f;

        public Color displayColor = Color.white;
        public AnimationClip animationClip;

        public GameObject castVfxPrefab;
        public GameObject hitVfxPrefab;
        public Vector3 hitVfxOffset = Vector3.up;
        public Vector3 vfxLocalScale = new Vector3(0.525f, 0.525f, 0.525f);

        public bool IsStun => type == AbilityType.Stun;
    }
}
