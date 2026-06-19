import type { Env } from "../lib/env";
import { errorResponse, jsonResponse } from "../lib/env";
import { createArena, getActiveArenaForChat, updateArenaMessageId } from "../lib/arena-store";
import {
  arenaWaitingText,
  buildGroupArenaKeyboard,
  buildWebAppKeyboard,
  deleteMessage,
  editMessageText,
  resolveBotUsername,
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
  message_id: number;
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

function timingSafeEqual(a: string, b: string): boolean {
  if (a.length !== b.length) return false;
  let result = 0;
  for (let i = 0; i < a.length; i++) {
    result |= a.charCodeAt(i) ^ b.charCodeAt(i);
  }
  return result === 0;
}

async function webhookSecretFromToken(token: string): Promise<string> {
  const input = `${token.trim()}:tpd-arena-webhook`;
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(input));
  return [...new Uint8Array(digest)].map((b) => b.toString(16).padStart(2, "0")).join("");
}

export const onRequestPost: PagesFunction<Env> = async (context) => {
  const token = context.env.TELEGRAM_BOT_TOKEN?.trim().replace(/\r/g, "") ?? "";
  if (!token) {
    return errorResponse("TELEGRAM_BOT_TOKEN is not configured.", 500);
  }

  const incomingSecret =
    context.request.headers.get("X-Telegram-Bot-Api-Secret-Token")?.trim() || "";
  const expectedSecret = await webhookSecretFromToken(token);
  if (!incomingSecret || !timingSafeEqual(incomingSecret, expectedSecret)) {
    return errorResponse("Forbidden.", 403);
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
  const sourceMessageId = message.message_id;

  async function tryDeleteSourceMessage(): Promise<void> {
    if (!sourceMessageId || sourceMessageId <= 0) return;
    try {
      await deleteMessage(token, chatId, sourceMessageId);
    } catch {
      // Ignore: bot might not have rights to delete user messages in some chats.
    }
  }

  try {
    if (command === "/start") {
      await sendMessage(
        token,
        chatId,
        "Отправьте /start_tpd_arena в групповом чате или /battle с JSON в личке.\n\n" +
          'Пример:\n/battle {"leftHp":80,"rightHp":100,"leftName":"Левый","rightName":"Правый"}',
      );
      return jsonResponse({ ok: true });
    }

    if (command === "/arena") {
      await sendMessage(
        token,
        chatId,
        "Команда обновлена. Используйте /start_tpd_arena.",
      );
      return jsonResponse({ ok: true });
    }

    if (command === "/start_tpd_arena") {
      if (!isGroupChat(message.chat)) {
        await sendMessage(
          token,
          chatId,
          "Команда /start_tpd_arena работает в групповом чате. Добавьте бота в группу и вызовите арену там.",
        );
        return jsonResponse({ ok: true });
      }

      const kv = context.env.ARENA_KV;
      if (!kv) {
        await sendMessage(token, chatId, "Арена временно недоступна (KV не настроен).");
        return jsonResponse({ ok: true });
      }

      const openerName = message.from ? displayName(message.from) : "Игрок";
      const botUsername = await resolveBotUsername(token, context.env.TELEGRAM_BOT_USERNAME);
      const miniApp = context.env.TELEGRAM_MINI_APP_SHORT_NAME;

      await tryDeleteSourceMessage();

      const existing = await getActiveArenaForChat(kv, chatId);
      if (existing) {
        const lockedText =
          existing.status === "fighting"
            ? "Сейчас уже идёт бой. Новая арена будет доступна после отправки видео в чат."
            : "Арена уже открыта и ждёт второго бойца. Новую арену можно запустить после завершения текущей.";
        await sendMessage(
          token,
          chatId,
          lockedText,
        );
        return jsonResponse({ ok: true });
      }

      const arena = await createArena(kv, {
        chatId,
        messageId: 0,
        openerName,
      });

      const sent = await sendMessage(
        token,
        chatId,
        arenaWaitingText(openerName, null, arena.id),
        buildGroupArenaKeyboard(botUsername, arena.id, "Войти на арену (ПК)", miniApp),
      );

      await updateArenaMessageId(kv, arena.id, sent.message_id);
      return jsonResponse({ ok: true });
    }

    if (command === "/battle" || text.startsWith("{")) {
      if (isGroupChat(message.chat)) {
        await sendMessage(
          token,
          chatId,
          "В группе используйте /start_tpd_arena. /battle — для личных сообщений с ботом.",
        );
        return jsonResponse({ ok: true });
      }

      const battle = extractJsonFromMessage(text);
      const ownerUserId = message.from?.id ?? chatId;
      await tryDeleteSourceMessage();
      const sent = await sendMessage(
        token,
        chatId,
        "Нажмите кнопку ниже. Бой отрисуется на вашем устройстве, затем видео придёт в этот чат.",
      );
      const sessionId = await createSessionId(chatId, battle, token, ownerUserId, sent.message_id);
      const webAppUrl = `${context.env.WEB_APP_URL.replace(/\/$/, "")}/?session=${encodeURIComponent(sessionId)}`;

      await editMessageText(
        token,
        chatId,
        sent.message_id,
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
