/**
 * Self-test for Telegram initData HMAC (matches functions/lib/telegram-init.ts).
 * Run: node scripts/verify-init-data-selftest.mjs
 */
import crypto from "node:crypto";

function buildSecretKey(botToken) {
  return crypto.createHmac("sha256", "WebAppData").update(botToken).digest();
}

function signInitData(initData, botToken) {
  const params = new URLSearchParams(initData);
  params.delete("hash");
  const dataCheckString = [...params.entries()]
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([k, v]) => `${k}=${v}`)
    .join("\n");
  const secretKey = buildSecretKey(botToken);
  const hash = crypto.createHmac("sha256", secretKey).update(dataCheckString).digest("hex");
  params.set("hash", hash);
  return params.toString();
}

function verify(initData, botToken, { dropSignature } = {}) {
  const params = new URLSearchParams(initData);
  const hash = params.get("hash");
  params.delete("hash");
  if (dropSignature) params.delete("signature");
  const dataCheckString = [...params.entries()]
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([k, v]) => `${k}=${v}`)
    .join("\n");
  const secretKey = buildSecretKey(botToken);
  const calculated = crypto.createHmac("sha256", secretKey).update(dataCheckString).digest("hex");
  return calculated === hash;
}

const token = "123456789:AAFakeTokenForSelfTestOnly";
const base = new URLSearchParams({
  auth_date: String(Math.floor(Date.now() / 1000)),
  user: JSON.stringify({ id: 42, first_name: "Test" }),
  start_param: "arena123",
  signature: "fake-signature-field-should-be-included",
});
const signed = signInitData(base.toString(), token);

const withSignature = verify(signed, token, { dropSignature: false });
const withoutSignature = verify(signed, token, { dropSignature: true });

console.log("verify with signature in check-string:", withSignature);
console.log("verify if signature removed (old bug):", withoutSignature);

if (!withSignature || withoutSignature) {
  console.error("Self-test failed");
  process.exit(1);
}
console.log("Self-test OK");
