import type { Env } from "../../../lib/env";
import { errorResponse, jsonResponse } from "../../../lib/env";
import { getArena, markArenaDone } from "../../../lib/arena-store";
import { arenaAnimationCaption, sendAnimation } from "../../../lib/telegram";
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

  if (arena.status !== "fighting" && arena.status !== "done") {
    return errorResponse("Бой ещё не начался.", 400);
  }

  const contentType = context.request.headers.get("content-type") || "";
  if (!contentType.includes("multipart/form-data")) {
    return errorResponse("Expected multipart form upload.", 400);
  }

  const form = await context.request.formData();
  const rawFile = form.get("file");
  if (!rawFile || typeof rawFile === "string") {
    return errorResponse("Missing video file.", 400);
  }
  const file = rawFile as Blob;

  if (file.size > MAX_UPLOAD_BYTES) {
    return errorResponse("Video file is too large.", 413);
  }

  const filename = `arena-${arenaId}.mp4`;
  const p1 = arena.player1?.displayName || "Игрок 1";
  const p2 = arena.player2?.displayName || "Игрок 2";
  const caption = arenaAnimationCaption(p1, p2, arena.battle.leftHp, arena.battle.rightHp);

  try {
    await sendAnimation(token, arena.chatId, file, caption, filename);
    await markArenaDone(kv, arenaId);
  } catch (error) {
    const message = error instanceof Error ? error.message : "Failed to send video.";
    return errorResponse(message, 502);
  }

  return jsonResponse({ ok: true, arenaId });
};
