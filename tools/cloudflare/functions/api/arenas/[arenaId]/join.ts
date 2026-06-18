import type { Env } from "../../../lib/env";
import { errorResponse, jsonResponse } from "../../../lib/env";
import { arenaToPublicJson, joinArena } from "../../../lib/arena-store";
import {
  arenaFightingText,
  arenaWaitingText,
  buildGroupArenaKeyboard,
  editMessageText,
  resolveBotUsername,
} from "../../../lib/telegram";
import { displayNameFromUser, verifyWebAppInitDataDetailed } from "../../../lib/telegram-init";

interface JoinBody {
  initData?: string;
}

export const onRequestPost: PagesFunction<Env> = async (context) => {
  const token = context.env.TELEGRAM_BOT_TOKEN?.trim().replace(/\r/g, "") ?? "";
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

  const initData =
    body.initData?.trim() ||
    context.request.headers.get("X-Telegram-Init-Data")?.trim() ||
    "";
  if (!initData) {
    return errorResponse("initData is required.", 400);
  }

  const verified = await verifyWebAppInitDataDetailed(initData, token);
  if (!verified.user) {
    let hint = "";
    if (verified.reason === "bad_hash") {
      try {
        const botUsername = await resolveBotUsername(token, context.env.TELEGRAM_BOT_USERNAME);
        hint = ` Токен на сервере — бот @${botUsername}. Откройте Mini App через этого же бота в BotFather (Main Mini App).`;
      } catch {
        hint = " Проверьте TELEGRAM_BOT_TOKEN в GitHub Secrets и Cloudflare Pages.";
      }
    }
    return errorResponse(`Недействительные данные Telegram (${verified.reason ?? "unknown"}).${hint}`, 401);
  }
  const user = verified.user;

  try {
    const result = await joinArena(
      kv,
      arenaId,
      { id: user.id, displayName: displayNameFromUser(user) },
      context.env.STATS_API_URL,
    );

    const botUsername = await resolveBotUsername(token, context.env.TELEGRAM_BOT_USERNAME);
    const miniApp = context.env.TELEGRAM_MINI_APP_SHORT_NAME;

    if (result.justStarted && result.arena.player1 && result.arena.player2) {
      try {
        await editMessageText(
          token,
          result.arena.chatId,
          result.arena.messageId,
          arenaFightingText(
            result.arena.player1.displayName,
            result.arena.player2.displayName,
            result.arena.id,
          ),
          buildGroupArenaKeyboard(botUsername, result.arena.id, "Смотреть бой", miniApp),
        );
      } catch (error) {
        console.warn("editMessageText (fighting) failed:", error);
      }
    } else if (
      result.arena.status === "waiting" &&
      result.arena.player1 &&
      !result.arena.player2
    ) {
      try {
        await editMessageText(
          token,
          result.arena.chatId,
          result.arena.messageId,
          arenaWaitingText(result.arena.openerName, result.arena.player1, result.arena.id),
          buildGroupArenaKeyboard(botUsername, result.arena.id, "Войти на арену", miniApp),
        );
      } catch (error) {
        console.warn("editMessageText (waiting) failed:", error);
      }
    }

    return jsonResponse({
      ...arenaToPublicJson(result.arena, user.id),
      justStarted: result.justStarted,
      needSecondPlayer:
        result.arena.status === "waiting" &&
        result.arena.player1?.id === user.id &&
        !result.arena.player2,
    });
  } catch (error) {
    const message = error instanceof Error ? error.message : "Не удалось войти на арену.";
    return errorResponse(message, 400);
  }
};
