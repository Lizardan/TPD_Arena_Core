import type { Env } from "../../../lib/env";
import { errorResponse, jsonResponse } from "../../../lib/env";
import { arenaKey, getArena, markArenaDone, tryBeginArenaVideoUpload } from "../../../lib/arena-store";
import { getLastBotMessageId, setLastBotMessageId } from "../../../lib/chat-message-store";
import {
  arenaAnimationCaption,
  battleWinnerCaption,
  deleteMessage,
  sendAnimation,
} from "../../../lib/telegram";
import { verifyWebAppInitData } from "../../../lib/telegram-init";

const MAX_UPLOAD_BYTES = 50 * 1024 * 1024;

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

  const initData = context.request.headers.get("X-Telegram-Init-Data") || "";
  const user = initData ? await verifyWebAppInitData(initData, token) : null;
  if (!user) {
    return errorResponse("Недействительные данные Telegram.", 401);
  }

  const arena = await getArena(kv, arenaId);
  if (!arena) {
    return errorResponse("Арена не найдена или истекла.", 404);
  }

  if (arena.hostUserId !== user.id) {
    return errorResponse("Только хост арены может загрузить видео.", 403);
  }

  if (arena.status === "done" || arena.status === "uploading") {
    return errorResponse("Видео уже отправлено или загружается.", 409);
  }

  if (arena.status !== "fighting") {
    return errorResponse("Бой ещё не начался.", 400);
  }

  const lockedArena = await tryBeginArenaVideoUpload(kv, arenaId, user.id);
  if (!lockedArena) {
    return errorResponse("Видео уже отправлено или загружается.", 409);
  }

  const contentType = context.request.headers.get("content-type") || "";
  if (!contentType.includes("multipart/form-data")) {
    return errorResponse("Expected multipart form upload.", 400);
  }

  const form = await context.request.formData();
  const rawWinner = form.get("winner");
  const parsedWinner =
    typeof rawWinner === "string" ? Number.parseInt(rawWinner.trim(), 10) : Number.NaN;
  const winnerSide = [0, 1, 2].includes(parsedWinner) ? parsedWinner : null;
  const rawFile = form.get("file");
  if (!rawFile || typeof rawFile === "string") {
    return errorResponse("Missing video file.", 400);
  }
  const file = rawFile as File;

  if (file.size > MAX_UPLOAD_BYTES) {
    return errorResponse("Video file is too large.", 413);
  }

  const fileType = (file.type || "").toLowerCase();
  const fileName = (file.name || "").toLowerCase();
  if (fileType.includes("webm") || fileName.endsWith(".webm")) {
    return errorResponse("Для автопроигрывания поддерживается только MP4 (H264).", 415);
  }
  const filename = `arena-${arenaId}.mp4`;
  const p1 = lockedArena.player1?.displayName || "Игрок 1";
  const p2 = lockedArena.player2?.displayName || "Игрок 2";
  const winnerCaption = battleWinnerCaption(p1, p2, winnerSide);
  const caption =
    winnerCaption ??
    arenaAnimationCaption(p1, p2, lockedArena.battle.leftHp, lockedArena.battle.rightHp);

  try {
    const previousMessageId = await getLastBotMessageId(kv, lockedArena.chatId);
    const sent = await sendAnimation(token, lockedArena.chatId, file, caption, filename);
    await setLastBotMessageId(kv, lockedArena.chatId, sent.message_id);

    const staleMessageIds = new Set<number>();
    if (previousMessageId && previousMessageId !== sent.message_id) {
      staleMessageIds.add(previousMessageId);
    }
    if (lockedArena.messageId > 0 && lockedArena.messageId !== sent.message_id) {
      staleMessageIds.add(lockedArena.messageId);
    }
    for (const staleMessageId of staleMessageIds) {
      try {
        await deleteMessage(token, lockedArena.chatId, staleMessageId);
      } catch (error) {
        console.warn("deleteMessage (arena stale message) failed:", error);
      }
    }
    await markArenaDone(kv, arenaId);
  } catch (error) {
    try {
      await kv.put(arenaKey(arenaId), JSON.stringify({ ...lockedArena, status: "fighting" }), {
        expirationTtl: 30 * 60,
      });
      await kv.delete(`arena-upload:${arenaId}`);
    } catch (rollbackError) {
      console.warn("arena upload rollback failed:", rollbackError);
    }
    const message = error instanceof Error ? error.message : "Failed to send video.";
    return errorResponse(message, 502);
  }

  return jsonResponse({ ok: true, arenaId });
};
