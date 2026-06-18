using System;
using System.Collections.Generic;
using UnityEngine;

namespace TPD.Arena
{
    [CreateAssetMenu(fileName = "BattleAbilityRegistry", menuName = "Battle/Ability Registry")]
    public class BattleAbilityRegistry : ScriptableObject
    {
        public AbilityDataSO[] abilities;

        public bool TryResolve(string abilityName, out AbilityDataSO ability)
        {
            ability = null;
            if (string.IsNullOrWhiteSpace(abilityName) || abilities == null)
                return false;

            foreach (AbilityDataSO candidate in abilities)
            {
                if (candidate != null && candidate.abilityName == abilityName)
                {
                    ability = candidate;
                    return true;
                }
            }

            return false;
        }

        public bool TryResolveMany(string[] abilityNames, out AbilityDataSO[] resolved, out string error)
        {
            resolved = Array.Empty<AbilityDataSO>();
            error = null;

            if (abilityNames == null || abilityNames.Length == 0)
                return true;

            var list = new List<AbilityDataSO>(abilityNames.Length);
            foreach (string abilityName in abilityNames)
            {
                if (!TryResolve(abilityName, out AbilityDataSO ability))
                {
                    error = $"Unknown ability: {abilityName}";
                    return false;
                }

                list.Add(ability);
            }

            resolved = list.ToArray();
            return true;
        }
    }
}
