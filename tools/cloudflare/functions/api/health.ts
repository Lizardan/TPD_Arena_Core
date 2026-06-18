import type { Env } from "../lib/env";
import { jsonResponse } from "../lib/env";
import { resolveBotUsername } from "../lib/telegram";

interface BotMe {
  id: number;
  username?: string;
  first_name?: string;
  has_main_web_app?: boolean;
}

async function getBotMe(token: string): Promise<BotMe | null> {
  try {
    const response = await fetch(`https://api.telegram.org/bot${token}/getMe`);
    const payload = (await response.json()) as { ok: boolean; result?: BotMe };
    return payload.ok ? payload.result ?? null : null;
  } catch {
    return null;
  }
}

export const onRequestGet: PagesFunction<Env> = async (context) => {
  const token = context.env.TELEGRAM_BOT_TOKEN?.trim();
  const body: Record<string, unknown> = { ok: true };

  if (token) {
    const me = await getBotMe(token);
    if (me) {
      body.telegramBot = {
        id: me.id,
        username: me.username ? `@${me.username}` : null,
        hasMainMiniApp: me.has_main_web_app === true,
      };
      body.expectedMiniAppBot = await resolveBotUsername(token, context.env.TELEGRAM_BOT_USERNAME);
    } else {
      body.telegramBot = { error: "getMe failed — TELEGRAM_BOT_TOKEN invalid on Pages" };
    }
  } else {
    body.telegramBot = { error: "TELEGRAM_BOT_TOKEN not set on Pages" };
  }

  return jsonResponse(body);
};
