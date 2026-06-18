using System.Collections.Generic;
using UnityEngine;

namespace TPD.Arena
{
    public static class TimelineStateBuilder
    {
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

        public struct PlayerReplayState
        {
            public enum State { Idle, Casting, Stunned, Won }
            public int hp, shield;
            public State state;
            public string abilityName;
            public float castProgress, stunProgress;
            public float stunElapsed;
            public int castSessionId;
        }

        public struct TimelineInterval
        {
            public float startTime;
            public float endTime;
            public bool isStun;
            public AbilityDataSO ability;
        }

        public static List<TimelineEvent> FilterAndSortForPlayer(List<TimelineEvent> allEvents, int playerIndex)
        {
            var relevant = new List<TimelineEvent>();
            foreach (var ev in allEvents)
            {
                if (ev.actorPlayer == playerIndex || ev.targetPlayer == playerIndex)
                    relevant.Add(ev);
            }
            relevant.Sort(CompareEvents);
            return relevant;
        }

        public static int CompareEvents(TimelineEvent a, TimelineEvent b)
        {
            int cmp = a.timestamp.CompareTo(b.timestamp);
            if (cmp != 0) return cmp;
            int pa = EventPriority.TryGetValue(a.eventType, out int va) ? va : 99;
            int pb = EventPriority.TryGetValue(b.eventType, out int vb) ? vb : 99;
            return pa.CompareTo(pb);
        }

        public static PlayerReplayState GetPlayerStateAtTime(
            List<TimelineEvent> playerEvents,
            int playerIdx,
            float time,
            AbilityDataSO[] abilities,
            AbilityDataSO autoAttack,
            int maxHealth)
        {
            var res = new PlayerReplayState
            {
                hp = maxHealth,
                shield = 0,
                state = PlayerReplayState.State.Idle,
                stunProgress = -1,
                castSessionId = 0
            };

            bool casting = false;
            float castStart = 0, castDuration = 0;
            string castAbility = "";
            float stunEnd = -1, stunProgress = -1, stunStart = -1;
            int sessionId = 0, interruptedId = -1;
            int shield = 0, hp = maxHealth;

            foreach (var ev in playerEvents)
            {
                if (ev.timestamp > time) break;

                if (ev.targetPlayer == playerIdx)
                {
                    switch (ev.eventType)
                    {
                        case TimelineEventType.Hit when ev.damage > 0:
                            hp = ev.targetHPLeft;
                            shield = ev.targetShieldLeft;
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
                            casting = true;
                            castStart = ev.timestamp;
                            castAbility = ev.abilityName;
                            var ab = BattleSimulator.GetAbilityByName(ev.abilityName, abilities, autoAttack);
                            castDuration = ab != null ? ab.castTime : 1f;
                            sessionId = ev.castSessionId;
                            interruptedId = -1;
                            break;
                        case TimelineEventType.CastInterrupted:
                            if (casting)
                            {
                                stunProgress = castDuration > 0 ? Mathf.Clamp01((ev.timestamp - castStart) / castDuration) : 0f;
                                interruptedId = ev.castSessionId;
                                casting = false;
                            }
                            break;
                        case TimelineEventType.CastFinished:
                            casting = false;
                            stunProgress = -1;
                            break;
                        case TimelineEventType.ResumeCasting:
                            casting = true;
                            castStart = ev.timestamp - (castDuration - ev.remainingCastTime);
                            castAbility = ev.abilityName;
                            stunProgress = -1;
                            sessionId = ev.castSessionId;
                            break;
                        case TimelineEventType.StunEnded:
                            stunEnd = -1;
                            break;
                    }
                }

                if (ev.targetPlayer == playerIdx && ev.eventType == TimelineEventType.StunApplied)
                {
                    var stunAb = BattleSimulator.GetAbilityByName(ev.abilityName, abilities, autoAttack);
                    float duration = stunAb != null ? stunAb.stunDuration : 0f;
                    stunStart = ev.timestamp;
                    stunEnd = ev.timestamp + duration;
                    if (casting)
                    {
                        stunProgress = castDuration > 0 ? Mathf.Clamp01((ev.timestamp - castStart) / castDuration) : 0f;
                        interruptedId = sessionId;
                        casting = false;
                    }
                }
            }

            res.hp = hp;
            res.shield = shield;

            if (stunEnd > time)
            {
                res.state = PlayerReplayState.State.Stunned;
                res.stunProgress = stunProgress;
                res.stunElapsed = stunStart >= 0f ? time - stunStart : 0f;
                if (stunProgress >= 0)
                {
                    res.abilityName = castAbility;
                    res.castSessionId = interruptedId;
                }
            }
            else if (casting)
            {
                res.state = PlayerReplayState.State.Casting;
                res.castProgress = castDuration > 0 ? Mathf.Clamp01((time - castStart) / castDuration) : 0f;
                res.abilityName = castAbility;
                res.castSessionId = sessionId;
            }

            return res;
        }

        public static List<TimelineInterval> BuildIntervals(
            List<TimelineEvent> playerEvents,
            int playerIndex,
            float battleDuration,
            AbilityDataSO[] abilities,
            AbilityDataSO autoAttack)
        {
            var intervals = new List<TimelineInterval>();
            const int Idle = 0, Casting = 1, Stunned = 2;
            int state = Idle;
            AbilityDataSO currentAbility = null;
            float stateStart = 0f;

            foreach (var ev in playerEvents)
            {
                if (ev.actorPlayer == playerIndex)
                {
                    switch (ev.eventType)
                    {
                        case TimelineEventType.StartCasting:
                        case TimelineEventType.ResumeCasting:
                            if (state == Idle)
                            {
                                state = Casting;
                                currentAbility = BattleSimulator.GetAbilityByName(ev.abilityName, abilities, autoAttack);
                                stateStart = ev.timestamp;
                            }
                            break;
                        case TimelineEventType.CastInterrupted:
                            if (state == Casting)
                            {
                                intervals.Add(new TimelineInterval
                                {
                                    startTime = stateStart,
                                    endTime = ev.timestamp,
                                    isStun = false,
                                    ability = currentAbility
                                });
                                state = Idle;
                            }
                            break;
                        case TimelineEventType.CastFinished:
                            if (state == Casting)
                            {
                                intervals.Add(new TimelineInterval
                                {
                                    startTime = stateStart,
                                    endTime = ev.timestamp,
                                    isStun = false,
                                    ability = currentAbility
                                });
                                state = Idle;
                            }
                            break;
                        case TimelineEventType.StunEnded:
                            if (state == Stunned)
                            {
                                intervals.Add(new TimelineInterval
                                {
                                    startTime = stateStart,
                                    endTime = ev.timestamp,
                                    isStun = true,
                                    ability = null
                                });
                                state = Idle;
                            }
                            break;
                    }
                }

                if (ev.targetPlayer == playerIndex && ev.eventType == TimelineEventType.StunApplied)
                {
                    if (state == Casting)
                    {
                        intervals.Add(new TimelineInterval
                        {
                            startTime = stateStart,
                            endTime = ev.timestamp,
                            isStun = false,
                            ability = currentAbility
                        });
                    }
                    state = Stunned;
                    stateStart = ev.timestamp;
                }
            }

            if (state != Idle)
            {
                intervals.Add(new TimelineInterval
                {
                    startTime = stateStart,
                    endTime = battleDuration,
                    isStun = state == Stunned,
                    ability = currentAbility
                });
            }

            return intervals;
        }

        public static int CountProcessedEvents(List<TimelineEvent> playerEvents, float time)
        {
            int count = 0;
            foreach (var ev in playerEvents)
            {
                if (ev.timestamp > time) break;
                count++;
            }
            return count;
        }
    }
}
