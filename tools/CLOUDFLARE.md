# TPD Arena — Cloudflare (web-app + bot)

Everything runs on **Cloudflare Pages** at `https://tpd-arena.pages.dev`:

- `/` — Telegram Mini App shell → **Unity WebGL** at `/game/`
- `/game/` — Unity WebGL build (from CI)
- `/api/*` — bot webhook, arenas, sessions, video upload

## Your setup checklist (one time)

### 1. Cloudflare account

1. Sign up at [dash.cloudflare.com](https://dash.cloudflare.com) (free)
2. **My Profile → API Tokens → Create Token**
3. Use template **Edit Cloudflare Workers** (must include **Workers KV Storage → Edit**)
4. Copy **API Token** and **Account ID** (dashboard home, right sidebar)

### 2. GitHub Secrets

Repo → **Settings → Secrets and variables → Actions → New repository secret**:

| Secret | Value |
|--------|--------|
| `CLOUDFLARE_API_TOKEN` | API token (`cfut_...` or legacy format) |
| `CLOUDFLARE_ACCOUNT_ID` | **Account ID** from dashboard sidebar (32 hex chars) — **not** Zone ID |
| `TELEGRAM_BOT_TOKEN` | from [@BotFather](https://t.me/BotFather) |
| `ARENA_KV_NAMESPACE_ID` | optional — KV namespace id if auto-create fails (see below) |
| `UNITY_LICENSE` | Unity activation `.ulf` for WebGL CI — see [UNITY_WEBGL.md](UNITY_WEBGL.md) |
| `UNITY_EMAIL` | alternative to `UNITY_LICENSE` (with `UNITY_PASSWORD`) |
| `UNITY_PASSWORD` | Unity account password for CI |

**Important for `cfut_` tokens:** Account ID is required. Find it on the Cloudflare dashboard home page, right column — labeled **Account ID** (not under a domain/zone).

### 3. KV for group arenas

CI runs `ensure-arena-kv.mjs` automatically and binds namespace `tpd-arena-arenas` to `ARENA_KV`.

If the **Ensure Arena KV namespace** step fails (token lacks KV permission):

1. [dash.cloudflare.com](https://dash.cloudflare.com) → **Workers & Pages** → **KV**
2. **Create** namespace `tpd-arena-arenas`
3. Copy the **Namespace ID**
4. GitHub → **Settings → Secrets** → add `ARENA_KV_NAMESPACE_ID` with that id
5. Re-run **Deploy Cloudflare**

Or recreate API token with **Workers KV Storage → Edit** permission.

For local deploy once:

```powershell
cd tools/cloudflare
node scripts/ensure-arena-kv.mjs
```

Commit the updated `id` in [wrangler.toml](wrangler.toml) if you deploy outside GitHub Actions.

### 4. Push to `main`

Workflow **Deploy Cloudflare** will:

- build **Unity WebGL** when game sources change (or on manual run)
- ensure Pages project `tpd-arena` exists
- ensure KV namespace `tpd-arena-arenas` exists
- store `TELEGRAM_BOT_TOKEN` as a Pages secret **before** deploy
- build and deploy web-app + API to Pages
- call `setWebhook` for your bot

### 5. Bot in group chat

1. Add bot to a Telegram group
2. In [@BotFather](https://t.me/BotFather): `/setprivacy` → **Disable**
3. In BotFather: **Bot Settings → Menu Button → Configure** → URL `https://tpd-arena.pages.dev`  
   (needed for `?startapp=` links from group buttons)
4. Optional `/setcommands`:
   ```
   arena - Открыть арену в группе
   battle - Соло-бой в личке
   start - Справка
   ```

### 6. Test group arena

1. In group: `/arena`
2. First two users tap **Войти на арену** → become fighters
3. Others tap **Смотреть бой** → spectators
4. Battle plays in real time in Mini App; first entrant encodes MP4
5. Video appears in group chat when battle ends

### 7. Test solo (DM)

```
/battle {"leftHp":80,"rightHp":100}
```

## External player stats API (optional)

Uncomment in [wrangler.toml](wrangler.toml):

```toml
STATS_API_URL = "https://friend-server.example/stats"
```

Contract:

```
GET {STATS_API_URL}?left={nick1}&right={nick2}
→ { "leftHp": 80, "rightHp": 100 }
```

Until configured, HP defaults to **100 / 100**.

## Optional

- **Custom domain**: Cloudflare Pages → tpd-arena → Custom domains (update `WEB_APP_URL` in wrangler.toml and workflow)
- **BotFather menu button**: set URL to `https://tpd-arena.pages.dev`

## Local development

```powershell
cd tools/cloudflare
npm ci
node scripts/ensure-arena-kv.mjs
npm run copy-web-app
npx wrangler pages dev public
```

Telegram Web App requires HTTPS in production; use `wrangler pages dev` for local API testing only.

## Layout

```
tools/cloudflare/
  functions/     Pages Functions (API + webhook + arenas)
  public/        static web-app (built from tools/web-app)
  scripts/       copy-web-app, set-webhook, ensure-arena-kv
  wrangler.toml
```

