import type { Env } from "../../../lib/env";
import { errorResponse, jsonResponse } from "../../../lib/env";
import { arenaToPublicJson, getActiveArenaForChat, getArena, joinArena } from "../../../lib/arena-store";
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

function isMobileArenaClient(platformHeader: string, userAgent: string): boolean {
  const platform = platformHeader.toLowerCase();
  if (platform === "android" || platform === "ios") {
    return true;
  }
  const ua = userAgent.toLowerCase();
  return /android|iphone|ipad|ipod|mobile/.test(ua);
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

  const platformHeader = context.request.headers.get("X-Telegram-Platform") || "";
  const userAgent = context.request.headers.get("User-Agent") || "";
  if (isMobileArenaClient(platformHeader, userAgent)) {
    return errorResponse("Арена доступна только с ПК (Telegram Desktop/Web).", 403);
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
    const currentArena = await getArena(kv, arenaId);
    if (!currentArena) {
      return errorResponse("Арена не найдена или истекла.", 404);
    }
    const activeArena = await getActiveArenaForChat(kv, currentArena.chatId);
    if (
      activeArena &&
      activeArena.id !== arenaId &&
      currentArena.status === "waiting"
    ) {
      return errorResponse(
        `Кнопка устарела. Откройте активную арену с кодом ${activeArena.id}.`,
        409,
      );
    }

    if (
      currentArena.status === "fighting" &&
      currentArena.player1 &&
      currentArena.player2 &&
      user.id !== currentArena.player1.id &&
      user.id !== currentArena.player2.id
    ) {
      return errorResponse("Вы не успели зайти в бой. Ждите следующую арену.", 409);
    }

    const result = await joinArena(
      kv,
      arenaId,
      { id: user.id, displayName: displayNameFromUser(user) },
    );

    let arenaForClient = result.arena;
    if (arenaForClient.status === "waiting") {
      const refreshed = await getArena(kv, arenaId);
      if (
        refreshed &&
        refreshed.status === "fighting" &&
        refreshed.player1 &&
        refreshed.player2
      ) {
        arenaForClient = refreshed;
      }
    }

    if (
      arenaForClient.status === "fighting" &&
      arenaForClient.player1 &&
      arenaForClient.player2 &&
      user.id !== arenaForClient.player1.id &&
      user.id !== arenaForClient.player2.id
    ) {
      return errorResponse("Вы не успели зайти в бой. Ждите следующую арену.", 409);
    }

    const botUsername = await resolveBotUsername(token, context.env.TELEGRAM_BOT_USERNAME);
    const miniApp = context.env.TELEGRAM_MINI_APP_SHORT_NAME;

    if (arenaForClient.status === "fighting" && arenaForClient.player1 && arenaForClient.player2) {
      try {
        const noMoreJoinText =
          `${arenaFightingText(
            arenaForClient.player1.displayName,
            arenaForClient.player2.displayName,
            arenaForClient.id,
          )}\n\n` + "Набор закрыт. Если не успели — ждите следующую арену.";
        await editMessageText(
          token,
          arenaForClient.chatId,
          arenaForClient.messageId,
          noMoreJoinText,
        );
      } catch (error) {
        console.warn("editMessageText (fighting) failed:", error);
      }
    } else if (
      arenaForClient.status === "waiting" &&
      arenaForClient.player1 &&
      !arenaForClient.player2
    ) {
      try {
        await editMessageText(
          token,
          arenaForClient.chatId,
          arenaForClient.messageId,
          arenaWaitingText(arenaForClient.openerName, arenaForClient.player1, arenaForClient.id),
          buildGroupArenaKeyboard(botUsername, arenaForClient.id, "Войти на арену (ПК)", miniApp),
        );
      } catch (error) {
        console.warn("editMessageText (waiting) failed:", error);
      }
    }

    return jsonResponse({
      ...arenaToPublicJson(arenaForClient, user.id),
      justStarted:
        result.justStarted ||
        (result.arena.status === "waiting" && arenaForClient.status === "fighting"),
      needSecondPlayer:
        arenaForClient.status === "waiting" &&
        arenaForClient.player1?.id === user.id &&
        !arenaForClient.player2,
    });
  } catch (error) {
    const message = error instanceof Error ? error.message : "Не удалось войти на арену.";
    return errorResponse(message, 400);
  }
};
