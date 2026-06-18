import type { Env } from "../lib/env";
import { errorResponse, jsonResponse } from "../lib/env";
import { createSessionId } from "../lib/session";
import { extractJsonFromMessage } from "../lib/validation";
import { buildWebAppKeyboard, sendMessage } from "../lib/telegram";

interface TelegramUpdate {
  message?: {
    chat: { id: number };
    text?: string;
  };
}

export const onRequestPost: PagesFunction<Env> = async (context) => {
  const token = context.env.TELEGRAM_BOT_TOKEN;
  if (!token) {
    return errorResponse("TELEGRAM_BOT_TOKEN is not configured.", 500);
  }

  let update: TelegramUpdate;
  try {
    update = await context.request.json();
  } catch {
    return errorResponse("Invalid Telegram update.", 400);
  }

  const message = update.message;
  if (!message?.text || !message.chat?.id) {
    return jsonResponse({ ok: true });
  }

  const chatId = message.chat.id;
  const text = message.text.trim();

  try {
    if (text === "/start") {
      await sendMessage(
        token,
        chatId,
        "Send /battle with JSON to open the renderer on your device.\n" +
          'Example:\n/battle {"leftHp":80,"rightHp":100}',
      );
      return jsonResponse({ ok: true });
    }

    if (text.startsWith("/battle") || text.startsWith("{")) {
      const battle = extractJsonFromMessage(text);
      const sessionId = await createSessionId(chatId, battle, token);
      const webAppUrl = `${context.env.WEB_APP_URL.replace(/\/$/, "")}/?session=${encodeURIComponent(sessionId)}`;

      await sendMessage(
        token,
        chatId,
        "Tap the button below. The battle renders on your device, then the video is sent back to this chat.",
        buildWebAppKeyboard(webAppUrl),
      );
      return jsonResponse({ ok: true });
    }
  } catch (error) {
    const messageText = error instanceof Error ? error.message : "Invalid battle request.";
    await sendMessage(token, chatId, messageText);
    return jsonResponse({ ok: true });
  }

  return jsonResponse({ ok: true });
};
