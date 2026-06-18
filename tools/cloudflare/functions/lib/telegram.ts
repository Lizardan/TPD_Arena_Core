interface TelegramResponse<T> {
  ok: boolean;
  result?: T;
  description?: string;
}

async function callTelegram<T>(
  token: string,
  method: string,
  body?: Record<string, unknown> | FormData,
): Promise<T> {
  const init: RequestInit = { method: "POST" };
  if (body instanceof FormData) {
    init.body = body;
  } else if (body) {
    init.headers = { "Content-Type": "application/json" };
    init.body = JSON.stringify(body);
  }

  const response = await fetch(`https://api.telegram.org/bot${token}/${method}`, init);
  const payload = (await response.json()) as TelegramResponse<T>;
  if (!payload.ok) {
    throw new Error(payload.description || `Telegram ${method} failed`);
  }
  return payload.result as T;
}

export async function sendMessage(
  token: string,
  chatId: number,
  text: string,
  replyMarkup?: Record<string, unknown>,
): Promise<void> {
  await callTelegram(token, "sendMessage", {
    chat_id: chatId,
    text,
    reply_markup: replyMarkup,
  });
}

export async function sendAnimation(
  token: string,
  chatId: number,
  animation: Blob,
  caption: string,
  filename: string,
): Promise<void> {
  const form = new FormData();
  form.append("chat_id", String(chatId));
  form.append("caption", caption);
  form.append("animation", animation, filename);
  await callTelegram(token, "sendAnimation", form);
}

export async function sendVideo(
  token: string,
  chatId: number,
  video: Blob,
  caption: string,
  filename: string,
): Promise<void> {
  const form = new FormData();
  form.append("chat_id", String(chatId));
  form.append("caption", caption);
  form.append("supports_streaming", "true");
  form.append("video", video, filename);
  await callTelegram(token, "sendVideo", form);
}

export function buildWebAppKeyboard(webAppUrl: string) {
  return {
    inline_keyboard: [
      [
        {
          text: "Render battle video",
          web_app: { url: webAppUrl },
        },
      ],
    ],
  };
}
