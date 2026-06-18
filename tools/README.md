# TPD Arena — Telegram bot tools

Battle video via **Telegram Mini App** on **Cloudflare Pages**.

## Quick start

See **[CLOUDFLARE.md](CLOUDFLARE.md)** — 4 steps: Cloudflare token → GitHub secrets → push → test `/battle`.

Live URL: `https://tpd-arena.pages.dev`

## How it works

**Group arena (main flow):**

```
/arena in group → waiting for 2 fighters → realtime battle in Mini App
First entrant encodes MP4 in parallel → video posted to group chat
```

**Solo (DM):**

```
/battle JSON → Web App → render → video in chat
```

## Project layout

| Path | Role |
|------|------|
| [cloudflare/](cloudflare/) | Pages deploy, API, webhook |
| [web-app/](web-app/) | Mini App source (canvas renderer) |
| [sample-battle-request.json](sample-battle-request.json) | Example JSON |

## Unity local export (optional)

For MP4 export in Editor without Telegram:

- Menu **TPD Arena → Export Battle From Sample JSON (FFmpeg)**
- CLI: `BattleExportCli.Run` with `-exportJson` / `-output`

## Battle JSON

```json
{
  "leftHp": 80,
  "rightHp": 100
}
```

Optional: `leftAbilities`, `rightAbilities` (string arrays).
