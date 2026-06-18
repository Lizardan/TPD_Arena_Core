/**
 * Verifies bot Mini App setup and sets the default menu button URL.
 * Group arena buttons use t.me deep links — Main Mini App must be enabled in BotFather.
 */
const token = process.env.TELEGRAM_BOT_TOKEN;
const webAppUrl = (process.env.WEB_APP_URL || "https://tpd-arena.pages.dev").replace(/\/$/, "");

if (!token) {
  console.error("TELEGRAM_BOT_TOKEN is required.");
  process.exit(1);
}

async function callTelegram(method, body) {
  const response = await fetch(`https://api.telegram.org/bot${token}/${method}`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  const payload = await response.json();
  if (!payload.ok) {
    throw new Error(payload.description || `${method} failed`);
  }
  return payload.result;
}

const me = await callTelegram("getMe", {});
const username = me.username;
if (!username) {
  console.error("::error::Bot has no @username. Set it in @BotFather.");
  process.exit(1);
}

console.log(`Bot: @${username} (id ${me.id})`);
console.log(`Main Mini App enabled: ${me.has_main_web_app === true ? "yes" : "NO"}`);

if (me.has_main_web_app !== true) {
  console.warn("");
  console.warn("::warning::Main Mini App is NOT enabled — group button «Войти на арену» will show Bot_Invalid.");
  console.warn("Fix in @BotFather:");
  console.warn("  /mybots → your bot → Bot Settings → Configure Mini App → Enable Mini App");
  console.warn(`  URL: ${webAppUrl}/`);
  console.warn("  (Also: Bot Settings → Menu Button → Configure → same URL)");
  console.warn("");
}

await callTelegram("setChatMenuButton", {
  menu_button: {
    type: "web_app",
    text: "TPD Arena",
    web_app: { url: `${webAppUrl}/` },
  },
});

console.log(`Menu button → ${webAppUrl}/`);
