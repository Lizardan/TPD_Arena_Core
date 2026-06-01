using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerController : MonoBehaviour
{
    [Header("UI")]
    public TextMeshProUGUI hpText;
    public TextMeshProUGUI stateText;
    public Image castBarFill;

    [Header("VFX")]
    public PlayerVFX vfx;

    [Header("Abilities")]
    public AbilityDataSO[] abilities;
    public AbilityDataSO autoAttack;

    [Header("Skin")]
    public GameObject[] skinPrefabs;
    public int skinIndex;
    public float yRotation = 0f;          // поворот скина

    [HideInInspector] public Animator animator; // заполняется автоматически

    private int maxHP = 100;

    private void Awake()
    {
        if (skinPrefabs != null && skinIndex >= 0 && skinIndex < skinPrefabs.Length)
        {
            GameObject skinInstance = Instantiate(skinPrefabs[skinIndex], transform);
            // Позиция остаётся такой, как в префабе (localPosition не переопределяем)
            skinInstance.transform.localRotation = Quaternion.Euler(0f, yRotation, 0f);
            animator = skinInstance.GetComponent<Animator>();
        }
        else
        {
            Debug.LogError($"Skin prefabs not configured correctly on {gameObject.name}");
        }
    }

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