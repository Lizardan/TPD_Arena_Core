const CHAT_LAST_BOT_MESSAGE_PREFIX = "chat-last-bot-message:";
const CHAT_LAST_BOT_MESSAGE_TTL_SEC = 30 * 24 * 60 * 60;

function chatLastBotMessageKey(chatId: number): string {
  return `${CHAT_LAST_BOT_MESSAGE_PREFIX}${chatId}`;
}

export async function getLastBotMessageId(
  kv: KVNamespace,
  chatId: number,
): Promise<number | null> {
  const raw = await kv.get(chatLastBotMessageKey(chatId));
  if (!raw) return null;
  const parsed = Number.parseInt(raw.trim(), 10);
  if (!Number.isInteger(parsed) || parsed <= 0) {
    return null;
  }
  return parsed;
}

export async function setLastBotMessageId(
  kv: KVNamespace,
  chatId: number,
  messageId: number,
): Promise<void> {
  await kv.put(chatLastBotMessageKey(chatId), String(messageId), {
    expirationTtl: CHAT_LAST_BOT_MESSAGE_TTL_SEC,
  });
}

export async function clearLastBotMessageId(
  kv: KVNamespace,
  chatId: number,
): Promise<void> {
  await kv.delete(chatLastBotMessageKey(chatId));
}
