using System;
using System.Collections.Generic;
using UnityEngine;

public class BattleReplayer : MonoBehaviour
{
    private BattleController battleController;

    private List<TimelineEvent> timelineEvents;
    private float battleDuration;

    private PlayerReplayState prevPlayerState1;
    private PlayerReplayState prevPlayerState2;
    private float player1CastSpeed = 1f;
    private float player2CastSpeed = 1f;

    private string currentAnim1 = "";
    private string currentAnim2 = "";

    private int lastEventIndex1 = 0;
    private int lastEventIndex2 = 0;

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
        lastEventIndex1 = 0;
        lastEventIndex2 = 0;
    }

    public void UpdateAtTime(float time)
    {
        PlayerReplayState s1 = GetPlayerReplayState(1, time);
        PlayerReplayState s2 = GetPlayerReplayState(2, time);

        if (s1.hp <= 0 && s2.hp > 0) s2.state = PlayerReplayState.State.Won;
        else if (s2.hp <= 0 && s1.hp > 0) s1.state = PlayerReplayState.State.Won;

        PlayerController p1 = battleController.player1;
        PlayerController p2 = battleController.player2;

        p1.UpdateHP(s1.hp, s1.shield);
        p2.UpdateHP(s2.hp, s2.shield);
        ApplyReplayStateToUI(p1, s1);
        ApplyReplayStateToUI(p2, s2);

        UpdateAnimator(p1.animator, s1, ref prevPlayerState1, ref player1CastSpeed, 1);
        UpdateAnimator(p2.animator, s2, ref prevPlayerState2, ref player2CastSpeed, 2);

        ProcessEvents(1, time);
        ProcessEvents(2, time);

        prevPlayerState1 = s1;
        prevPlayerState2 = s2;
    }

    private void ApplyReplayStateToUI(PlayerController ui, PlayerReplayState st)
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

    private void ProcessEvents(int playerIdx, float currentTime)
    {
        ref int lastIdx = ref (playerIdx == 1 ? ref lastEventIndex1 : ref lastEventIndex2);
        PlayerController pc = playerIdx == 1 ? battleController.player1 : battleController.player2;

        for (int i = lastIdx; i < timelineEvents.Count; i++)
        {
            TimelineEvent ev = timelineEvents[i];
            if (ev.timestamp > currentTime) break;

            if (ev.targetPlayer == playerIdx)
            {
                switch (ev.eventType)
                {
                    case TimelineEventType.StunApplied:
                        pc.vfx.ShowStun();
                        break;
                    case TimelineEventType.Hit when ev.damage > 0:
                        pc.vfx.SpawnDamageText(ev.damage);
                        break;
                    case TimelineEventType.HealApplied:
                        pc.vfx.SpawnHealText(ev.damage);
                        break;
                    case TimelineEventType.ShieldApplied:
                        pc.vfx.SpawnShieldText(ev.damage);
                        break;
                }
            }
            if (ev.actorPlayer == playerIdx && ev.eventType == TimelineEventType.StunEnded)
                pc.vfx.HideStun();

            lastIdx = i + 1;
        }
    }

    private void UpdateAnimator(Animator anim, PlayerReplayState state, ref PlayerReplayState prevState, ref float castSpeed, int playerIdx)
    {
        if (!anim) return;
        string targetAnim = null;
        float targetSpeed = 1f;
        bool freezeAnim = false, newCast = false;

        if (state.hp <= 0) targetAnim = "Death";
        else if (state.state == PlayerReplayState.State.Won) targetAnim = "Idle";
        else if (state.state == PlayerReplayState.State.Stunned)
        {
            if (state.stunProgress >= 0f) { targetSpeed = 0f; freezeAnim = true; }
            else targetAnim = "Stun";
        }
        else if (state.state == PlayerReplayState.State.Casting)
        {
            PlayerController pc = playerIdx == 1 ? battleController.player1 : battleController.player2;
            AbilityDataSO data = BattleSimulator.GetAbilityByName(state.abilityName, pc.abilities, pc.autoAttack);

            targetAnim = data.animationClip ? data.animationClip.name : "Idle";

            float clipLen = data.animationClip ? data.animationClip.length : 1f;
            float castTime = data.castTime;
            targetSpeed = castTime > 0 ? clipLen / castTime : 1f;
            if (targetSpeed <= 0) targetSpeed = 1f;

            if (prevState.state != PlayerReplayState.State.Casting)
            {
                newCast = true;
                if (prevState.state == PlayerReplayState.State.Stunned && prevState.castSessionId == state.castSessionId)
                    newCast = false;
            }
            else if (prevState.castSessionId != state.castSessionId)
                newCast = true;
        }
        else targetAnim = "Idle";

        string current = playerIdx == 1 ? currentAnim1 : currentAnim2;
        if (freezeAnim) { }
        else if (state.state == PlayerReplayState.State.Casting && newCast)
        {
            if (targetAnim != null)
            {
                anim.CrossFade(targetAnim, 0.1f, 0, 0f);
                if (playerIdx == 1) currentAnim1 = targetAnim; else currentAnim2 = targetAnim;
            }
        }
        else if (targetAnim != null && current != targetAnim)
        {
            anim.CrossFade(targetAnim, 0.1f, 0, 0f);
            if (playerIdx == 1) currentAnim1 = targetAnim; else currentAnim2 = targetAnim;
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
        PlayerController pc = playerIdx == 1 ? battleController.player1 : battleController.player2;

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
                        var ab = BattleSimulator.GetAbilityByName(ev.abilityName, pc.abilities, pc.autoAttack);
                        castDuration = ab.castTime;
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
                var stunAb = BattleSimulator.GetAbilityByName(ev.abilityName, pc.abilities, pc.autoAttack);
                stunEnd = ev.timestamp + stunAb.stunDuration;
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
        battleController.player1.vfx.ClearVFX();
        battleController.player2.vfx.ClearVFX();
    }
}