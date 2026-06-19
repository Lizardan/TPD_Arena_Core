export const MIN_HP = 1;
export const MAX_HP = 999;

export interface BattlePayload {
  leftHp: number;
  rightHp: number;
  leftAbilities?: string[];
  rightAbilities?: string[];
  leftName?: string;
  rightName?: string;
}

const MAX_NAME_LEN = 64;

function parseOptionalName(
  record: Record<string, unknown>,
  key: "leftName" | "rightName",
): string | undefined {
  const raw = record[key];
  if (raw === undefined) return undefined;
  if (typeof raw !== "string") {
    throw new Error(`${key} должно быть строкой.`);
  }
  const name = raw.trim();
  if (!name) {
    throw new Error(`${key} не должно быть пустым.`);
  }
  if (name.length > MAX_NAME_LEN) {
    throw new Error(`${key} слишком длинное (максимум ${MAX_NAME_LEN} символа).`);
  }
  return name;
}

export function validateBattlePayload(payload: unknown): BattlePayload {
  if (!payload || typeof payload !== "object") {
    throw new Error("JSON боя должен быть объектом.");
  }

  const record = payload as Record<string, unknown>;
  const leftHp = record.leftHp;
  const rightHp = record.rightHp;

  if (!Number.isInteger(leftHp) || !Number.isInteger(rightHp)) {
    throw new Error("leftHp и rightHp должны быть целыми числами.");
  }

  if (
    (leftHp as number) < MIN_HP ||
    (leftHp as number) > MAX_HP ||
    (rightHp as number) < MIN_HP ||
    (rightHp as number) > MAX_HP
  ) {
    throw new Error(`HP должны быть от ${MIN_HP} до ${MAX_HP}.`);
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
      throw new Error(`${side} должен быть массивом строк.`);
    }
    battle[side] = abilities;
  }

  const leftName = parseOptionalName(record, "leftName");
  const rightName = parseOptionalName(record, "rightName");
  if (leftName) battle.leftName = leftName;
  if (rightName) battle.rightName = rightName;

  return battle;
}

export function validateBattleJson(raw: string): BattlePayload {
  let payload: unknown;
  try {
    payload = JSON.parse(raw);
  } catch {
    throw new Error("Некорректный JSON.");
  }
  return validateBattlePayload(payload);
}

export function extractJsonFromMessage(text: string): BattlePayload {
  let trimmed = text.trim();
  if (!trimmed) {
    throw new Error('Отправьте JSON после /battle, например:\n/battle {"leftHp":80,"rightHp":100}');
  }

  if (trimmed.startsWith("/battle")) {
    trimmed = trimmed.slice("/battle".length).trim();
  }

  const match = trimmed.match(/\{[\s\S]*\}/);
  if (!match) {
    throw new Error("Не удалось найти JSON-объект в сообщении.");
  }

  return validateBattleJson(match[0]);
}
