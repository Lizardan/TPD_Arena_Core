import type { Env } from "../lib/env";
import { errorResponse, jsonResponse } from "../lib/env";
import { createArenaIfNoneActive, createArenaPveIfNoneActive, getActiveArenaForChat, markArenaDone, updateArenaMessageId } from "../lib/arena-store";
import type { ArenaRecord } from "../lib/arena-store";
import { getLastBotMessageId, setLastBotMessageId } from "../lib/chat-message-store";
import { isDuplicateTelegramUpdate } from "../lib/update-dedup";
import {
  answerCallbackQuery,
  arenaFightingText,
  arenaPveOpenText,
  arenaWaitingText,
  buildBotReplyKeyboard,
  buildGroupArenaKeyboard,
  buildWebAppKeyboard,
  deleteMessageQuiet,
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
  new_chat_members?: TelegramUser[];
}

interface TelegramUpdate {
  update_id?: number;
  message?: TelegramMessage;
  callback_query?: TelegramCallbackQuery;
  my_chat_member?: TelegramChatMemberUpdated;
}

interface TelegramCallbackQuery {
  id: string;
  from: TelegramUser;
  message?: TelegramMessage;
  data?: string;
}

interface TelegramChatMemberUpdated {
  chat: TelegramChat;
  old_chat_member: {
    status: string;
    user: TelegramUser;
  };
  new_chat_member: {
    status: string;
    user: TelegramUser;
  };
}

const SETUP_KEYBOARD_MESSAGE =
  "Кнопки арены включены возле поля ввода.\n\n" +
  "Чтобы нажатия кнопок не оставались в чате, дайте боту права администратора: «Удаление сообщений».";

const STOPPED_ARENA_MESSAGE = "Текущая арена остановлена. Можно запускать новую.";

interface BotMessageRestore {
  text: string;
  replyMarkup?: Record<string, unknown>;
}

async function deleteTrackedBotMessage(
  token: string,
  kv: KVNamespace | undefined,
  chatId: number,
  messageId: number | null | undefined,
): Promise<void> {
  if (!messageId || messageId <= 0) return;
  await deleteMessageQuiet(token, chatId, messageId);
}

async function sendPersistentBotMessage(
  token: string,
  kv: KVNamespace | undefined,
  chatId: number,
  messageText: string,
  replyMarkup?: Record<string, unknown>,
): Promise<{ message_id: number }> {
  const previousMessageId = kv ? await getLastBotMessageId(kv, chatId) : null;
  const sent = await sendMessage(token, chatId, messageText, replyMarkup);
  if (kv) {
    await setLastBotMessageId(kv, chatId, sent.message_id);
  }
  if (previousMessageId && previousMessageId !== sent.message_id) {
    await deleteTrackedBotMessage(token, kv, chatId, previousMessageId);
  }
  return sent;
}

async function replacePersistentBotMessage(
  token: string,
  kv: KVNamespace | undefined,
  chatId: number,
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
  return sendPersistentBotMessage(token, kv, chatId, messageText, replyMarkup);
}

async function sendTemporaryNotice(
  token: string,
  kv: KVNamespace | undefined,
  chatId: number,
  noticeText: string,
  restore?: BotMessageRestore,
  ttlMs = 3000,
): Promise<void> {
  const trackedMessageId = kv ? await getLastBotMessageId(kv, chatId) : null;
  if (trackedMessageId && restore) {
    try {
      await editMessageText(token, chatId, trackedMessageId, noticeText);
      await sleep(Math.max(0, ttlMs));
      await editMessageText(token, chatId, trackedMessageId, restore.text, restore.replyMarkup);
      return;
    } catch {
      // Fall back below if edit is not possible.
    }
  }

  const previousMessageId = trackedMessageId;
  const sent = await sendMessage(token, chatId, noticeText);
  await sleep(Math.max(0, ttlMs));
  await deleteTrackedBotMessage(token, kv, chatId, sent.message_id);
  if (restore) {
    await sendPersistentBotMessage(token, kv, chatId, restore.text, restore.replyMarkup);
  } else if (previousMessageId) {
    await deleteTrackedBotMessage(token, kv, chatId, previousMessageId);
  }
}

async function sendSetupKeyboardMessage(
  token: string,
  kv: KVNamespace | undefined,
  chatId: number,
): Promise<{ message_id: number }> {
  return replacePersistentBotMessage(
    token,
    kv,
    chatId,
    SETUP_KEYBOARD_MESSAGE,
    buildBotReplyKeyboard(),
  );
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

function normalizeUserText(value: string): string {
  return value.trim().replace(/\s+/g, " ");
}

function mapReplyKeyboardText(rawText: string): string | null {
  const normalized = normalizeUserText(rawText);
  if (
    normalized === "Арена против игрока" ||
    normalized === "Создать арену" ||
    normalized === "Выйти на арену" ||
    normalized === "Запустить арену"
  ) {
    return "/start_tpd_arena";
  }
  if (normalized === "Арена против бота" || normalized === "Арена против Бота" || normalized === "Бой с компьютером") {
    return "/start_tpd_bot";
  }
  if (normalized === "Остановить арену") {
    return "/stop_tpd_arena";
  }
  return null;
}

function scheduleBackground(context: EventContext<Env, string, unknown>, task: Promise<unknown>): void {
  context.waitUntil(
    task.catch((error) => {
      console.error("telegram webhook background task failed:", error);
    }),
  );
}

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function respondTemporaryNotice(
  context: EventContext<Env, string, unknown>,
  token: string,
  kv: KVNamespace | undefined,
  chatId: number,
  noticeText: string,
  restore?: BotMessageRestore,
): Response {
  scheduleBackground(context, sendTemporaryNotice(token, kv, chatId, noticeText, restore));
  return jsonResponse({ ok: true });
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

function existingArenaLockedNotice(
  existing: ArenaRecord,
  botUsername: string,
  miniApp?: string,
): { lockedText: string; restore: BotMessageRestore } {
  if (existing.mode === "pve") {
    const hostName = existing.player1?.displayName || existing.openerName;
    const { leftHp, rightHp } = existing.battle;
    return {
      lockedText:
        existing.status === "fighting"
          ? "Сейчас уже идёт бой с ботом. Новая арена будет доступна после видео в чате."
          : "Арена с ботом уже открыта. Дождитесь завершения текущей.",
      restore: {
        text: arenaPveOpenText(hostName, leftHp, rightHp, existing.id),
        replyMarkup: buildGroupArenaKeyboard(
          botUsername,
          existing.id,
          "Начать бой с ботом (ПК)",
          miniApp,
        ),
      },
    };
  }

  const lockedText =
    existing.status === "fighting"
      ? "Сейчас уже идёт бой. Новая арена будет доступна после отправки видео в чат."
      : "Арена уже открыта и ждёт второго бойца. Новую арену можно запустить после завершения текущей.";

  let restore: BotMessageRestore;
  if (existing.status === "fighting" && existing.player1 && existing.player2) {
    restore = {
      text:
        `${arenaFightingText(
          existing.player1.displayName,
          existing.player2.displayName,
          existing.id,
        )}\n\n` + "Набор закрыт. Если не успели — ждите следующую арену.",
    };
  } else if (existing.player1) {
    restore = {
      text: arenaWaitingText(existing.openerName, existing.player1, existing.id),
      replyMarkup: buildGroupArenaKeyboard(
        botUsername,
        existing.id,
        "Войти на арену (ПК)",
        miniApp,
      ),
    };
  } else {
    restore = {
      text: arenaWaitingText(existing.openerName, null, existing.id),
      replyMarkup: buildGroupArenaKeyboard(
        botUsername,
        existing.id,
        "Войти на арену (ПК)",
        miniApp,
      ),
    };
  }

  return { lockedText, restore };
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

  const kv = context.env.ARENA_KV;
  if (await isDuplicateTelegramUpdate(kv, update.update_id)) {
    return jsonResponse({ ok: true });
  }

  const myChatMember = update.my_chat_member;
  const botJoinedGroup =
    myChatMember &&
    isGroupChat(myChatMember.chat) &&
    ["left", "kicked"].includes(myChatMember.old_chat_member.status) &&
    ["member", "administrator"].includes(myChatMember.new_chat_member.status);
  if (botJoinedGroup && myChatMember) {
    await sendSetupKeyboardMessage(token, context.env.ARENA_KV, myChatMember.chat.id);
    return jsonResponse({ ok: true });
  }

  const callback = update.callback_query;
  const message = update.message ?? callback?.message;
  const callbackCommand =
    callback?.data === "start_tpd_arena"
      ? "/start_tpd_arena"
      : callback?.data === "start_tpd_bot"
        ? "/start_tpd_bot"
        : callback?.data === "stop_tpd_arena"
          ? "/stop_tpd_arena"
          : null;

  if (!message?.chat?.id || (!update.message?.text && !callbackCommand)) {
    if (callback?.id) {
      await answerCallbackQuery(token, callback.id, "Неизвестная кнопка.");
    }
    return jsonResponse({ ok: true });
  }

  const chatId = message.chat.id;
  const rawText = callbackCommand ?? update.message?.text ?? "";
  const mappedReplyKeyboard = update.message?.text ? mapReplyKeyboardText(rawText) : null;
  const isReplyKeyboardAction = mappedReplyKeyboard != null;
  const text = mappedReplyKeyboard ?? normalizeUserText(rawText);
  const command = commandName(text);
  const sourceMessageId = update.message?.message_id ?? 0;
  const actor = update.message?.from ?? callback?.from;

  async function tryDeleteUserMessage(): Promise<boolean> {
    if (!sourceMessageId || sourceMessageId <= 0) return false;
    return deleteMessageQuiet(token, chatId, sourceMessageId);
  }

  const setupRestore: BotMessageRestore = {
    text: SETUP_KEYBOARD_MESSAGE,
    replyMarkup: buildBotReplyKeyboard(),
  };

  try {
    if (callback?.id) {
      await answerCallbackQuery(token, callback.id);
    }

    if (isReplyKeyboardAction || command.startsWith("/") || text.startsWith("{")) {
      await tryDeleteUserMessage();
    }

    if (command === "/start") {
      await sendSetupKeyboardMessage(token, kv, chatId);
      return jsonResponse({ ok: true });
    }

    if (command === "/arena") {
      await sendPersistentBotMessage(
        token,
        kv,
        chatId,
        "Команда обновлена. Используйте кнопку «Арена против игрока».",
        buildBotReplyKeyboard(),
      );
      return jsonResponse({ ok: true });
    }

    if (command === "/stop_tpd_arena") {
      if (!isGroupChat(message.chat)) {
        return respondTemporaryNotice(
          context,
          token,
          kv,
          chatId,
          "Команда остановки арены работает в групповом чате.",
        );
      }

      if (!kv) {
        return respondTemporaryNotice(
          context,
          token,
          kv,
          chatId,
          "Арена временно недоступна (KV не настроен).",
        );
      }

      const existing = await getActiveArenaForChat(kv, chatId);
      if (!existing) {
        return respondTemporaryNotice(
          context,
          token,
          kv,
          chatId,
          "Активной арены сейчас нет.",
          setupRestore,
        );
      }

      await markArenaDone(kv, existing.id);
      if (existing.messageId > 0) {
        const trackedMessageId = await getLastBotMessageId(kv, chatId);
        if (trackedMessageId !== existing.messageId) {
          await deleteTrackedBotMessage(token, kv, chatId, existing.messageId);
        }
      }

      await sendPersistentBotMessage(
        token,
        kv,
        chatId,
        STOPPED_ARENA_MESSAGE,
        buildBotReplyKeyboard(),
      );
      return jsonResponse({ ok: true });
    }

    if (command === "/start_tpd_arena") {
      if (!isGroupChat(message.chat)) {
        await sendPersistentBotMessage(
          token,
          kv,
          chatId,
          "Команда /start_tpd_arena работает в групповом чате. Добавьте бота в группу и вызовите арену там.",
        );
        return jsonResponse({ ok: true });
      }

      if (!kv) {
        await sendPersistentBotMessage(token, kv, chatId, "Арена временно недоступна (KV не настроен).");
        return jsonResponse({ ok: true });
      }

      const openerName = actor ? displayName(actor) : "Игрок";
      const botUsername = await resolveBotUsername(token, context.env.TELEGRAM_BOT_USERNAME);
      const miniApp = context.env.TELEGRAM_MINI_APP_SHORT_NAME;

      const createResult = await createArenaIfNoneActive(kv, {
        chatId,
        messageId: 0,
        openerName,
      });

      if ("existing" in createResult) {
        const { lockedText, restore } = existingArenaLockedNotice(
          createResult.existing,
          botUsername,
          miniApp,
        );
        return respondTemporaryNotice(context, token, kv, chatId, lockedText, restore);
      }

      const arena = createResult.created;

      const sent = await replacePersistentBotMessage(
        token,
        kv,
        chatId,
        arenaWaitingText(openerName, null, arena.id),
        buildGroupArenaKeyboard(botUsername, arena.id, "Войти на арену (ПК)", miniApp),
      );

      await updateArenaMessageId(kv, arena.id, sent.message_id);
      return jsonResponse({ ok: true });
    }

    if (command === "/start_tpd_bot") {
      if (!isGroupChat(message.chat)) {
        await sendPersistentBotMessage(
          token,
          kv,
          chatId,
          "Арена против бота доступна в групповом чате. Добавьте бота в группу и нажмите кнопку там.",
        );
        return jsonResponse({ ok: true });
      }

      if (!kv) {
        await sendPersistentBotMessage(token, kv, chatId, "Арена временно недоступна (KV не настроен).");
        return jsonResponse({ ok: true });
      }

      if (!actor?.id) {
        await sendPersistentBotMessage(token, kv, chatId, "Не удалось определить игрока.");
        return jsonResponse({ ok: true });
      }

      const hostName = displayName(actor);
      const botUsername = await resolveBotUsername(token, context.env.TELEGRAM_BOT_USERNAME);
      const miniApp = context.env.TELEGRAM_MINI_APP_SHORT_NAME;

      const createResult = await createArenaPveIfNoneActive(kv, {
        chatId,
        messageId: 0,
        openerName: hostName,
        host: { id: actor.id, displayName: hostName },
      });

      if ("existing" in createResult) {
        const { lockedText, restore } = existingArenaLockedNotice(
          createResult.existing,
          botUsername,
          miniApp,
        );
        return respondTemporaryNotice(context, token, kv, chatId, lockedText, restore);
      }

      const arena = createResult.created;
      const sent = await replacePersistentBotMessage(
        token,
        kv,
        chatId,
        arenaPveOpenText(hostName, arena.battle.leftHp, arena.battle.rightHp, arena.id),
        buildGroupArenaKeyboard(botUsername, arena.id, "Начать бой с ботом (ПК)", miniApp),
      );

      await updateArenaMessageId(kv, arena.id, sent.message_id);
      return jsonResponse({ ok: true });
    }

    if (command === "/battle" || text.startsWith("{")) {
      if (isGroupChat(message.chat)) {
        await sendPersistentBotMessage(
          token,
          kv,
          chatId,
          "В группе используйте «Арена против игрока» или «Арена против бота». /battle — для личных сообщений с ботом.",
          buildBotReplyKeyboard(),
        );
        return jsonResponse({ ok: true });
      }

      const battle = extractJsonFromMessage(text);
      const ownerUserId = message.from?.id ?? chatId;
      const sent = await sendPersistentBotMessage(
        token,
        kv,
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
    await sendPersistentBotMessage(token, kv, chatId, messageText);
    return jsonResponse({ ok: true });
  }

  return jsonResponse({ ok: true });
};
