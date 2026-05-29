using System.Collections.Generic;
using UnityEngine;

public static class BattleSimulator
{
    public static List<TimelineEvent> Simulate(AbilityDataSO[] player1Abilities, AbilityDataSO[] player2Abilities, AbilityDataSO autoAttackData)
    {
        List<TimelineEvent> timeline = new List<TimelineEvent>();
        AbilityDataSO[] p1Full = AddAutoAttack(player1Abilities, autoAttackData);
        AbilityDataSO[] p2Full = AddAutoAttack(player2Abilities, autoAttackData);
        CharacterSimState p1 = new CharacterSimState(p1Full);
        CharacterSimState p2 = new CharacterSimState(p2Full);
        List<SimEvent> eventQueue = new List<SimEvent>();

        int p1SessionCounter = 0;
        int p2SessionCounter = 0;

        void AddTimelineEvent(float time, TimelineEventType type, int actor, int target = 0,
            string abilityName = "", int slot = -1, int dmg = 0,
            float remainCast = 0f, int targetHP = -1, int targetShield = -1, int actorHP = -1, int sessionId = 0)
        {
            timeline.Add(new TimelineEvent
            {
                timestamp = time,
                eventType = type,
                actorPlayer = actor,
                targetPlayer = target,
                abilityName = abilityName,
                slotIndex = slot,
                damage = dmg,
                remainingCastTime = remainCast,
                targetHPLeft = targetHP,
                targetShieldLeft = targetShield,
                actorHPLeft = actorHP,
                castSessionId = sessionId
            });
        }

        void TryStartCasting(CharacterSimState ch, float time, int playerIdx)
        {
            if (ch.state != CharacterSimState.State.Idle) return;
            for (int i = 0; i < ch.slots.Length; i++)
            {
                if (ch.slots[i].cooldownRemaining <= 0f)
                {
                    AbilitySlot slot = ch.slots[i];
                    AbilityDataSO data = slot.abilityData;
                    ch.state = CharacterSimState.State.Casting;
                    ch.currentSlotIndex = i;
                    float castTime = data.castTime;
                    ch.castStartTime = time;
                    ch.castFinishTime = time + castTime;
                    ch.hitTime = time + castTime * 0.5f;
                    ch.remainingCastAfterStun = 0f;

                    int sessionId = (playerIdx == 1) ? ++p1SessionCounter : ++p2SessionCounter;
                    ch.currentCastSessionId = sessionId;
                    ch.interruptedCastSessionId = -1;

                    AddTimelineEvent(time, TimelineEventType.StartCasting, playerIdx,
                        abilityName: data.abilityName, slot: i, actorHP: ch.health, sessionId: sessionId);

                    SimEvent hitEvent = new SimEvent
                    {
                        time = ch.hitTime,
                        priority = 0,
                        type = SimEvent.SimEventType.Hit,
                        character = ch,
                        slotIndex = i,
                        slot = slot
                    };
                    SimEvent castEvent = new SimEvent
                    {
                        time = ch.castFinishTime,
                        priority = 1,
                        type = SimEvent.SimEventType.CastFinished,
                        character = ch,
                        slotIndex = i,
                        slot = slot
                    };
                    ch.activeHitEvent = hitEvent;
                    ch.activeCastFinishedEvent = castEvent;
                    eventQueue.Add(hitEvent);
                    eventQueue.Add(castEvent);
                    return;
                }
            }
        }

        void ProcessEvent(SimEvent ev, float time)
        {
            if (ev.cancelled) return;

            CharacterSimState ch = ev.character;
            int playerIdx = (ch == p1) ? 1 : 2;
            int opponentIdx = (playerIdx == 1) ? 2 : 1;
            CharacterSimState target = (ch == p1) ? p2 : p1;

            switch (ev.type)
            {
                case SimEvent.SimEventType.Hit:
                    if (ch.state != CharacterSimState.State.Casting || ch.currentSlotIndex != ev.slotIndex) return;
                    AbilitySlot slotHit = ch.slots[ev.slotIndex];
                    AbilityDataSO dataHit = slotHit.abilityData;
                    int sessionIdHit = ch.currentCastSessionId;

                    switch (dataHit.type)
                    {
                        case AbilityType.Damage:
                            int remainingDamage = dataHit.damage;
                            int absorbed = Mathf.Min(target.shield, remainingDamage);
                            target.shield -= absorbed;
                            remainingDamage -= absorbed;
                            target.health -= remainingDamage;

                            AddTimelineEvent(time, TimelineEventType.Hit, playerIdx,
                                target: opponentIdx, abilityName: dataHit.abilityName,
                                dmg: dataHit.damage, targetHP: target.health, targetShield: target.shield, sessionId: sessionIdHit);

                            if (target.health <= 0)
                                AddTimelineEvent(time, TimelineEventType.BattleEnd, opponentIdx, targetHP: target.health);
                            break;

                        case AbilityType.Heal:
                            int heal = dataHit.damage;
                            ch.health = Mathf.Min(ch.health + heal, CharacterSimState.MAX_HEALTH);
                            AddTimelineEvent(time, TimelineEventType.HealApplied, playerIdx,
                                target: playerIdx, abilityName: dataHit.abilityName,
                                dmg: heal, targetHP: ch.health, targetShield: ch.shield, sessionId: sessionIdHit);
                            break;

                        case AbilityType.Shield:
                            ch.shield = dataHit.damage;
                            AddTimelineEvent(time, TimelineEventType.ShieldApplied, playerIdx,
                                target: playerIdx, abilityName: dataHit.abilityName,
                                dmg: ch.shield, targetHP: ch.health, targetShield: ch.shield, sessionId: sessionIdHit);
                            break;

                        case AbilityType.Stun:
                            AddTimelineEvent(time, TimelineEventType.Hit, playerIdx,
                                target: opponentIdx, abilityName: dataHit.abilityName, sessionId: sessionIdHit);
                            if (target.state != CharacterSimState.State.Stunned)
                            {
                                eventQueue.Add(new SimEvent
                                {
                                    time = time,
                                    priority = 2,
                                    type = SimEvent.SimEventType.StunApplied,
                                    character = target,
                                    stunDuration = dataHit.stunDuration,
                                    stunAbilityName = dataHit.abilityName
                                });
                            }
                            else
                                AddTimelineEvent(time, TimelineEventType.StunAttemptFailed, playerIdx,
                                    target: opponentIdx, abilityName: dataHit.abilityName);
                            break;
                    }
                    ch.activeHitEvent = null;
                    break;

                case SimEvent.SimEventType.CastFinished:
                    if (ch.state != CharacterSimState.State.Casting || ch.currentSlotIndex != ev.slotIndex) return;
                    AbilitySlot slotFin = ch.slots[ev.slotIndex];
                    AbilityDataSO dataFin = slotFin.abilityData;
                    int sessionIdFin = ch.currentCastSessionId;

                    AddTimelineEvent(time, TimelineEventType.CastFinished, playerIdx,
                        target: 0, abilityName: dataFin.abilityName,
                        slot: ev.slotIndex, actorHP: ch.health, sessionId: sessionIdFin);

                    slotFin.cooldownRemaining = dataFin.cooldown;
                    eventQueue.Add(new SimEvent
                    {
                        time = time + dataFin.cooldown,
                        priority = -1,
                        type = SimEvent.SimEventType.CooldownExpired,
                        character = ch,
                        slotIndex = ev.slotIndex
                    });

                    ch.state = CharacterSimState.State.Idle;
                    ch.remainingCastAfterStun = 0f;
                    ch.activeHitEvent = null;
                    ch.activeCastFinishedEvent = null;
                    TryStartCasting(ch, time, playerIdx);
                    break;

                case SimEvent.SimEventType.StunApplied:
                    AddTimelineEvent(time, TimelineEventType.StunApplied, opponentIdx,
                        target: playerIdx, abilityName: ev.stunAbilityName,
                        actorHP: target.health);
                    if (ch.state == CharacterSimState.State.Casting)
                    {
                        float elapsed = time - ch.castStartTime;
                        float castTime = ch.slots[ch.currentSlotIndex].abilityData.castTime;
                        ch.remainingCastAfterStun = castTime - elapsed;
                        ch.interruptedCastSessionId = ch.currentCastSessionId;
                        AddTimelineEvent(time, TimelineEventType.CastInterrupted, playerIdx,
                            abilityName: ch.slots[ch.currentSlotIndex].abilityData.abilityName,
                            slot: ch.currentSlotIndex, remainCast: ch.remainingCastAfterStun, actorHP: ch.health, sessionId: ch.currentCastSessionId);

                        if (ch.activeHitEvent != null) { ch.activeHitEvent.cancelled = true; ch.activeHitEvent = null; }
                        if (ch.activeCastFinishedEvent != null) { ch.activeCastFinishedEvent.cancelled = true; ch.activeCastFinishedEvent = null; }
                    }
                    ch.state = CharacterSimState.State.Stunned;
                    ch.stunEndTime = time + ev.stunDuration;
                    eventQueue.Add(new SimEvent
                    {
                        time = ch.stunEndTime,
                        priority = 3,
                        type = SimEvent.SimEventType.StunEnded,
                        character = ch
                    });
                    break;

                case SimEvent.SimEventType.StunEnded:
                    if (ch.state != CharacterSimState.State.Stunned) return;
                    AddTimelineEvent(time, TimelineEventType.StunEnded, playerIdx, actorHP: ch.health);
                    ch.state = CharacterSimState.State.Idle;
                    if (ch.remainingCastAfterStun > 0f)
                    {
                        ch.state = CharacterSimState.State.Casting;
                        float castTime = ch.slots[ch.currentSlotIndex].abilityData.castTime;
                        float remaining = ch.remainingCastAfterStun;
                        float elapsed = castTime - remaining;
                        ch.castStartTime = time - elapsed;
                        ch.castFinishTime = time + remaining;
                        ch.hitTime = elapsed >= castTime * 0.5f ? float.MaxValue : ch.castStartTime + castTime * 0.5f;
                        ch.currentCastSessionId = ch.interruptedCastSessionId;

                        AddTimelineEvent(time, TimelineEventType.ResumeCasting, playerIdx,
                            abilityName: ch.slots[ch.currentSlotIndex].abilityData.abilityName,
                            slot: ch.currentSlotIndex, remainCast: remaining, actorHP: ch.health, sessionId: ch.currentCastSessionId);

                        if (elapsed < castTime * 0.5f)
                        {
                            SimEvent newHit = new SimEvent { time = ch.hitTime, priority = 0, type = SimEvent.SimEventType.Hit, character = ch, slotIndex = ch.currentSlotIndex, slot = ch.slots[ch.currentSlotIndex] };
                            ch.activeHitEvent = newHit;
                            eventQueue.Add(newHit);
                        }
                        SimEvent newCast = new SimEvent { time = ch.castFinishTime, priority = 1, type = SimEvent.SimEventType.CastFinished, character = ch, slotIndex = ch.currentSlotIndex, slot = ch.slots[ch.currentSlotIndex] };
                        ch.activeCastFinishedEvent = newCast;
                        eventQueue.Add(newCast);
                        ch.remainingCastAfterStun = 0f;
                    }
                    else TryStartCasting(ch, time, playerIdx);
                    break;

                case SimEvent.SimEventType.CooldownExpired:
                    ch.slots[ev.slotIndex].cooldownRemaining = 0f;
                    AddTimelineEvent(time, TimelineEventType.CooldownExpired, playerIdx, slot: ev.slotIndex, actorHP: ch.health);
                    if (ch.state == CharacterSimState.State.Idle) TryStartCasting(ch, time, playerIdx);
                    break;
            }
        }

        TryStartCasting(p1, 0f, 1);
        TryStartCasting(p2, 0f, 2);

        while (eventQueue.Count > 0 && p1.health > 0 && p2.health > 0)
        {
            eventQueue.Sort();
            float nextTime = eventQueue[0].time;
            while (eventQueue.Count > 0 && Mathf.Approximately(eventQueue[0].time, nextTime))
            {
                SimEvent ev = eventQueue[0];
                eventQueue.RemoveAt(0);
                ProcessEvent(ev, nextTime);
            }
        }
        return timeline;
    }

    public static AbilityDataSO[] AddAutoAttack(AbilityDataSO[] original, AbilityDataSO autoAttackData)
    {
        if (autoAttackData == null) { Debug.LogError("AutoAttack Data not assigned!"); return original; }
        AbilityDataSO[] extended = new AbilityDataSO[original.Length + 1];
        System.Array.Copy(original, extended, original.Length);
        extended[extended.Length - 1] = autoAttackData;
        return extended;
    }

    public static AbilityDataSO GetAbilityByName(string abilityName, AbilityDataSO[] playerAbilities, AbilityDataSO autoAttackData)
    {
        AbilityDataSO[] fullList = AddAutoAttack(playerAbilities, autoAttackData);
        foreach (var a in fullList) if (a.abilityName == abilityName) return a;
        return null;
    }
}