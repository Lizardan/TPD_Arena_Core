import type { Env } from "../../lib/env";
import { errorResponse, jsonResponse } from "../../lib/env";
import { verifySessionId } from "../../lib/session";
import { verifyWebAppInitData } from "../../lib/telegram-init";

export const onRequestGet: PagesFunction<Env> = async (context) => {
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
    return errorResponse("Сессия принадлежит другому пользователю.", 403);
  }

  return jsonResponse({
    sessionId,
    battle: session.battle,
    completed: false,
  });
};
