import type { BattlePayload } from "./validation";

const SESSION_TTL_MS = 60 * 60 * 1000;

export interface SessionData {
  chatId: number;
  userId: number;
  battle: BattlePayload;
  promptMessageId?: number;
  exp: number;
}

function timingSafeEqual(a: string, b: string): boolean {
  if (a.length !== b.length) return false;
  let result = 0;
  for (let i = 0; i < a.length; i++) {
    result |= a.charCodeAt(i) ^ b.charCodeAt(i);
  }
  return result === 0;
}

function base64UrlEncode(bytes: Uint8Array): string {
  let binary = "";
  for (const byte of bytes) {
    binary += String.fromCharCode(byte);
  }
  return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/g, "");
}

function base64UrlDecode(value: string): Uint8Array {
  const padded = value.replace(/-/g, "+").replace(/_/g, "/");
  const padLength = (4 - (padded.length % 4)) % 4;
  const base64 = padded + "=".repeat(padLength);
  const binary = atob(base64);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) {
    bytes[i] = binary.charCodeAt(i);
  }
  return bytes;
}

async function hmacSign(message: string, secret: string): Promise<string> {
  const key = await crypto.subtle.importKey(
    "raw",
    new TextEncoder().encode(secret),
    { name: "HMAC", hash: "SHA-256" },
    false,
    ["sign"],
  );
  const signature = await crypto.subtle.sign(
    "HMAC",
    key,
    new TextEncoder().encode(message),
  );
  return base64UrlEncode(new Uint8Array(signature));
}

export async function createSessionId(
  chatId: number,
  battle: BattlePayload,
  secret: string,
  userId: number,
  promptMessageId?: number,
): Promise<string> {
  const payload: SessionData = {
    chatId,
    userId,
    battle,
    ...(typeof promptMessageId === "number" ? { promptMessageId } : {}),
    exp: Date.now() + SESSION_TTL_MS,
  };
  const data = base64UrlEncode(new TextEncoder().encode(JSON.stringify(payload)));
  const signature = await hmacSign(data, secret);
  return `${data}.${signature}`;
}

export async function verifySessionId(
  sessionId: string,
  secret: string,
): Promise<SessionData | null> {
  const dot = sessionId.lastIndexOf(".");
  if (dot <= 0) return null;

  const data = sessionId.slice(0, dot);
  const signature = sessionId.slice(dot + 1);
  const expected = await hmacSign(data, secret);
  if (!timingSafeEqual(signature, expected)) return null;

  try {
    const json = new TextDecoder().decode(base64UrlDecode(data));
    const payload = JSON.parse(json) as SessionData;
    if (
      !payload ||
      typeof payload.chatId !== "number" ||
      typeof payload.userId !== "number" ||
      !payload.battle
    ) {
      return null;
    }
    if (payload.promptMessageId != null && typeof payload.promptMessageId !== "number") {
      return null;
    }
    if (payload.exp < Date.now()) return null;
    return payload;
  } catch {
    return null;
  }
}
