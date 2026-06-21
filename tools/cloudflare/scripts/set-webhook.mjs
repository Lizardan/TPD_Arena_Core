import crypto from "node:crypto";

const token = process.env.TELEGRAM_BOT_TOKEN?.trim().replace(/\r/g, "");
const webAppUrl = (process.env.WEB_APP_URL || "https://tpd-arena.pages.dev").replace(/\/$/, "");

if (!token) {
  console.error("TELEGRAM_BOT_TOKEN is required.");
  process.exit(1);
}

const webhookUrl = `${webAppUrl}/api/telegram-webhook`;
const webhookSecret = crypto
  .createHash("sha256")
  .update(`${token.trim()}:tpd-arena-webhook`)
  .digest("hex");

async function callTelegram(method, body) {
  const response = await fetch(`https://api.telegram.org/bot${token}/${method}`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  const payload = await response.json();
  if (!payload.ok) {
    console.error(`${method} failed:`, payload.description || payload);
    process.exit(1);
  }
  return payload.result;
}

await callTelegram("setWebhook", {
  url: webhookUrl,
  secret_token: webhookSecret,
  allowed_updates: ["message", "callback_query", "my_chat_member"],
});

await callTelegram("setMyCommands", {
  commands: [
    {
      command: "start_tpd_arena",
      description: "Открыть арену в групповом чате",
    },
    {
      command: "start_tpd_bot",
      description: "Арена против бота в группе",
    },
    {
      command: "stop_tpd_arena",
      description: "Остановить текущую арену",
    },
    {
      command: "battle",
      description: "Запустить бой в личке с JSON",
    },
    {
      command: "start",
      description: "Показать подсказку",
    },
  ],
});

console.log(`Webhook set to ${webhookUrl} (secret: ${webhookSecret.slice(0, 8)}...)`);
console.log("Bot slash commands updated.");
