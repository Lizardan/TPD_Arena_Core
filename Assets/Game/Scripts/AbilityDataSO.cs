using UnityEngine;

public enum AbilityType { Damage, Heal, Shield, Stun }

[CreateAssetMenu(fileName = "NewAbility", menuName = "Abilities/Ability Data")]
public class AbilityDataSO : ScriptableObject
{
    public string abilityName = "New Ability";
    public AbilityType type = AbilityType.Damage;
    public float castTime = 1f;
    public float cooldown = 3f;

    // Универсальное значение:
    // Damage/Stun -> урон, Heal -> лечение, Shield -> объём щита
    public int damage = 10;
    public float stunDuration = 0f;   // >0 означает стан

    public Color displayColor = Color.white;
    public AnimationClip animationClip;

    public bool IsStun => type == AbilityType.Stun;
}