import type { Env } from "../lib/env";
import { errorResponse, jsonResponse } from "../lib/env";
import { createArena, getActiveArenaForChat, markArenaDone, updateArenaMessageId } from "../lib/arena-store";
import { clearLastBotMessageId, getLastBotMessageId, setLastBotMessageId } from "../lib/chat-message-store";
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

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
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
  const kv = context.env.ARENA_KV;

  async function tryDeleteSourceMessage(): Promise<void> {
    if (!sourceMessageId || sourceMessageId <= 0) return;
    try {
      await deleteMessage(token, chatId, sourceMessageId);
    } catch {
      // Ignore: bot might not have rights to delete user messages in some chats.
    }
  }

  async function sendPersistentMessage(
    messageText: string,
    replyMarkup?: Record<string, unknown>,
  ): Promise<{ message_id: number }> {
    const previousMessageId = kv ? await getLastBotMessageId(kv, chatId) : null;
    const sent = await sendMessage(token, chatId, messageText, replyMarkup);
    if (kv) {
      await setLastBotMessageId(kv, chatId, sent.message_id);
    }
    if (previousMessageId && previousMessageId !== sent.message_id) {
      try {
        await deleteMessage(token, chatId, previousMessageId);
      } catch {
        // Ignore: previous message may already be deleted or inaccessible.
      }
    }
    return sent;
  }

  async function replacePersistentMessage(
    messageText: string,
    replyMarkup?: Record<string, unknown>,
  ): Promise<{ message_id: number }> {
    const previousMessageId = kv ? await getLastBotMessageId(kv, chatId) : null;
    if (previousMessageId) {
      try {
        await editMessageText(token, chatId, previousMessageId, messageText, replyMarkup);
        return { message_id: previousMessageId };
      } catch {
        // Fall back to a new message if Telegram cannot edit the previous one.
      }
    }
    return sendPersistentMessage(messageText, replyMarkup);
  }

  async function sendTemporaryNotice(messageText: string, ttlMs = 3000): Promise<void> {
    const sent = await sendMessage(token, chatId, messageText);
    await sleep(Math.max(0, ttlMs));
    try {
      await deleteMessage(token, chatId, sent.message_id);
    } catch {
      // Ignore: Telegram may refuse delete in some chats.
    }
  }

  try {
    if (command.startsWith("/") || text.startsWith("{")) {
      await tryDeleteSourceMessage();
    }

    if (command === "/start") {
      await sendPersistentMessage(
        "Отправьте /start_tpd_arena в групповом чате или /battle с JSON в личке.\n\n" +
          'Пример:\n/battle {"leftHp":80,"rightHp":100,"leftName":"Левый","rightName":"Правый"}',
      );
      return jsonResponse({ ok: true });
    }

    if (command === "/arena") {
      await sendPersistentMessage(
        "Команда обновлена. Используйте /start_tpd_arena.",
      );
      return jsonResponse({ ok: true });
    }

    if (command === "/stop_tpd_arena") {
      if (!isGroupChat(message.chat)) {
        await sendTemporaryNotice(
          "Команда остановки арены работает в групповом чате.",
          3000,
        );
        return jsonResponse({ ok: true });
      }

      if (!kv) {
        await sendTemporaryNotice("Арена временно недоступна (KV не настроен).", 3000);
        return jsonResponse({ ok: true });
      }

      const existing = await getActiveArenaForChat(kv, chatId);
      if (!existing) {
        await sendTemporaryNotice("Активной арены сейчас нет.", 3000);
        return jsonResponse({ ok: true });
      }

      await markArenaDone(kv, existing.id);

      const staleMessageIds = new Set<number>();
      const lastMessageId = await getLastBotMessageId(kv, chatId);
      if (lastMessageId) staleMessageIds.add(lastMessageId);
      if (existing.messageId > 0) staleMessageIds.add(existing.messageId);

      for (const messageId of staleMessageIds) {
        try {
          await deleteMessage(token, chatId, messageId);
        } catch {
          // Ignore: message may already be deleted or Telegram may refuse deletion.
        }
      }
      await clearLastBotMessageId(kv, chatId);

      await sendTemporaryNotice(
        "Текущая арена остановлена. Можно запускать новую.",
        3000,
      );
      return jsonResponse({ ok: true });
    }

    if (command === "/start_tpd_arena") {
      if (!isGroupChat(message.chat)) {
        await sendPersistentMessage(
          "Команда /start_tpd_arena работает в групповом чате. Добавьте бота в группу и вызовите арену там.",
        );
        return jsonResponse({ ok: true });
      }

      if (!kv) {
        await sendPersistentMessage("Арена временно недоступна (KV не настроен).");
        return jsonResponse({ ok: true });
      }

      const openerName = message.from ? displayName(message.from) : "Игрок";
      const botUsername = await resolveBotUsername(token, context.env.TELEGRAM_BOT_USERNAME);
      const miniApp = context.env.TELEGRAM_MINI_APP_SHORT_NAME;

      const existing = await getActiveArenaForChat(kv, chatId);
      if (existing) {
        const lockedText =
          existing.status === "fighting"
            ? "Сейчас уже идёт бой. Новая арена будет доступна после отправки видео в чат."
            : "Арена уже открыта и ждёт второго бойца. Новую арену можно запустить после завершения текущей.";
        await sendTemporaryNotice(lockedText, 3000);
        return jsonResponse({ ok: true });
      }

      const arena = await createArena(kv, {
        chatId,
        messageId: 0,
        openerName,
      });

      const sent = await replacePersistentMessage(
        arenaWaitingText(openerName, null, arena.id),
        buildGroupArenaKeyboard(botUsername, arena.id, "Войти на арену (ПК)", miniApp),
      );

      await updateArenaMessageId(kv, arena.id, sent.message_id);
      return jsonResponse({ ok: true });
    }

    if (command === "/battle" || text.startsWith("{")) {
      if (isGroupChat(message.chat)) {
        await sendPersistentMessage(
          "В группе используйте /start_tpd_arena. /battle — для личных сообщений с ботом.",
        );
        return jsonResponse({ ok: true });
      }

      const battle = extractJsonFromMessage(text);
      const ownerUserId = message.from?.id ?? chatId;
      const sent = await sendPersistentMessage(
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
    await sendPersistentMessage(messageText);
    return jsonResponse({ ok: true });
  }

  return jsonResponse({ ok: true });
};
