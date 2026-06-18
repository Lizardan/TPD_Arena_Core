/**
 * Simplified deterministic battle timeline for the web renderer MVP.
 * Full Unity parity can be added later via WebGL or a WASM sim export.
 */
export function simulateBattle(leftHp, rightHp) {
  const events = [];
  let p1Hp = leftHp;
  let p2Hp = rightHp;
  let p1Shield = 0;
  let p2Shield = 0;
  let time = 0;
  let turn = 1;

  const push = (eventType, actor, target, extra = {}) => {
    events.push({
      timestamp: time,
      eventType,
      actorPlayer: actor,
      targetPlayer: target,
      p1Hp,
      p2Hp,
      p1Shield,
      p2Shield,
      ...extra,
    });
  };

  push("BattleStart", 0, 0, { label: "Battle begins" });

  while (p1Hp > 0 && p2Hp > 0 && time < 120) {
    const actor = turn % 2 === 1 ? 1 : 2;
    const target = actor === 1 ? 2 : 1;
    const ability = turn % 4 === 0 ? "Shield" : turn % 3 === 0 ? "Heal" : "Strike";
    time += 0.8;

    push("StartCasting", actor, target, { abilityName: ability });

    time += 0.6;

    if (ability === "Shield") {
      if (actor === 1) p1Shield = Math.min(40, p1Shield + 18);
      else p2Shield = Math.min(40, p2Shield + 18);
      push("ShieldApplied", actor, actor, { abilityName: ability, amount: 18 });
    } else if (ability === "Heal") {
      const heal = 14;
      if (actor === 1) p1Hp = Math.min(leftHp, p1Hp + heal);
      else p2Hp = Math.min(rightHp, p2Hp + heal);
      push("HealApplied", actor, actor, { abilityName: ability, amount: heal });
    } else {
      const damage = 12 + (turn % 5);
      let remaining = damage;
      if (target === 1 && p1Shield > 0) {
        const absorbed = Math.min(p1Shield, remaining);
        p1Shield -= absorbed;
        remaining -= absorbed;
      }
      if (target === 2 && p2Shield > 0) {
        const absorbed = Math.min(p2Shield, remaining);
        p2Shield -= absorbed;
        remaining -= absorbed;
      }
      if (target === 1) p1Hp = Math.max(0, p1Hp - remaining);
      else p2Hp = Math.max(0, p2Hp - remaining);
      push("Hit", actor, target, { abilityName: ability, damage, remaining });
    }

    push("CastFinished", actor, target, { abilityName: ability });
    turn += 1;
    time += 0.4;
  }

  const winner = p1Hp > 0 && p2Hp <= 0 ? 1 : p2Hp > 0 && p1Hp <= 0 ? 2 : 0;
  push("BattleEnd", winner, winner, { label: winner ? `Player ${winner} wins` : "Draw" });

  return {
    leftHp,
    rightHp,
    duration: Math.max(time, 1),
    events,
  };
}
