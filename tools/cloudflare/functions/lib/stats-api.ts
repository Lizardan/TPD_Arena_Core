import type { BattlePayload } from "./validation";

const DEFAULT_HP = 100;

export async function fetchPlayerStats(
  leftName: string,
  rightName: string,
  statsApiUrl?: string,
): Promise<BattlePayload> {
  if (!statsApiUrl) {
    return { leftHp: DEFAULT_HP, rightHp: DEFAULT_HP };
  }

  const url = new URL(statsApiUrl);
  url.searchParams.set("left", leftName);
  url.searchParams.set("right", rightName);

  try {
    const response = await fetch(url.toString());
    if (!response.ok) {
      console.warn("Stats API error:", response.status);
      return { leftHp: DEFAULT_HP, rightHp: DEFAULT_HP };
    }

    const data = (await response.json()) as { leftHp?: number; rightHp?: number };
    const leftHp = Number.isInteger(data.leftHp) ? data.leftHp! : DEFAULT_HP;
    const rightHp = Number.isInteger(data.rightHp) ? data.rightHp! : DEFAULT_HP;
    return { leftHp, rightHp };
  } catch (error) {
    console.warn("Stats API fetch failed:", error);
    return { leftHp: DEFAULT_HP, rightHp: DEFAULT_HP };
  }
}
