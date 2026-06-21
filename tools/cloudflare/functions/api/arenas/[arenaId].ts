import type { Env } from "../../lib/env";
import { errorResponse, jsonResponse } from "../../lib/env";
import { arenaToPublicJson, getArena } from "../../lib/arena-store";
import { verifyWebAppInitData } from "../../lib/telegram-init";

export const onRequestGet: PagesFunction<Env> = async (context) => {
  const token = context.env.TELEGRAM_BOT_TOKEN;
  if (!token) {
    return errorResponse("TELEGRAM_BOT_TOKEN is not configured.", 500);
  }

  const arenaId = context.params.arenaId;
  if (!arenaId || Array.isArray(arenaId)) {
    return errorResponse("Arena id is required.", 400);
  }

  const kv = context.env.ARENA_KV;
  if (!kv) {
    return errorResponse("ARENA_KV is not configured.", 500);
  }

  const arena = await getArena(kv, arenaId);
  if (!arena) {
    return errorResponse("Арена не найдена или истекла.", 404);
  }

  const initData = context.request.headers.get("X-Telegram-Init-Data") || "";
  const user = initData
    ? await verifyWebAppInitData(initData, token.trim().replace(/\r/g, ""))
    : null;
  if (!user) {
    return errorResponse("Недействительные data Telegram.", 401);
  }

  return jsonResponse(arenaToPublicJson(arena, user.id), 200, {
    "Cache-Control": "no-store",
  });
};
