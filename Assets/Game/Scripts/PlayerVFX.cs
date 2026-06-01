using System.Collections;
using UnityEngine;
using TMPro;

public class PlayerVFX : MonoBehaviour
{
    [SerializeField] GameObject damageTextPrefab;
    [SerializeField] GameObject stunVFXPrefab;
    [SerializeField] Vector3 stunLocalPos = new Vector3(0, 1.25f, -1);
    [SerializeField] Vector3 stunLocalScale = new Vector3(0.5f, 0.5f, 0.5f);

    private GameObject activeStunVFX;

    public void SpawnDamageText(int damage) => SpawnFloatingText(damage.ToString(), Color.red);
    public void SpawnHealText(int heal) => SpawnFloatingText($"+{heal} hp", Color.green);
    public void SpawnShieldText(int shield) => SpawnFloatingText($"+{shield} es", Color.cyan);

    public void ShowStun()
    {
        if (activeStunVFX == null && stunVFXPrefab != null)
        {
            activeStunVFX = Instantiate(stunVFXPrefab, transform);
            activeStunVFX.transform.localPosition = stunLocalPos;
            activeStunVFX.transform.localScale = stunLocalScale;
        }
    }

    public void HideStun()
    {
        if (activeStunVFX != null)
        {
            Destroy(activeStunVFX);
            activeStunVFX = null;
        }
    }

    private void SpawnFloatingText(string text, Color color)
    {
        if (damageTextPrefab == null) return;
        Vector3 pos = transform.position + Vector3.up * 2f;
        GameObject obj = Instantiate(damageTextPrefab, pos, Quaternion.identity);
        TextMeshProUGUI tmp = obj.GetComponent<TextMeshProUGUI>();
        if (tmp)
        {
            tmp.text = text;
            tmp.color = color;
        }
        StartCoroutine(FloatingTextRoutine(obj));
    }

    private IEnumerator FloatingTextRoutine(GameObject obj)
    {
        float duration = 1.2f;
        float elapsed = 0;
        Vector3 startPos = obj.transform.position;
        Vector3 targetPos = startPos + new Vector3(Random.Range(-0.5f, 0.5f), 1.5f, 0);
        TextMeshProUGUI tmp = obj.GetComponent<TextMeshProUGUI>();
        Color orig = tmp ? tmp.color : Color.white;
        while (elapsed < duration && obj)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            obj.transform.position = Vector3.Lerp(startPos, targetPos, t);
            if (tmp) { Color c = orig; c.a = Mathf.Lerp(1, 0, t); tmp.color = c; }
            yield return null;
        }
        if (obj) Destroy(obj);
    }

    public void ClearVFX() => HideStun();
}