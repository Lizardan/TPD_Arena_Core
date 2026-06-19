import type { Env } from "../../../lib/env";
import { errorResponse, jsonResponse } from "../../../lib/env";
import { verifySessionId } from "../../../lib/session";
import { sendAnimation } from "../../../lib/telegram";
import { verifyWebAppInitData } from "../../../lib/telegram-init";

const MAX_UPLOAD_BYTES = 50 * 1024 * 1024;

export const onRequestPost: PagesFunction<Env> = async (context) => {
  const token = context.env.TELEGRAM_BOT_TOKEN?.trim().replace(/\r/g, "") ?? "";
  if (!token) {
    return errorResponse("TELEGRAM_BOT_TOKEN is not configured.", 500);
  }

  const sessionId = context.params.sessionId;
  if (!sessionId || Array.isArray(sessionId)) {
    return errorResponse("Session id is required.", 400);
  }

  const session = await verifySessionId(sessionId, token);
  if (!session) {
    return errorResponse("Session not found or expired.", 404);
  }

  const initData = context.request.headers.get("X-Telegram-Init-Data") || "";
  const user = initData ? await verifyWebAppInitData(initData, token) : null;
  if (!user) {
    return errorResponse("Недействительные данные Telegram.", 401);
  }
  if (user.id !== session.userId) {
    return errorResponse("Только владелец сессии может загрузить видео.", 403);
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

  const filename = `battle-${sessionId}.mp4`;
  const caption = `Бой: ${session.battle.leftHp} HP против ${session.battle.rightHp} HP`;

  try {
    await sendAnimation(token, session.chatId, file, caption, filename);
  } catch (error) {
    const message = error instanceof Error ? error.message : "Failed to send video.";
    return errorResponse(message, 502);
  }

  return jsonResponse({ ok: true, sessionId });
};
