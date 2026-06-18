# Telegram battle bot

Python bot that accepts battle JSON, triggers the `render-battle.yml` GitHub Actions workflow, waits for the MP4 artifact, and sends it back to Telegram.

## Setup

1. Create a Telegram bot via [@BotFather](https://t.me/BotFather) and copy the token.
2. Create a GitHub personal access token with:
   - `actions:read`
   - `actions:write` (for `workflow_dispatch`)
   - `contents:read`
3. Ensure `build-linux-renderer.yml` has produced a `battle-renderer` artifact on `main`.
4. Copy `.env.example` to `.env` and fill in values.

```bash
cd tools/telegram-bot
python -m venv .venv
source .venv/bin/activate  # Windows: .venv\Scripts\activate
pip install -r requirements.txt
python bot.py
```

## Usage

```
/battle {"leftHp":80,"rightHp":100}
```

The bot validates JSON, dispatches CI, polls until the workflow completes, downloads `battle-video`, and replies with the MP4.

## Hosting

Run on Railway, Fly.io, Render, or any small always-on host. GitHub Actions only renders the video; the bot process must stay online to receive Telegram updates.

## Limits

- Telegram upload limit: 50 MB per video
- GitHub Actions queue time + render time: typically 1–4 minutes
- `GITHUB_REPO` format: `owner/repo`
