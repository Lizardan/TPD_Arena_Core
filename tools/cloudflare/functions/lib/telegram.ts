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
): Promise<{ message_id: number }> {
  return callTelegram(token, "sendMessage", {
    chat_id: chatId,
    text,
    reply_markup: replyMarkup,
  });
}

export async function editMessageText(
  token: string,
  chatId: number,
  messageId: number,
  text: string,
  replyMarkup?: Record<string, unknown>,
): Promise<void> {
  await callTelegram(token, "editMessageText", {
    chat_id: chatId,
    message_id: messageId,
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

export function buildWebAppKeyboard(webAppUrl: string, buttonText = "Сгенерировать видео") {
  return {
    inline_keyboard: [
      [
        {
          text: buttonText,
          web_app: { url: webAppUrl },
        },
      ],
    ],
  };
}

export function buildArenaWebAppUrl(baseUrl: string, arenaId: string): string {
  return `${baseUrl.replace(/\/$/, "")}/?arena=${encodeURIComponent(arenaId)}`;
}

export function arenaWaitingText(openerName: string, player1: { displayName: string } | null): string {
  if (!player1) {
    return (
      `${openerName} открыл арену.\n\n` +
      `Ждём двух бойцов. Первые два вошедших в Mini App сразятся на арене, остальные — зрители.`
    );
  }
  return (
    `${openerName} открыл арену.\n\n` +
    `Левый боец: ${player1.displayName}\n` +
    `Ждём правого бойца…`
  );
}

export function arenaFightingText(player1Name: string, player2Name: string): string {
  return `Сражаются: ${player1Name} против ${player2Name}`;
}

export function arenaAnimationCaption(
  player1Name: string,
  player2Name: string,
  leftHp: number,
  rightHp: number,
): string {
  return `Бой: ${player1Name} (${leftHp} HP) против ${player2Name} (${rightHp} HP)`;
}
