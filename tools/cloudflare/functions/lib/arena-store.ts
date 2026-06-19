import type { BattlePayload } from "./validation";

export type ArenaStatus = "waiting" | "fighting" | "done" | "expired";

export type ArenaRole = "host" | "player1" | "player2" | "spectator";

export interface ArenaPlayer {
  id: number;
  displayName: string;
}

export interface ArenaRecord {
  id: string;
  chatId: number;
  messageId: number;
  status: ArenaStatus;
  hostUserId: number | null;
  openerName: string;
  player1: ArenaPlayer | null;
  player2: ArenaPlayer | null;
  spectatorIds: number[];
  battle: BattlePayload;
  battleStartAt: number | null;
  createdAt: number;
  exp: number;
}

const ARENA_TTL_SEC = 30 * 60;
const KV_PREFIX = "arena:";
const CHAT_ACTIVE_PREFIX = "chat-active:";
const JOIN_CLAIM_PREFIX = "arena-join:";
const BATTLE_HP_MIN = 30;
const BATTLE_HP_MAX = 100;

interface ArenaJoinClaim extends ArenaPlayer {
  joinedAt: number;
}

export function arenaKey(id: string): string {
  return `${KV_PREFIX}${id}`;
}

export function chatActiveKey(chatId: number): string {
  return `${CHAT_ACTIVE_PREFIX}${chatId}`;
}

function joinClaimPrefix(arenaId: string): string {
  return `${JOIN_CLAIM_PREFIX}${arenaId}:`;
}

function joinClaimKey(arenaId: string, userId: number): string {
  return `${joinClaimPrefix(arenaId)}${userId}`;
}

export async function getActiveArenaForChat(
  kv: KVNamespace,
  chatId: number,
): Promise<ArenaRecord | null> {
  const arenaId = await kv.get(chatActiveKey(chatId));
  if (!arenaId) return null;

  const arena = await getArena(kv, arenaId);
  if (!arena || arena.status === "done" || arena.status === "expired") {
    await kv.delete(chatActiveKey(chatId));
    return null;
  }
  return arena;
}

export async function setActiveArenaForChat(
  kv: KVNamespace,
  chatId: number,
  arenaId: string,
): Promise<void> {
  await kv.put(chatActiveKey(chatId), arenaId, { expirationTtl: ARENA_TTL_SEC });
}

export async function clearActiveArenaForChat(kv: KVNamespace, chatId: number): Promise<void> {
  await kv.delete(chatActiveKey(chatId));
}

function newArenaId(): string {
  return crypto.randomUUID().replace(/-/g, "").slice(0, 16);
}

function randomHpValue(min: number, max: number): number {
  return Math.floor(Math.random() * (max - min + 1)) + min;
}

function generateMockBattlePayload(): BattlePayload {
  return {
    leftHp: randomHpValue(BATTLE_HP_MIN, BATTLE_HP_MAX),
    rightHp: randomHpValue(BATTLE_HP_MIN, BATTLE_HP_MAX),
  };
}

async function putJoinClaim(
  kv: KVNamespace,
  arenaId: string,
  player: ArenaPlayer,
): Promise<ArenaJoinClaim> {
  const key = joinClaimKey(arenaId, player.id);
  const existingRaw = await kv.get(key);
  const claim: ArenaJoinClaim = existingRaw
    ? { ...(JSON.parse(existingRaw) as ArenaJoinClaim), displayName: player.displayName }
    : { ...player, joinedAt: Date.now() };

  await kv.put(key, JSON.stringify(claim), { expirationTtl: ARENA_TTL_SEC });
  return claim;
}

async function listJoinClaims(kv: KVNamespace, arenaId: string): Promise<ArenaJoinClaim[]> {
  const claims: ArenaJoinClaim[] = [];
  let cursor: string | undefined;
  do {
    const page = await kv.list({ prefix: joinClaimPrefix(arenaId), cursor });
    const pageClaims = await Promise.all(
      page.keys.map(async (key) => {
        const raw = await kv.get(key.name);
        if (!raw) return null;
        try {
          const claim = JSON.parse(raw) as ArenaJoinClaim;
          if (!Number.isInteger(claim.id) || !claim.displayName || !Number.isFinite(claim.joinedAt)) {
            return null;
          }
          return claim;
        } catch {
          return null;
        }
      }),
    );
    for (const claim of pageClaims) {
      if (claim) claims.push(claim);
    }
    cursor = page.list_complete ? undefined : page.cursor;
  } while (cursor);

  const unique = new Map<number, ArenaJoinClaim>();
  for (const claim of claims) {
    const existing = unique.get(claim.id);
    if (!existing || claim.joinedAt < existing.joinedAt) {
      unique.set(claim.id, claim);
    }
  }

  return [...unique.values()].sort((a, b) => a.joinedAt - b.joinedAt || a.id - b.id);
}

function mergeExistingPlayersAsClaims(
  arena: ArenaRecord,
  claims: ArenaJoinClaim[],
): ArenaJoinClaim[] {
  const merged = new Map<number, ArenaJoinClaim>();
  for (const claim of claims) {
    merged.set(claim.id, claim);
  }
  if (arena.player1 && !merged.has(arena.player1.id)) {
    merged.set(arena.player1.id, { ...arena.player1, joinedAt: arena.createdAt });
  }
  if (arena.player2 && !merged.has(arena.player2.id)) {
    merged.set(arena.player2.id, { ...arena.player2, joinedAt: arena.createdAt + 1 });
  }
  return [...merged.values()].sort((a, b) => a.joinedAt - b.joinedAt || a.id - b.id);
}

async function reconcileArenaPlayersFromClaims(
  kv: KVNamespace,
  arena: ArenaRecord,
): Promise<ArenaRecord> {
  if (arena.status === "done" || arena.status === "expired") {
    return arena;
  }

  const claims = mergeExistingPlayersAsClaims(arena, await listJoinClaims(kv, arena.id));
  const player1 = claims[0] ? { id: claims[0].id, displayName: claims[0].displayName } : null;
  const player2 = claims[1] ? { id: claims[1].id, displayName: claims[1].displayName } : null;
  const status: ArenaStatus = player1 && player2 ? "fighting" : "waiting";
  const hostUserId = player1?.id ?? null;
  const next: ArenaRecord = {
    ...arena,
    player1,
    player2,
    hostUserId,
    status,
    battle: arena.battle ?? generateMockBattlePayload(),
  };

  if (
    arena.player1?.id === next.player1?.id &&
    arena.player2?.id === next.player2?.id &&
    arena.status === next.status &&
    arena.hostUserId === next.hostUserId
  ) {
    return arena;
  }

  await kv.put(arenaKey(next.id), JSON.stringify(next), { expirationTtl: ARENA_TTL_SEC });
  return (await getArena(kv, next.id)) ?? next;
}

export async function createArena(
  kv: KVNamespace,
  params: {
    chatId: number;
    messageId: number;
    openerName: string;
  },
): Promise<ArenaRecord> {
  const now = Date.now();
  const arena: ArenaRecord = {
    id: newArenaId(),
    chatId: params.chatId,
    messageId: params.messageId,
    status: "waiting",
    hostUserId: null,
    openerName: params.openerName,
    player1: null,
    player2: null,
    spectatorIds: [],
    battle: generateMockBattlePayload(),
    battleStartAt: null,
    createdAt: now,
    exp: now + ARENA_TTL_SEC * 1000,
  };

  await kv.put(arenaKey(arena.id), JSON.stringify(arena), {
    expirationTtl: ARENA_TTL_SEC,
  });
  await setActiveArenaForChat(kv, params.chatId, arena.id);

  return arena;
}

export async function getArena(kv: KVNamespace, id: string): Promise<ArenaRecord | null> {
  const raw = await kv.get(arenaKey(id));
  if (!raw) return null;

  const arena = JSON.parse(raw) as ArenaRecord;
  if (arena.exp < Date.now() && arena.status !== "done") {
    arena.status = "expired";
  }
  return arena;
}

function roleForUser(arena: ArenaRecord, userId: number): ArenaRole {
  if (arena.player1?.id === userId) return "player1";
  if (arena.player2?.id === userId) return "player2";
  if (arena.hostUserId === userId) return "host";
  return "spectator";
}

function alreadyJoined(arena: ArenaRecord, userId: number): boolean {
  return (
    arena.player1?.id === userId ||
    arena.player2?.id === userId ||
    arena.spectatorIds.includes(userId) ||
    arena.hostUserId === userId
  );
}

export interface JoinArenaResult {
  arena: ArenaRecord;
  role: ArenaRole;
  isHost: boolean;
  justStarted: boolean;
}

export async function joinArena(
  kv: KVNamespace,
  arenaId: string,
  player: ArenaPlayer,
): Promise<JoinArenaResult> {
  const arena = await getArena(kv, arenaId);
  if (!arena) {
    throw new Error("Арена не найдена или истекла.");
  }
  if (arena.status === "expired" || arena.status === "done") {
    throw new Error("Эта арена уже завершена.");
  }

  const alreadyInFightingArena =
    arena.status === "fighting" &&
    arena.player1 &&
    arena.player2 &&
    (arena.player1.id === player.id || arena.player2.id === player.id);
  if (arena.status === "fighting" && !alreadyInFightingArena) {
    throw new Error("Вы не успели зайти в бой. Ждите следующую арену.");
  }

  if (!alreadyInFightingArena) {
    await putJoinClaim(kv, arenaId, player);
  }

  const reconciled = await reconcileArenaPlayersFromClaims(kv, arena);
  const role = roleForUser(reconciled, player.id);
  if (role === "spectator") {
    throw new Error("Вы не успели зайти в бой. Ждите следующую арену.");
  }

  return {
    arena: reconciled,
    role,
    isHost: reconciled.hostUserId === player.id,
    justStarted: arena.status !== "fighting" && reconciled.status === "fighting",
  };
}

export async function markArenaDone(kv: KVNamespace, arenaId: string): Promise<ArenaRecord | null> {
  const arena = await getArena(kv, arenaId);
  if (!arena) return null;
  arena.status = "done";
  await kv.put(arenaKey(arena.id), JSON.stringify(arena), {
    expirationTtl: ARENA_TTL_SEC,
  });
  const active = await getActiveArenaForChat(kv, arena.chatId);
  if (active?.id === arena.id) {
    await clearActiveArenaForChat(kv, arena.chatId);
  }
  return arena;
}

export async function updateArenaMessageId(
  kv: KVNamespace,
  arenaId: string,
  messageId: number,
): Promise<ArenaRecord | null> {
  const arena = await getArena(kv, arenaId);
  if (!arena) return null;
  arena.messageId = messageId;
  await kv.put(arenaKey(arena.id), JSON.stringify(arena), {
    expirationTtl: ARENA_TTL_SEC,
  });
  return arena;
}

export function arenaToPublicJson(arena: ArenaRecord, userId?: number) {
  return {
    id: arena.id,
    status: arena.status,
    openerName: arena.openerName,
    player1: arena.player1,
    player2: arena.player2,
    battle: arena.battle,
    battleStartAt: arena.battleStartAt,
    isHost: userId != null && arena.hostUserId === userId,
    role: userId != null ? roleForUser(arena, userId) : null,
  };
}
