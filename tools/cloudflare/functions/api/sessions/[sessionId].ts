import type { Env } from "../../lib/env";
import { errorResponse, jsonResponse } from "../../lib/env";
import { verifySessionId } from "../../lib/session";

export const onRequestGet: PagesFunction<Env> = async (context) => {
  const token = context.env.TELEGRAM_BOT_TOKEN;
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

  return jsonResponse({
    sessionId,
    battle: session.battle,
    completed: false,
  });
};
