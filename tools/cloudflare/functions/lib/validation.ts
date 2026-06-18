export const MIN_HP = 1;
export const MAX_HP = 999;

export interface BattlePayload {
  leftHp: number;
  rightHp: number;
  leftAbilities?: string[];
  rightAbilities?: string[];
}

export function validateBattlePayload(payload: unknown): BattlePayload {
  if (!payload || typeof payload !== "object") {
    throw new Error("Battle JSON must be an object.");
  }

  const record = payload as Record<string, unknown>;
  const leftHp = record.leftHp;
  const rightHp = record.rightHp;

  if (!Number.isInteger(leftHp) || !Number.isInteger(rightHp)) {
    throw new Error("leftHp and rightHp must be integers.");
  }

  if (
    (leftHp as number) < MIN_HP ||
    (leftHp as number) > MAX_HP ||
    (rightHp as number) < MIN_HP ||
    (rightHp as number) > MAX_HP
  ) {
    throw new Error(`HP must be between ${MIN_HP} and ${MAX_HP}.`);
  }

  const battle: BattlePayload = {
    leftHp: leftHp as number,
    rightHp: rightHp as number,
  };

  for (const side of ["leftAbilities", "rightAbilities"] as const) {
    const abilities = record[side];
    if (abilities === undefined) continue;
    if (
      !Array.isArray(abilities) ||
      !abilities.every((item) => typeof item === "string")
    ) {
      throw new Error(`${side} must be an array of strings.`);
    }
    battle[side] = abilities;
  }

  return battle;
}

export function validateBattleJson(raw: string): BattlePayload {
  let payload: unknown;
  try {
    payload = JSON.parse(raw);
  } catch {
    throw new Error("Invalid JSON.");
  }
  return validateBattlePayload(payload);
}

export function extractJsonFromMessage(text: string): BattlePayload {
  let trimmed = text.trim();
  if (!trimmed) {
    throw new Error('Send JSON after /battle, e.g. /battle {"leftHp":80,"rightHp":100}');
  }

  if (trimmed.startsWith("/battle")) {
    trimmed = trimmed.slice("/battle".length).trim();
  }

  const match = trimmed.match(/\{[\s\S]*\}/);
  if (!match) {
    throw new Error("Could not find JSON object in message.");
  }

  return validateBattleJson(match[0]);
}
