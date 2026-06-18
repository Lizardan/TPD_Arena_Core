import type { BattlePayload } from "./validation";
import { fetchPlayerStats } from "./stats-api";

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
const MAX_JOIN_RETRIES = 5;

export function arenaKey(id: string): string {
  return `${KV_PREFIX}${id}`;
}

function newArenaId(): string {
  return crypto.randomUUID().replace(/-/g, "").slice(0, 16);
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
    battle: { leftHp: 100, rightHp: 100 },
    battleStartAt: null,
    createdAt: now,
    exp: now + ARENA_TTL_SEC * 1000,
  };

  await kv.put(arenaKey(arena.id), JSON.stringify(arena), {
    expirationTtl: ARENA_TTL_SEC,
  });

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
  if (arena.hostUserId === userId) return "host";
  if (arena.player1?.id === userId) return "player1";
  if (arena.player2?.id === userId) return "player2";
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
  statsApiUrl?: string,
): Promise<JoinArenaResult> {
  for (let attempt = 0; attempt < MAX_JOIN_RETRIES; attempt++) {
    const arena = await getArena(kv, arenaId);
    if (!arena) {
      throw new Error("Арена не найдена или истекла.");
    }
    if (arena.status === "expired" || arena.status === "done") {
      throw new Error("Эта арена уже завершена.");
    }

    if (alreadyJoined(arena, player.id)) {
      return {
        arena,
        role: roleForUser(arena, player.id),
        isHost: arena.hostUserId === player.id,
        justStarted: false,
      };
    }

    let justStarted = false;

    if (arena.status === "fighting") {
      if (!arena.spectatorIds.includes(player.id)) {
        arena.spectatorIds.push(player.id);
        await kv.put(arenaKey(arena.id), JSON.stringify(arena), {
          expirationTtl: ARENA_TTL_SEC,
        });
      }
      return {
        arena,
        role: "spectator",
        isHost: false,
        justStarted: false,
      };
    }

    if (!arena.player1) {
      arena.player1 = player;
      arena.hostUserId = player.id;
    } else if (!arena.player2 && arena.player1.id !== player.id) {
      arena.player2 = player;
      arena.battle = await fetchPlayerStats(
        arena.player1.displayName,
        player.displayName,
        statsApiUrl,
      );
      arena.status = "fighting";
      justStarted = true;
    } else {
      if (!arena.spectatorIds.includes(player.id)) {
        arena.spectatorIds.push(player.id);
      }
      await kv.put(arenaKey(arena.id), JSON.stringify(arena), {
        expirationTtl: ARENA_TTL_SEC,
      });
      return {
        arena,
        role: "spectator",
        isHost: arena.hostUserId === player.id,
        justStarted: false,
      };
    }

    await kv.put(arenaKey(arena.id), JSON.stringify(arena), {
      expirationTtl: ARENA_TTL_SEC,
    });

    return {
      arena,
      role: roleForUser(arena, player.id),
      isHost: arena.hostUserId === player.id,
      justStarted,
    };
  }

  throw new Error("Не удалось занять слот. Попробуйте ещё раз.");
}

export async function markArenaDone(kv: KVNamespace, arenaId: string): Promise<ArenaRecord | null> {
  const arena = await getArena(kv, arenaId);
  if (!arena) return null;
  arena.status = "done";
  await kv.put(arenaKey(arena.id), JSON.stringify(arena), {
    expirationTtl: ARENA_TTL_SEC,
  });
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
