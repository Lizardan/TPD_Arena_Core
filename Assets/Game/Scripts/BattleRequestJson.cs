using System;
using System.IO;
using UnityEngine;

namespace TPD.Arena
{
    [Serializable]
    public class BattleRequestJson
    {
        public const int MinHp = 1;
        public const int MaxHp = 999;

        public int leftHp = 100;
        public int rightHp = 100;
        public string[] leftAbilities;
        public string[] rightAbilities;

        public bool HasCustomLoadouts()
        {
            return (leftAbilities != null && leftAbilities.Length > 0)
                || (rightAbilities != null && rightAbilities.Length > 0);
        }

        public static BattleRequestJson Parse(string json, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "Battle JSON is empty.";
                return null;
            }

            BattleRequestJson request;
            try
            {
                request = JsonUtility.FromJson<BattleRequestJson>(json);
            }
            catch (Exception ex)
            {
                error = $"Invalid battle JSON: {ex.Message}";
                return null;
            }

            if (request == null)
            {
                error = "Battle JSON could not be parsed.";
                return null;
            }

            if (!request.TryValidate(out error))
                return null;

            return request;
        }

        public static BattleRequestJson LoadFromFile(string path, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                error = $"Battle JSON file not found: {path}";
                return null;
            }

            return Parse(File.ReadAllText(path), out error);
        }

        public bool TryValidate(out string error)
        {
            if (leftHp < MinHp || leftHp > MaxHp)
            {
                error = $"leftHp must be between {MinHp} and {MaxHp}.";
                return false;
            }

            if (rightHp < MinHp || rightHp > MaxHp)
            {
                error = $"rightHp must be between {MinHp} and {MaxHp}.";
                return false;
            }

            error = null;
            return true;
        }
    }
}
