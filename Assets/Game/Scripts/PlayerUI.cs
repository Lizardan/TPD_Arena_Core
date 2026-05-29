using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerUI : MonoBehaviour
{
    public TextMeshProUGUI hpText;
    public TextMeshProUGUI stateText;
    public Image castBarFill;

    private int maxHP = 100;

    public void UpdateHP(int currentHP, int shield = 0)
    {
        string shieldText = shield > 0 ? $" (+{shield})" : "";
        hpText.text = $"HP: {currentHP}/{maxHP}{shieldText}";
    }

    public void UpdateState(string state, float castProgress = -1f)
    {
        stateText.text = state;
        if (castBarFill)
        {
            if (castProgress >= 0f) { castBarFill.gameObject.SetActive(true); castBarFill.fillAmount = castProgress; }
            else castBarFill.gameObject.SetActive(false);
        }
    }
}