using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BattleReplayer : MonoBehaviour
{
    private BattleController battleController;

    private List<TimelineEvent> timelineEvents;
    private float battleDuration;

    private PlayerReplayState prevPlayerState1;
    private PlayerReplayState prevPlayerState2;
    private float player1CastSpeed = 1f;
    private float player2CastSpeed = 1f;

    private GameObject activeStunVFX1;
    private GameObject activeStunVFX2;
    private int lastVFXEventIndex1 = 0;
    private int lastVFXEventIndex2 = 0;

    private string currentAnim1 = "";
    private string currentAnim2 = "";

    public static readonly Dictionary<TimelineEventType, int> EventPriority = new Dictionary<TimelineEventType, int>
    {
        { TimelineEventType.CastInterrupted, 0 },
        { TimelineEventType.Hit, 1 },
        { TimelineEventType.HealApplied, 2 },
        { TimelineEventType.ShieldApplied, 3 },
        { TimelineEventType.CastFinished, 4 },
        { TimelineEventType.StunApplied, 5 },
        { TimelineEventType.StunAttemptFailed, 6 },
        { TimelineEventType.StunEnded, 7 },
        { TimelineEventType.StartCasting, 8 },
        { TimelineEventType.ResumeCasting, 9 },
        { TimelineEventType.CooldownExpired, 10 },
        { TimelineEventType.BattleEnd, 11 }
    };

    private void Awake() => battleController = GetComponent<BattleController>();

    public void Initialize(List<TimelineEvent> events)
    {
        timelineEvents = events;
        battleDuration = events.Count > 0 ? events[events.Count - 1].timestamp : 0f;
        lastVFXEventIndex1 = 0;
        lastVFXEventIndex2 = 0;
    }

    public void UpdateAtTime(float time)
    {
        PlayerReplayState s1 = GetPlayerReplayState(1, time);
        PlayerReplayState s2 = GetPlayerReplayState(2, time);

        if (s1.hp <= 0 && s2.hp > 0) s2.state = PlayerReplayState.State.Won;
        else if (s2.hp <= 0 && s1.hp > 0) s1.state = PlayerReplayState.State.Won;

        battleController.player1UI.UpdateHP(s1.hp, s1.shield);
        battleController.player2UI.UpdateHP(s2.hp, s2.shield);
        ApplyReplayStateToUI(battleController.player1UI, s1);
        ApplyReplayStateToUI(battleController.player2UI, s2);

        UpdateAnimator(battleController.player1Animator, s1, ref prevPlayerState1, ref player1CastSpeed, 1);
        UpdateAnimator(battleController.player2Animator, s2, ref prevPlayerState2, ref player2CastSpeed, 2);

        ProcessVFXEvents(1, time);
        ProcessVFXEvents(2, time);

        prevPlayerState1 = s1;
        prevPlayerState2 = s2;
    }

    private void ApplyReplayStateToUI(PlayerUI ui, PlayerReplayState st)
    {
        string text;
        float progress = -1f;
        if (st.hp <= 0) text = "Dead";
        else if (st.state == PlayerReplayState.State.Won) text = "Won";
        else if (st.state == PlayerReplayState.State.Casting) { text = $"Casting {st.abilityName}"; progress = st.castProgress; }
        else if (st.state == PlayerReplayState.State.Stunned) { text = "Stunned"; progress = st.stunProgress; }
        else text = "Idle";
        ui.UpdateState(text, progress);
    }

    private void ProcessVFXEvents(int playerIdx, float currentTime)
    {
        ref int lastIdx = ref (playerIdx == 1 ? ref lastVFXEventIndex1 : ref lastVFXEventIndex2);
        GameObject playerCube = playerIdx == 1 ? battleController.player1Cube : battleController.player2Cube;
        PlayerUI playerUI = playerIdx == 1 ? battleController.player1UI : battleController.player2UI;
        Vector3 localPos = playerIdx == 1 ? battleController.stunVFXLocalPosPlayer1 : battleController.stunVFXLocalPosPlayer2;

        for (int i = lastIdx; i < timelineEvents.Count; i++)
        {
            TimelineEvent ev = timelineEvents[i];
            if (ev.timestamp > currentTime) break;

            if (ev.targetPlayer == playerIdx)
            {
                switch (ev.eventType)
                {
                    case TimelineEventType.StunApplied:
                        if (activeStunVFXForPlayer(playerIdx) == null && battleController.stunVFXPrefab != null)
                        {
                            GameObject vfx = Instantiate(battleController.stunVFXPrefab, playerUI.transform);
                            vfx.transform.localPosition = localPos;
                            vfx.transform.localScale = battleController.stunVFXLocalScale;
                            SetStunVFX(playerIdx, vfx);
                        }
                        break;
                    case TimelineEventType.Hit when ev.damage > 0:
                        SpawnDamageText(playerCube, ev.damage);
                        break;
                    case TimelineEventType.HealApplied:
                        SpawnHealText(playerCube, ev.damage);
                        break;
                    case TimelineEventType.ShieldApplied:
                        SpawnShieldText(playerCube, ev.damage);
                        break;
                }
            }
            if (ev.actorPlayer == playerIdx && ev.eventType == TimelineEventType.StunEnded)
                DestroyStunVFX(playerIdx);

            lastIdx = i + 1;
        }
    }

    private GameObject activeStunVFXForPlayer(int p) => p == 1 ? activeStunVFX1 : activeStunVFX2;
    private void SetStunVFX(int p, GameObject vfx) { if (p == 1) activeStunVFX1 = vfx; else activeStunVFX2 = vfx; }
    private void DestroyStunVFX(int p) { var v = activeStunVFXForPlayer(p); if (v) { Destroy(v); if (p == 1) activeStunVFX1 = null; else activeStunVFX2 = null; } }

    private void SpawnDamageText(GameObject targetCube, int damage) => SpawnFloatingText(targetCube, damage.ToString(), Color.red);
    private void SpawnHealText(GameObject targetCube, int heal) => SpawnFloatingText(targetCube, $"+{heal} hp", Color.green);
    private void SpawnShieldText(GameObject targetCube, int shield) => SpawnFloatingText(targetCube, $"+{shield} es", Color.cyan);

    private void SpawnFloatingText(GameObject targetCube, string text, Color color)
    {
        if (battleController.damageTextPrefab == null) return;
        Vector3 pos = targetCube.transform.position + Vector3.up * 2f;
        GameObject obj = Instantiate(battleController.damageTextPrefab, pos, Quaternion.identity);
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
        Vector3 targetPos = startPos + new Vector3(UnityEngine.Random.Range(-0.5f, 0.5f), 1.5f, 0);
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

    private float GetAnimationLength(Animator anim, string stateName)
    {
        if (anim == null || anim.runtimeAnimatorController == null) return 1f;
        foreach (var clip in anim.runtimeAnimatorController.animationClips)
            if (clip.name == stateName) return clip.length;
        return 1f;
    }

    private void UpdateAnimator(Animator anim, PlayerReplayState state, ref PlayerReplayState prevState, ref float castSpeed, int playerIdx)
    {
        if (!anim) return;
        string targetAnim = null;
        float targetSpeed = 1f;
        bool freezeAnim = false, newCast = false;

        if (state.hp <= 0)
        {
            targetAnim = "Death";
        }
        else if (state.state == PlayerReplayState.State.Won)
        {
            targetAnim = "Idle";
        }
        else if (state.state == PlayerReplayState.State.Stunned)
        {
            if (state.stunProgress >= 0f)
            {
                targetSpeed = 0f;
                freezeAnim = true;
            }
            else
            {
                targetAnim = "Stun";
            }
        }
        else if (state.state == PlayerReplayState.State.Casting)
        {
            // Получаем данные способности
            AbilityDataSO data = BattleSimulator.GetAbilityByName(state.abilityName,
                playerIdx == 1 ? battleController.player1Abilities : battleController.player2Abilities,
                battleController.autoAttackData);

            // Всегда используем анимацию из ScriptableObject
            if (data != null && data.animationClip != null)
            {
                targetAnim = data.animationClip.name;
            }
            else
            {
                // Запасной вариант, если анимация не назначена
                targetAnim = "Idle";
            }

            // Скорость анимации
            float clipLen = data?.animationClip ? data.animationClip.length : GetAnimationLength(anim, targetAnim);
            float castTime = data?.castTime ?? 1f;
            targetSpeed = castTime > 0 ? clipLen / castTime : 1f;
            if (targetSpeed <= 0) targetSpeed = 1f;

            // Перезапуск анимации при новом касте
            if (prevState.state != PlayerReplayState.State.Casting)
            {
                newCast = true;
                if (prevState.state == PlayerReplayState.State.Stunned && prevState.castSessionId == state.castSessionId)
                    newCast = false;
            }
            else if (prevState.castSessionId != state.castSessionId)
            {
                newCast = true;
            }
        }
        else
        {
            targetAnim = "Idle";
        }

        // Применяем анимацию
        string current = playerIdx == 1 ? currentAnim1 : currentAnim2;
        if (freezeAnim)
        {
            // Заморозка (стан во время каста) – анимация не меняется
        }
        else if (state.state == PlayerReplayState.State.Casting && newCast)
        {
            if (targetAnim != null)
            {
                anim.CrossFade(targetAnim, 0.1f, 0, 0f);
                if (playerIdx == 1) currentAnim1 = targetAnim;
                else currentAnim2 = targetAnim;
            }
        }
        else if (targetAnim != null && current != targetAnim)
        {
            anim.CrossFade(targetAnim, 0.1f, 0, 0f);
            if (playerIdx == 1) currentAnim1 = targetAnim;
            else currentAnim2 = targetAnim;
        }

        anim.speed = targetSpeed;
    }

    private PlayerReplayState GetPlayerReplayState(int playerIdx, float time)
    {
        PlayerReplayState res = new PlayerReplayState { hp = 100, shield = 0, state = PlayerReplayState.State.Idle, stunProgress = -1, castSessionId = 0 };
        List<TimelineEvent> relevant = new List<TimelineEvent>();
        foreach (var ev in timelineEvents) if (ev.actorPlayer == playerIdx || ev.targetPlayer == playerIdx) relevant.Add(ev);
        relevant.Sort((a, b) =>
        {
            int c = a.timestamp.CompareTo(b.timestamp);
            if (c == 0) { int pa = EventPriority.TryGetValue(a.eventType, out int va) ? va : 99; int pb = EventPriority.TryGetValue(b.eventType, out int vb) ? vb : 99; c = pa.CompareTo(pb); }
            return c;
        });

        bool casting = false; float castStart = 0, castDuration = 0; string castAbility = ""; float stunEnd = -1, stunProgress = -1;
        int sessionId = 0, interruptedId = -1;
        int shield = 0, hp = 100;

        foreach (var ev in relevant)
        {
            if (ev.timestamp > time) break;
            if (ev.targetPlayer == playerIdx)
            {
                switch (ev.eventType)
                {
                    case TimelineEventType.Hit when ev.damage > 0:
                        int remaining = ev.damage;
                        int absorb = Mathf.Min(shield, remaining);
                        shield -= absorb; remaining -= absorb;
                        hp = ev.targetHPLeft;
                        break;
                    case TimelineEventType.HealApplied:
                        hp = ev.targetHPLeft;
                        break;
                    case TimelineEventType.ShieldApplied:
                        shield = ev.targetShieldLeft;
                        break;
                }
            }
            if (ev.actorPlayer == playerIdx)
            {
                switch (ev.eventType)
                {
                    case TimelineEventType.StartCasting:
                        casting = true; castStart = ev.timestamp; castAbility = ev.abilityName;
                        var ab = BattleSimulator.GetAbilityByName(ev.abilityName,
                            playerIdx == 1 ? battleController.player1Abilities : battleController.player2Abilities,
                            battleController.autoAttackData);
                        castDuration = ab?.castTime ?? 1f;
                        sessionId = ev.castSessionId; interruptedId = -1;
                        break;
                    case TimelineEventType.CastInterrupted:
                        if (casting) { stunProgress = Mathf.Clamp01((ev.timestamp - castStart) / castDuration); interruptedId = ev.castSessionId; casting = false; }
                        break;
                    case TimelineEventType.CastFinished: casting = false; stunProgress = -1; break;
                    case TimelineEventType.ResumeCasting:
                        casting = true; castStart = ev.timestamp - (castDuration - ev.remainingCastTime); castAbility = ev.abilityName;
                        stunProgress = -1; sessionId = ev.castSessionId;
                        break;
                    case TimelineEventType.StunEnded: stunEnd = -1; break;
                }
            }
            if (ev.targetPlayer == playerIdx && ev.eventType == TimelineEventType.StunApplied)
            {
                var stunAb = BattleSimulator.GetAbilityByName(ev.abilityName,
                    playerIdx == 1 ? battleController.player1Abilities : battleController.player2Abilities,
                    battleController.autoAttackData);
                stunEnd = ev.timestamp + (stunAb?.stunDuration ?? 0.5f);
                if (casting) { stunProgress = Mathf.Clamp01((ev.timestamp - castStart) / castDuration); interruptedId = sessionId; casting = false; }
            }
        }

        res.hp = hp;
        res.shield = shield;
        if (stunEnd > time) { res.state = PlayerReplayState.State.Stunned; res.stunProgress = stunProgress; if (stunProgress >= 0) { res.abilityName = castAbility; res.castSessionId = interruptedId; } }
        else if (casting) { res.state = PlayerReplayState.State.Casting; res.castProgress = Mathf.Clamp01((time - castStart) / castDuration); res.abilityName = castAbility; res.castSessionId = sessionId; }
        return res;
    }

    private struct PlayerReplayState
    {
        public enum State { Idle, Casting, Stunned, Won }
        public int hp, shield;
        public State state;
        public string abilityName;
        public float castProgress, stunProgress;
        public int castSessionId;
    }

    public void ClearVFX()
    {
        if (activeStunVFX1) Destroy(activeStunVFX1);
        if (activeStunVFX2) Destroy(activeStunVFX2);
        activeStunVFX1 = activeStunVFX2 = null;
    }
}