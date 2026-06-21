const UPDATE_SEEN_PREFIX = "tg-update:";
const UPDATE_SEEN_TTL_SEC = 24 * 60 * 60;

function updateSeenKey(updateId: number): string {
  return `${UPDATE_SEEN_PREFIX}${updateId}`;
}

/** Returns true if this Telegram update_id was already handled (retry duplicate). */
export async function isDuplicateTelegramUpdate(
  kv: KVNamespace | undefined,
  updateId: number | undefined,
): Promise<boolean> {
  if (!kv || updateId == null || !Number.isInteger(updateId)) {
    return false;
  }

  const key = updateSeenKey(updateId);
  const seen = await kv.get(key);
  if (seen) {
    return true;
  }

  await kv.put(key, "1", { expirationTtl: UPDATE_SEEN_TTL_SEC });
  return false;
}
