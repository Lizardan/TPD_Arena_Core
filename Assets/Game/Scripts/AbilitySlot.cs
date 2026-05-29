using System;

[Serializable]
public class AbilitySlot
{
    public AbilityDataSO abilityData;
    [UnityEngine.HideInInspector] public float cooldownRemaining;

    public AbilitySlot(AbilityDataSO data)
    {
        abilityData = data;
        cooldownRemaining = 0;
    }
}