import type { Env } from "../../../lib/env";
import { errorResponse, jsonResponse } from "../../../lib/env";
import { arenaToPublicJson, joinArena } from "../../../lib/arena-store";
import {
  arenaFightingText,
  arenaWaitingText,
  buildGroupArenaKeyboard,
  editMessageText,
} from "../../../lib/telegram";
import { displayNameFromUser, verifyWebAppInitData } from "../../../lib/telegram-init";

interface JoinBody {
  initData?: string;
}

export const onRequestPost: PagesFunction<Env> = async (context) => {
  const token = context.env.TELEGRAM_BOT_TOKEN;
  if (!token) {
    return errorResponse("TELEGRAM_BOT_TOKEN is not configured.", 500);
  }

  const kv = context.env.ARENA_KV;
  if (!kv) {
    return errorResponse("ARENA_KV is not configured.", 500);
  }

  const arenaId = context.params.arenaId;
  if (!arenaId || Array.isArray(arenaId)) {
    return errorResponse("Arena id is required.", 400);
  }

  let body: JoinBody;
  try {
    body = await context.request.json();
  } catch {
    return errorResponse("Invalid JSON body.", 400);
  }

  if (!body.initData) {
    return errorResponse("initData is required.", 400);
  }

  const user = await verifyWebAppInitData(body.initData, token);
  if (!user) {
    return errorResponse("Недействительные данные Telegram.", 401);
  }

  try {
    const result = await joinArena(
      kv,
      arenaId,
      { id: user.id, displayName: displayNameFromUser(user) },
      context.env.STATS_API_URL,
    );

    if (result.justStarted && result.arena.player1 && result.arena.player2) {
      const botUsername = context.env.TELEGRAM_BOT_USERNAME || "TPD_Arena_bot";
      await editMessageText(
        token,
        result.arena.chatId,
        result.arena.messageId,
        arenaFightingText(result.arena.player1.displayName, result.arena.player2.displayName),
        buildGroupArenaKeyboard(botUsername, result.arena.id, "Смотреть бой"),
      );
    } else if (
      result.arena.status === "waiting" &&
      result.arena.player1 &&
      !result.arena.player2
    ) {
      const botUsername = context.env.TELEGRAM_BOT_USERNAME || "TPD_Arena_bot";
      await editMessageText(
        token,
        result.arena.chatId,
        result.arena.messageId,
        arenaWaitingText(result.arena.openerName, result.arena.player1),
        buildGroupArenaKeyboard(botUsername, result.arena.id, "Войти на арену"),
      );
    }

    return jsonResponse({
      ...arenaToPublicJson(result.arena, user.id),
      justStarted: result.justStarted,
    });
  } catch (error) {
    const message = error instanceof Error ? error.message : "Не удалось войти на арену.";
    return errorResponse(message, 400);
  }
};
