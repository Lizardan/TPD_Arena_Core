const token = process.env.TELEGRAM_BOT_TOKEN;
const webAppUrl = (process.env.WEB_APP_URL || "https://tpd-arena.pages.dev").replace(/\/$/, "");

if (!token) {
  console.error("TELEGRAM_BOT_TOKEN is required.");
  process.exit(1);
}

const webhookUrl = `${webAppUrl}/api/telegram-webhook`;
const response = await fetch(
  `https://api.telegram.org/bot${token}/setWebhook?url=${encodeURIComponent(webhookUrl)}`,
);
const payload = await response.json();

if (!payload.ok) {
  console.error("setWebhook failed:", payload.description || payload);
  process.exit(1);
}

console.log(`Webhook set to ${webhookUrl}`);
