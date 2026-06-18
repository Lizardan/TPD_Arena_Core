# Unity WebGL → Telegram Mini App

Mini App at `https://tpd-arena.pages.dev` loads the **Unity WebGL** build (not the JS canvas MVP).

## Flow

```
GitHub Actions (main)
  → game-ci unity-builder (WebGL)
  → copy to tools/cloudflare/public/game/
  → wrangler pages deploy

Telegram group arena:
  /arena → кнопка «Войти на арену»
  → https://tpd-arena.pages.dev/?arena=<id>
  → lobby (arena-lobby.js): ждёт 2-го игрока, poll API
  → когда оба есть: /game/index.html?battle=<base64 json>&arena=<id>
  → Unity WebGLBootstrap декодирует JSON → RunFromTelegramRequest → бой
```

No realtime sync in Unity — identical JSON → identical simulation.

## GitHub Secrets (Unity license)

Add **one** of these options in **Settings → Secrets → Actions**:

### Option A — activation file (recommended for CI)

| Secret | Value |
|--------|--------|
| `UNITY_LICENSE` | full contents of `.ulf` activation file |

How to get `.ulf` (Personal license):

1. Install [Unity Hub](https://unity.com/download) and activate **Unity 6000.5.0f1** with your license.
2. On a machine with Unity CLI, or use [game-ci/unity-activate](https://game.ci/docs/github/activation) docs to export activation.
3. Paste the entire `.ulf` file text into the `UNITY_LICENSE` secret.

### Option B — email + password

| Secret | Value |
|--------|--------|
| `UNITY_EMAIL` | Unity account email |
| `UNITY_PASSWORD` | Unity account password |

Works with Personal/Plus/Pro; Unity may prompt for 2FA — prefer Option A for CI.

## Local WebGL build

1. **File → Build Settings → WebGL**
2. Scene: `Assets/Game/Scenes/Game.unity`
3. **Build** → `build/WebGL/`

Then deploy static files:

```powershell
cd tools/cloudflare
$env:UNITY_WEBGL_SOURCE = "F:\Unity Projects\TPD_Arena_Core\build\WebGL"
npm run copy-web-app
npm run deploy
```

## CI behaviour

- **Unity sources changed** (`Assets/`, `ProjectSettings/`, `Packages/`) → full WebGL rebuild.
- **Only Cloudflare/tools changed** → reuses cached `build/WebGL` from previous run.
- **workflow_dispatch** → always rebuilds WebGL.

## WebGL limitations (current)

- **FFmpeg export** is disabled on WebGL (`#if !UNITY_WEBGL`). Battle plays in-engine; MP4 upload to Telegram will be added via JS interop / WebCodecs later.
- `WebGLBootstrap` reads `battle` (base64url JSON), `arena`, `host` from URL and auto-starts the fight.

## CI (aligned with legacy main.yml)

The working GitHub Pages workflow used:

- `game-ci/unity-builder@v5`
- `customParameters: "-ignoreAudio"` (required for headless Linux — avoids crashes)
- Default output path: `build/WebGL/WebGL/`
- Library cache key: `Library-v6-...`

Current `deploy-cloudflare.yml` uses the same Unity build settings; `copy-web-app.mjs` picks `build/WebGL/WebGL` automatically.

## CI troubleshooting

### `Build failed with exit code 139`

Unity crashed (segmentation fault) during WebGL build — often **out of memory** or **disk full** on GitHub runners.

The workflow already:
- frees disk space before build
- caches `Library/` between runs
- limits CPU (`-maxCpuCount 2`)

**What to do:**
1. Re-run the workflow — second run is faster with `Library` cache and may succeed.
2. Download artifact `unity-logs-<run_id>` from the failed job (if present).
3. Ensure `UNITY_LICENSE` is the full `.ulf` file from Unity Hub (not serial only).
4. If it keeps failing: build WebGL locally and deploy manually (see below).

### Deploy without Unity build

Even if `build-webgl` fails, **deploy still runs** — lobby and bot update, Unity shows a placeholder until WebGL is built.

## BotFather

Menu Button URL: `https://tpd-arena.pages.dev`

Group arena button: `https://t.me/TPD_Arena_bot?startapp={arenaId}`
