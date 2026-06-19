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

const response = await fetch(`https://api.telegram.org/bot${token}/setWebhook`, {
  method: "POST",
  headers: { "Content-Type": "application/json" },
  body: JSON.stringify({
    url: webhookUrl,
    secret_token: webhookSecret,
  }),
});
const payload = await response.json();

if (!payload.ok) {
  console.error("setWebhook failed:", payload.description || payload);
  process.exit(1);
}

console.log(`Webhook set to ${webhookUrl} (secret: ${webhookSecret.slice(0, 8)}...)`);
