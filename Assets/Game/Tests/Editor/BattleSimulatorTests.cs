using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace TPD.Arena.Tests
{
    public class BattleSimulatorTests
    {
        private const int MaxHealth = 100;

        [Test]
        public void Shield_AbsorbsDamageBeforeHealth()
        {
            var shield = CreateAbility("Shield", AbilityType.Shield, castTime: 0.1f, cooldown: 10f, damage: 20);
            var damage = CreateAbility("Smash", AbilityType.Damage, castTime: 2f, cooldown: 10f, damage: 30);
            var p1 = new[] { shield };
            var p2 = new[] { damage };
            var auto = CreateAbility("Auto", AbilityType.Damage, castTime: 20f, cooldown: 20f, damage: 1);

            var timeline = BattleSimulator.Simulate(p1, p2, auto, auto, MaxHealth);

            var hitEvent = timeline.FirstOrDefault(e => e.eventType == TimelineEventType.Hit && e.damage == 30);
            Assert.IsNotNull(hitEvent);
            Assert.AreEqual(10, hitEvent.targetHPLeft);
            Assert.AreEqual(0, hitEvent.targetShieldLeft);
        }

        [Test]
        public void Stun_InterruptsCast_AndResumesAfterStunEnds()
        {
            var longCast = CreateAbility("LongCast", AbilityType.Damage, castTime: 4f, cooldown: 10f, damage: 5);
            var stun = CreateAbility("Stun", AbilityType.Stun, castTime: 0.1f, cooldown: 1f, damage: 0, stunDuration: 1f);
            var filler = CreateAbility("Filler", AbilityType.Damage, castTime: 10f, cooldown: 10f, damage: 1);
            var p1 = new[] { longCast };
            var p2 = new[] { stun, filler };
            var auto = CreateAbility("Auto", AbilityType.Damage, castTime: 20f, cooldown: 20f, damage: 1);

            var timeline = BattleSimulator.Simulate(p1, p2, auto, auto, MaxHealth);

            Assert.IsTrue(timeline.Any(e => e.eventType == TimelineEventType.CastInterrupted));
            Assert.IsTrue(timeline.Any(e => e.eventType == TimelineEventType.ResumeCasting));
            Assert.IsTrue(timeline.Any(e => e.eventType == TimelineEventType.StunEnded));
        }

        [Test]
        public void Stun_OnAlreadyStunnedTarget_ProducesStunAttemptFailed()
        {
            var stun = CreateAbility("Stun", AbilityType.Stun, castTime: 0.1f, cooldown: 0.5f, damage: 0, stunDuration: 2f);
            var filler = CreateAbility("Filler", AbilityType.Damage, castTime: 10f, cooldown: 10f, damage: 1);
            var p1 = new[] { stun, stun };
            var p2 = new[] { filler };
            var auto = CreateAbility("Auto", AbilityType.Damage, castTime: 20f, cooldown: 20f, damage: 1);

            var timeline = BattleSimulator.Simulate(p1, p2, auto, auto, MaxHealth);

            Assert.IsTrue(timeline.Any(e => e.eventType == TimelineEventType.StunAttemptFailed));
        }

        [Test]
        public void Battle_EndsWhenHealthReachesZero()
        {
            var burst = CreateAbility("Burst", AbilityType.Damage, castTime: 0.1f, cooldown: 0.1f, damage: 50);
            var p1 = new[] { burst };
            var p2 = new[] { burst };
            var auto = CreateAbility("Auto", AbilityType.Damage, castTime: 20f, cooldown: 20f, damage: 1);

            var timeline = BattleSimulator.Simulate(p1, p2, auto, auto, MaxHealth);

            Assert.IsTrue(timeline.Any(e => e.eventType == TimelineEventType.BattleEnd));
            var battleEnd = timeline.Last(e => e.eventType == TimelineEventType.BattleEnd);
            Assert.LessOrEqual(battleEnd.targetHPLeft, 0);
        }

        [Test]
        public void AsymmetricMaxHealth_IsAppliedPerPlayer()
        {
            var burst = CreateAbility("Burst", AbilityType.Damage, castTime: 0.1f, cooldown: 0.1f, damage: 50);
            var p1 = new[] { burst };
            var p2 = new[] { burst };
            var auto = CreateAbility("Auto", AbilityType.Damage, castTime: 20f, cooldown: 20f, damage: 1);

            var timeline = BattleSimulator.Simulate(p1, p2, auto, auto, 80, 100);

            var p1Cast = timeline.Find(e => e.eventType == TimelineEventType.StartCasting && e.actorPlayer == 1);
            Assert.IsNotNull(p1Cast);
            Assert.AreEqual(80, p1Cast.actorHPLeft);

            var p2Cast = timeline.Find(e => e.eventType == TimelineEventType.StartCasting && e.actorPlayer == 2);
            Assert.IsNotNull(p2Cast);
            Assert.AreEqual(100, p2Cast.actorHPLeft);
        }

        [Test]
        public void SeparateAutoAttacks_AreUsedPerPlayer()
        {
            var fastAuto = CreateAbility("FastAuto", AbilityType.Damage, castTime: 0.5f, cooldown: 0.5f, damage: 1);
            var slowAuto = CreateAbility("SlowAuto", AbilityType.Damage, castTime: 5f, cooldown: 5f, damage: 1);
            var p1 = new AbilityDataSO[0];
            var p2 = new AbilityDataSO[0];

            var timeline = BattleSimulator.Simulate(p1, p2, fastAuto, slowAuto, MaxHealth);

            var p1Casts = timeline.Where(e => e.actorPlayer == 1 && e.eventType == TimelineEventType.StartCasting).ToList();
            var p2Casts = timeline.Where(e => e.actorPlayer == 2 && e.eventType == TimelineEventType.StartCasting).ToList();
            Assert.Greater(p1Casts.Count, p2Casts.Count);
        }

        private static AbilityDataSO CreateAbility(
            string name, AbilityType type, float castTime, float cooldown, int damage, float stunDuration = 0f)
        {
            var ability = ScriptableObject.CreateInstance<AbilityDataSO>();
            ability.abilityName = name;
            ability.type = type;
            ability.castTime = castTime;
            ability.cooldown = cooldown;
            ability.damage = damage;
            ability.stunDuration = stunDuration;
            return ability;
        }
    }
}
