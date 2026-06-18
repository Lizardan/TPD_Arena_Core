# TPD Arena — Cloudflare (web-app + bot)

Everything runs on **Cloudflare Pages** at `https://tpd-arena.pages.dev`:

- `/` — Telegram Mini App (client-side render)
- `/api/*` — bot webhook, sessions, video upload

## Your setup checklist (one time)

### 1. Cloudflare account

1. Sign up at [dash.cloudflare.com](https://dash.cloudflare.com) (free)
2. **My Profile → API Tokens → Create Token**
3. Use template **Edit Cloudflare Workers**
4. Copy **API Token** and **Account ID** (dashboard home, right sidebar)

### 2. GitHub Secrets

Repo → **Settings → Secrets and variables → Actions → New repository secret**:

| Secret | Value |
|--------|--------|
| `CLOUDFLARE_API_TOKEN` | API token (`cfut_...` or legacy format) |
| `CLOUDFLARE_ACCOUNT_ID` | **Account ID** from dashboard sidebar (32 hex chars) — **not** Zone ID |
| `TELEGRAM_BOT_TOKEN` | from [@BotFather](https://t.me/BotFather) |

**Important for `cfut_` tokens:** Account ID is required. Find it on the Cloudflare dashboard home page, right column — labeled **Account ID** (not under a domain/zone).

Remove obsolete items if present: `API_BASE_URL` variable, Render secrets.

### 3. Push to `main`

Workflow **Deploy Cloudflare** will:

- create the Pages project `tpd-arena` on first run (if it does not exist yet)
- build and deploy web-app + API to Pages
- store `TELEGRAM_BOT_TOKEN` as a Pages secret
- call `setWebhook` for your bot

### 4. Test in Telegram

```
/battle {"leftHp":80,"rightHp":100}
```

Tap **Render battle video** → wait → video appears in chat.

Nothing to install on your PC.

## Optional

- **Custom domain**: Cloudflare Pages → tpd-arena → Custom domains (update `WEB_APP_URL` in [wrangler.toml](wrangler.toml) and workflow)
- **BotFather menu button**: set URL to `https://tpd-arena.pages.dev`

## Local development

```powershell
cd tools/cloudflare
npm ci
npm run copy-web-app
npx wrangler pages dev public
```

Telegram Web App requires HTTPS in production; use `wrangler pages dev` for local API testing only.

## Layout

```
tools/cloudflare/
  functions/     Pages Functions (API + webhook)
  public/        static web-app (built from tools/web-app)
  scripts/       copy-web-app, set-webhook
  wrangler.toml
```
