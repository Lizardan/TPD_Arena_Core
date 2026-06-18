import type { Env } from "../lib/env";
import { errorResponse, jsonResponse } from "../lib/env";
import { createArena, updateArenaMessageId } from "../lib/arena-store";
import {
  arenaWaitingText,
  buildGroupArenaKeyboard,
  buildWebAppKeyboard,
  sendMessage,
} from "../lib/telegram";
import { extractJsonFromMessage } from "../lib/validation";
import { createSessionId } from "../lib/session";

interface TelegramUser {
  id: number;
  first_name: string;
  last_name?: string;
  username?: string;
}

interface TelegramChat {
  id: number;
  type: string;
}

interface TelegramMessage {
  chat: TelegramChat;
  text?: string;
  from?: TelegramUser;
}

interface TelegramUpdate {
  message?: TelegramMessage;
}

function commandName(text: string): string {
  const first = text.trim().split(/\s/)[0];
  return first.split("@")[0].toLowerCase();
}

function displayName(user: TelegramUser): string {
  if (user.username) return `@${user.username}`;
  const parts = [user.first_name, user.last_name].filter(Boolean);
  return parts.join(" ") || `user_${user.id}`;
}

function isGroupChat(chat: TelegramChat): boolean {
  return chat.type === "group" || chat.type === "supergroup";
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
        "Отправьте /arena в групповом чате или /battle с JSON в личке.\n\n" +
          'Пример:\n/battle {"leftHp":80,"rightHp":100}',
      );
      return jsonResponse({ ok: true });
    }

    if (command === "/arena") {
      if (!isGroupChat(message.chat)) {
        await sendMessage(
          token,
          chatId,
          "Команда /arena работает в групповом чате. Добавьте бота в группу и вызовите арену там.",
        );
        return jsonResponse({ ok: true });
      }

      const kv = context.env.ARENA_KV;
      if (!kv) {
        await sendMessage(token, chatId, "Арена временно недоступна (KV не настроен).");
        return jsonResponse({ ok: true });
      }

      const openerName = message.from ? displayName(message.from) : "Игрок";
      const arena = await createArena(kv, {
        chatId,
        messageId: 0,
        openerName,
      });

      const botUsername = context.env.TELEGRAM_BOT_USERNAME || "TPD_Arena_bot";
      const sent = await sendMessage(
        token,
        chatId,
        arenaWaitingText(openerName, null),
        buildGroupArenaKeyboard(botUsername, arena.id, "Войти на арену"),
      );

      await updateArenaMessageId(kv, arena.id, sent.message_id);
      return jsonResponse({ ok: true });
    }

    if (command === "/battle" || text.startsWith("{")) {
      if (isGroupChat(message.chat)) {
        await sendMessage(
          token,
          chatId,
          "В группе используйте /arena. /battle — для личных сообщений с ботом.",
        );
        return jsonResponse({ ok: true });
      }

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
    const messageText = error instanceof Error ? error.message : "Некорректный запрос.";
    await sendMessage(token, chatId, messageText);
    return jsonResponse({ ok: true });
  }

  return jsonResponse({ ok: true });
};
