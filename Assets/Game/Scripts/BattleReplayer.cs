using System;
using System.Collections.Generic;
using UnityEngine;
using static TPD.Arena.TimelineStateBuilder;

namespace TPD.Arena
{
    public class BattleReplayer : MonoBehaviour
    {
        private BattleController battleController;

        private List<TimelineEvent> timelineEvents;
        private List<TimelineEvent> player1Events;
        private List<TimelineEvent> player2Events;
        private float battleDuration;
        private int p1MaxHealth = 100;
        private int p2MaxHealth = 100;

        private PlayerReplayState prevPlayerState1;
        private PlayerReplayState prevPlayerState2;

        private string currentAnim1 = "";
        private string currentAnim2 = "";

        private int lastEventIndex1;
        private int lastEventIndex2;
        private float lastProcessedTime = -1f;

        private void Awake() => battleController = GetComponent<BattleController>();

        public void Initialize(List<TimelineEvent> events, int maxHealth)
        {
            Initialize(events, maxHealth, maxHealth);
        }

        public void Initialize(List<TimelineEvent> events, int p1MaxHealth, int p2MaxHealth)
        {
            timelineEvents = events;
            this.p1MaxHealth = p1MaxHealth;
            this.p2MaxHealth = p2MaxHealth;
            battleDuration = events.Count > 0 ? events[events.Count - 1].timestamp : 0f;
            player1Events = FilterAndSortForPlayer(events, 1);
            player2Events = FilterAndSortForPlayer(events, 2);
            ResetPlaybackState();
        }

        public void UpdateAtTime(float time, bool playTransientVfx = true)
        {
            const float epsilon = 0.001f;

            if (lastProcessedTime >= 0f && time < lastProcessedTime - epsilon)
            {
                battleController.player1.vfx.ClearVFX();
                battleController.player2.vfx.ClearVFX();
                lastEventIndex1 = 0;
                lastEventIndex2 = 0;
                lastProcessedTime = -1f;
                currentAnim1 = "";
                currentAnim2 = "";
            }

            PlayerReplayState s1 = GetPlayerStateAtTime(player1Events, 1, time,
                battleController.player1.abilities, battleController.player1.autoAttack, p1MaxHealth);
            PlayerReplayState s2 = GetPlayerStateAtTime(player2Events, 2, time,
                battleController.player2.abilities, battleController.player2.autoAttack, p2MaxHealth);

            if (s1.hp <= 0 && s2.hp > 0) s2.state = PlayerReplayState.State.Won;
            else if (s2.hp <= 0 && s1.hp > 0) s1.state = PlayerReplayState.State.Won;

            PlayerController p1 = battleController.player1;
            PlayerController p2 = battleController.player2;

            p1.UpdateHP(s1.hp, s1.shield);
            p2.UpdateHP(s2.hp, s2.shield);
            ApplyReplayStateToUI(p1, s1);
            ApplyReplayStateToUI(p2, s2);

            if (playTransientVfx)
            {
                p1.vfx.SyncStun(s1.state == PlayerReplayState.State.Stunned);
                p2.vfx.SyncStun(s2.state == PlayerReplayState.State.Stunned);
            }
            else
            {
                Func<int, string, AbilityDataSO> resolveAbility = ResolveAbilityForActor;
                int exportFps = battleController.battleConfig != null
                    ? Mathf.Max(1, battleController.battleConfig.exportFps)
                    : 30;
                // Finer than export fps so burst emitters match Play Mode integration, not fast-forward Simulate(age).
                float exportSimStep = 1f / Mathf.Max(60, exportFps);

                p1.vfx.SyncExportTransientVfx(time, player1Events, 1);
                p2.vfx.SyncExportTransientVfx(time, player2Events, 2);
                p1.vfx.SyncStun(
                    s1.state == PlayerReplayState.State.Stunned,
                    GetStunScrubTime(playTransientVfx, s1),
                    exportSimStep);
                p2.vfx.SyncStun(
                    s2.state == PlayerReplayState.State.Stunned,
                    GetStunScrubTime(playTransientVfx, s2),
                    exportSimStep);
                p1.vfx.SyncExportAbilityVfx(time, player1Events, 1, resolveAbility, exportSimStep);
                p2.vfx.SyncExportAbilityVfx(time, player2Events, 2, resolveAbility, exportSimStep);
            }

            UpdateAnimator(p1.animator, s1, ref prevPlayerState1, 1, !playTransientVfx);
            UpdateAnimator(p2.animator, s2, ref prevPlayerState2, 2, !playTransientVfx);

            if (!playTransientVfx)
                EvaluateAnimators(p1.animator, p2.animator);

            if (playTransientVfx && time > lastProcessedTime + epsilon)
            {
                ProcessEvents(1, time, player1Events, ref lastEventIndex1, p1);
                ProcessEvents(2, time, player2Events, ref lastEventIndex2, p2);
            }
            else if (lastProcessedTime < 0f)
            {
                lastEventIndex1 = CountProcessedEvents(player1Events, time);
                lastEventIndex2 = CountProcessedEvents(player2Events, time);
            }

            lastProcessedTime = time;
            prevPlayerState1 = s1;
            prevPlayerState2 = s2;
        }

        private static float GetStunScrubTime(bool playTransientVfx, PlayerReplayState state)
        {
            if (playTransientVfx || state.state != PlayerReplayState.State.Stunned)
                return -1f;
            return state.stunElapsed;
        }

        private AbilityDataSO ResolveAbilityForActor(int actorPlayer, string abilityName)
        {
            PlayerController pc = actorPlayer == 1 ? battleController.player1 : battleController.player2;
            return BattleSimulator.GetAbilityByName(abilityName, pc.abilities, pc.autoAttack);
        }

        private void ResetPlaybackState()
        {
            lastEventIndex1 = 0;
            lastEventIndex2 = 0;
            lastProcessedTime = -1f;
            currentAnim1 = "";
            currentAnim2 = "";
            prevPlayerState1 = default;
            prevPlayerState2 = default;
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

        private void ProcessEvents(int playerIdx, float currentTime, List<TimelineEvent> playerEvents, ref int lastIdx, PlayerController pc)
        {
            for (int i = lastIdx; i < playerEvents.Count; i++)
            {
                TimelineEvent ev = playerEvents[i];
                if (ev.timestamp > currentTime) break;
                if (ev.timestamp <= lastProcessedTime) continue;

                if (ev.actorPlayer == playerIdx)
                {
                    switch (ev.eventType)
                    {
                        case TimelineEventType.StartCasting:
                        case TimelineEventType.ResumeCasting:
                        {
                            var castAbility = BattleSimulator.GetAbilityByName(ev.abilityName, pc.abilities, pc.autoAttack);
                            if (castAbility?.castVfxPrefab != null && !IsSupportAbility(castAbility))
                            {
                                float maxLifetime = ev.eventType == TimelineEventType.ResumeCasting && ev.remainingCastTime > 0f
                                    ? ev.remainingCastTime
                                    : castAbility.castTime;
                                pc.vfx.PlayCastVFX(
                                    castAbility.castVfxPrefab,
                                    castAbility.hitVfxOffset,
                                    castAbility.vfxLocalScale,
                                    maxLifetime);
                            }
                            break;
                        }
                        case TimelineEventType.CastFinished:
                        case TimelineEventType.CastInterrupted:
                            pc.vfx.StopCastVFX();
                            break;
                    }
                }

                if (ev.targetPlayer == playerIdx)
                {
                    switch (ev.eventType)
                    {
                        case TimelineEventType.Hit when ev.damage > 0:
                            pc.vfx.SpawnDamageText(ev.damage);
                            var hitAbility = BattleSimulator.GetAbilityByName(ev.abilityName,
                                ev.actorPlayer == 1 ? battleController.player1.abilities : battleController.player2.abilities,
                                ev.actorPlayer == 1 ? battleController.player1.autoAttack : battleController.player2.autoAttack);
                            if (hitAbility?.hitVfxPrefab != null)
                            {
                                pc.vfx.PlayAbilityVFX(
                                    hitAbility.hitVfxPrefab,
                                    hitAbility.hitVfxOffset,
                                    hitAbility.vfxLocalScale,
                                    -1f,
                                    hitAbility.displayColor,
                                    true);
                            }
                            break;
                        case TimelineEventType.HealApplied:
                            pc.vfx.SpawnHealText(ev.damage);
                            var healAbility = BattleSimulator.GetAbilityByName(ev.abilityName, pc.abilities, pc.autoAttack);
                            if (healAbility?.hitVfxPrefab != null)
                                pc.vfx.PlayAbilityVFX(
                                    healAbility.hitVfxPrefab,
                                    pc.vfx.GetSupportEffectOffset(healAbility.hitVfxOffset),
                                    healAbility.vfxLocalScale,
                                    -1f,
                                    healAbility.displayColor,
                                    true);
                            break;
                        case TimelineEventType.ShieldApplied:
                            pc.vfx.SpawnShieldText(ev.damage);
                            var shieldAbility = BattleSimulator.GetAbilityByName(ev.abilityName, pc.abilities, pc.autoAttack);
                            if (shieldAbility?.hitVfxPrefab != null)
                                pc.vfx.PlayAbilityVFX(
                                    shieldAbility.hitVfxPrefab,
                                    pc.vfx.GetSupportEffectOffset(shieldAbility.hitVfxOffset),
                                    shieldAbility.vfxLocalScale,
                                    -1f,
                                    shieldAbility.displayColor,
                                    true);
                            break;
                    }
                }

                lastIdx = i + 1;
            }
        }

        private void UpdateAnimator(Animator anim, PlayerReplayState state, ref PlayerReplayState prevState, int playerIdx, bool scrubTimeline)
        {
            if (!anim) return;

            string targetAnim = null;
            float normalizedTime = 0f;
            AbilityDataSO castData = null;

            if (state.hp <= 0)
            {
                targetAnim = "Death";
                normalizedTime = 1f;
            }
            else if (state.state == PlayerReplayState.State.Won)
            {
                targetAnim = "Idle";
                normalizedTime = 0f;
            }
            else if (state.state == PlayerReplayState.State.Stunned)
            {
                PlayerController pc = playerIdx == 1 ? battleController.player1 : battleController.player2;
                castData = BattleSimulator.GetAbilityByName(state.abilityName, pc.abilities, pc.autoAttack);
                if (state.stunProgress >= 0f && castData?.animationClip != null)
                {
                    targetAnim = castData.animationClip.name;
                    normalizedTime = state.stunProgress;
                }
                else
                {
                    targetAnim = "Stun";
                    normalizedTime = state.stunProgress >= 0f ? state.stunProgress : 0f;
                }
            }
            else if (state.state == PlayerReplayState.State.Casting)
            {
                PlayerController pc = playerIdx == 1 ? battleController.player1 : battleController.player2;
                castData = BattleSimulator.GetAbilityByName(state.abilityName, pc.abilities, pc.autoAttack);
                targetAnim = castData != null && castData.animationClip ? castData.animationClip.name : "Idle";
                normalizedTime = state.castProgress;
            }
            else
            {
                targetAnim = "Idle";
                normalizedTime = 0f;
            }

            if (scrubTimeline)
            {
                if (targetAnim != null)
                {
                    int stateHash = Animator.StringToHash(targetAnim);
                    anim.Play(stateHash, 0, normalizedTime);
                }

                anim.speed = 0f;
                anim.Update(0f);

                if (playerIdx == 1)
                    currentAnim1 = targetAnim;
                else
                    currentAnim2 = targetAnim;

                return;
            }

            bool freezeAnim = state.state == PlayerReplayState.State.Stunned
                && state.stunProgress >= 0f
                && castData?.animationClip != null;

            string current = playerIdx == 1 ? currentAnim1 : currentAnim2;

            if (freezeAnim)
            {
                anim.Play(castData.animationClip.name, 0, state.stunProgress);
                anim.speed = 0f;
                return;
            }

            if (state.state == PlayerReplayState.State.Casting && targetAnim != null)
            {
                bool resumedFromFrozenStun =
                    prevState.state == PlayerReplayState.State.Stunned
                    && prevState.castSessionId == state.castSessionId
                    && prevState.abilityName == state.abilityName
                    && prevState.stunProgress >= 0f;

                bool newCast = prevState.state != PlayerReplayState.State.Casting
                    || prevState.castSessionId != state.castSessionId
                    || prevState.abilityName != state.abilityName;
                if (resumedFromFrozenStun)
                    anim.Play(targetAnim, 0, Mathf.Clamp01(prevState.stunProgress));
                else if (newCast)
                    anim.CrossFade(targetAnim, 0.1f, 0, 0f);

                float clipLen = castData?.animationClip ? castData.animationClip.length : 1f;
                float castTime = castData != null ? castData.castTime : 1f;
                anim.speed = castTime > 0f ? clipLen / castTime : 1f;
                if (playerIdx == 1) currentAnim1 = targetAnim; else currentAnim2 = targetAnim;
                return;
            }

            if (targetAnim != null && current != targetAnim)
            {
                anim.CrossFade(targetAnim, 0.1f, 0, 0f);
                if (playerIdx == 1) currentAnim1 = targetAnim; else currentAnim2 = targetAnim;
            }

            anim.speed = 1f;
        }

        private static void EvaluateAnimators(Animator anim1, Animator anim2)
        {
            if (anim1 != null) anim1.Update(0f);
            if (anim2 != null) anim2.Update(0f);
        }

        private static bool IsSupportAbility(AbilityDataSO ability)
        {
            if (ability == null)
                return false;
            return ability.type == AbilityType.Heal || ability.type == AbilityType.Shield;
        }

        private AnimatorCullingMode savedCullMode1;
        private AnimatorCullingMode savedCullMode2;

        private struct SkinnedMeshSnapshot
        {
            public SkinnedMeshRenderer renderer;
            public bool updateWhenOffscreen;
            public bool forceMatrixRecalculationPerRender;
        }

        private readonly List<SkinnedMeshSnapshot> skinnedMeshSnapshots = new List<SkinnedMeshSnapshot>();

        public void BeginExportAnimators()
        {
            battleController.player1.vfx.ClearVFX();
            battleController.player2.vfx.ClearVFX();
            savedCullMode1 = SetAlwaysAnimate(battleController.player1.animator);
            savedCullMode2 = SetAlwaysAnimate(battleController.player2.animator);
            skinnedMeshSnapshots.Clear();
            RegisterSkinnedMeshes(battleController.player1.animator);
            RegisterSkinnedMeshes(battleController.player2.animator);
        }

        public void EndExportAnimators()
        {
            battleController.player1.vfx.ClearVFX();
            battleController.player2.vfx.ClearVFX();

            RestoreCulling(battleController.player1.animator, savedCullMode1);
            RestoreCulling(battleController.player2.animator, savedCullMode2);
            foreach (SkinnedMeshSnapshot snapshot in skinnedMeshSnapshots)
            {
                if (snapshot.renderer == null)
                    continue;

                snapshot.renderer.updateWhenOffscreen = snapshot.updateWhenOffscreen;
                snapshot.renderer.forceMatrixRecalculationPerRender = snapshot.forceMatrixRecalculationPerRender;
            }

            skinnedMeshSnapshots.Clear();
        }

        public void PrepareSkinnedMeshesForRender()
        {
            foreach (SkinnedMeshSnapshot snapshot in skinnedMeshSnapshots)
            {
                if (snapshot.renderer != null)
                    snapshot.renderer.forceMatrixRecalculationPerRender = true;
            }
        }

        private void RegisterSkinnedMeshes(Animator anim)
        {
            if (anim == null)
                return;

            SkinnedMeshRenderer[] renderers = anim.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (SkinnedMeshRenderer renderer in renderers)
            {
                skinnedMeshSnapshots.Add(new SkinnedMeshSnapshot
                {
                    renderer = renderer,
                    updateWhenOffscreen = renderer.updateWhenOffscreen,
                    forceMatrixRecalculationPerRender = renderer.forceMatrixRecalculationPerRender
                });
                renderer.updateWhenOffscreen = true;
                renderer.forceMatrixRecalculationPerRender = true;
            }
        }

        private static AnimatorCullingMode SetAlwaysAnimate(Animator anim)
        {
            if (anim == null)
                return AnimatorCullingMode.CullUpdateTransforms;

            AnimatorCullingMode previous = anim.cullingMode;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            return previous;
        }

        private static void RestoreCulling(Animator anim, AnimatorCullingMode mode)
        {
            if (anim != null)
                anim.cullingMode = mode;
        }

        public string GetAnimatorStateName(int playerIndex)
        {
            Animator anim = playerIndex == 1 ? battleController.player1.animator : battleController.player2.animator;
            if (anim == null)
                return "none";

            AnimatorClipInfo[] clips = anim.GetCurrentAnimatorClipInfo(0);
            return clips.Length > 0 ? clips[0].clip.name : "unknown";
        }

        public float GetAnimatorNormalizedTime(int playerIndex)
        {
            Animator anim = playerIndex == 1 ? battleController.player1.animator : battleController.player2.animator;
            return anim != null ? anim.GetCurrentAnimatorStateInfo(0).normalizedTime : -1f;
        }

        public void ClearVFX()
        {
            battleController.player1.vfx.ClearVFX();
            battleController.player2.vfx.ClearVFX();
            ResetPlaybackState();
        }
    }
}
