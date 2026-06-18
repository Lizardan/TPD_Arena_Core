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

function commandName(text: string): string {
  const first = text.trim().split(/\s/)[0];
  return first.split("@")[0].toLowerCase();
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
  const command = commandName(text);

  try {
    if (command === "/start") {
      await sendMessage(
        token,
        chatId,
        "Отправьте /battle с JSON — бой отрендерится на вашем устройстве.\n\n" +
          'Пример:\n/battle {"leftHp":80,"rightHp":100}',
      );
      return jsonResponse({ ok: true });
    }

    if (command === "/battle" || text.startsWith("{")) {
      const battle = extractJsonFromMessage(text);
      const sessionId = await createSessionId(chatId, battle, token);
      const webAppUrl = `${context.env.WEB_APP_URL.replace(/\/$/, "")}/?session=${encodeURIComponent(sessionId)}`;

      await sendMessage(
        token,
        chatId,
        "Нажмите кнопку ниже. Бой отрисуется на вашем устройстве, затем видео придёт в этот чат.",
        buildWebAppKeyboard(webAppUrl),
      );
      return jsonResponse({ ok: true });
    }
  } catch (error) {
    const messageText = error instanceof Error ? error.message : "Некорректный запрос боя.";
    await sendMessage(token, chatId, messageText);
    return jsonResponse({ ok: true });
  }

  return jsonResponse({ ok: true });
};
