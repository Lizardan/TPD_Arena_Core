import json
import logging
import os
import re
import tempfile
import time
import zipfile
from pathlib import Path

import requests
from dotenv import load_dotenv
from telegram import Update
from telegram.ext import Application, CommandHandler, ContextTypes, MessageHandler, filters

load_dotenv()

logging.basicConfig(
    format="%(asctime)s %(levelname)s %(name)s %(message)s",
    level=logging.INFO,
)
logger = logging.getLogger("battle-bot")

GITHUB_API = "https://api.github.com"
WORKFLOW_FILE = "render-battle.yml"
ARTIFACT_NAME = "battle-video"
MIN_HP = 1
MAX_HP = 999


class GitHubRenderClient:
    def __init__(self, token: str, repo: str, ref: str):
        self.repo = repo
        self.ref = ref
        self.session = requests.Session()
        self.session.headers.update(
            {
                "Authorization": f"Bearer {token}",
                "Accept": "application/vnd.github+json",
                "X-GitHub-Api-Version": "2022-11-28",
            }
        )

    def dispatch_render(self, battle_json: str) -> None:
        url = f"{GITHUB_API}/repos/{self.repo}/actions/workflows/{WORKFLOW_FILE}/dispatches"
        response = self.session.post(
            url,
            json={"ref": self.ref, "inputs": {"battle_json": battle_json}},
            timeout=30,
        )
        if response.status_code != 204:
            raise RuntimeError(
                f"workflow_dispatch failed ({response.status_code}): {response.text}"
            )

    def wait_for_latest_run(
        self,
        started_after: float,
        poll_interval: int,
        timeout_seconds: int,
    ) -> dict:
        deadline = time.time() + timeout_seconds

        while time.time() < deadline:
            run = self._find_matching_run(started_after)
            if run is not None:
                status = run.get("status")
                conclusion = run.get("conclusion")
                if status == "completed":
                    if conclusion == "success":
                        return run
                    raise RuntimeError(
                        f"Render workflow failed: {conclusion} ({run.get('html_url')})"
                    )

            time.sleep(poll_interval)

        raise TimeoutError("Timed out waiting for battle render workflow.")

    def _find_matching_run(self, started_after: float) -> dict | None:
        url = f"{GITHUB_API}/repos/{self.repo}/actions/runs"
        response = self.session.get(
            url,
            params={"event": "workflow_dispatch", "per_page": 20},
            timeout=30,
        )
        response.raise_for_status()

        for run in response.json().get("workflow_runs", []):
            if run.get("name") != "Render Battle Video":
                continue

            created_at = run.get("created_at")
            if not created_at:
                continue

            created_ts = _parse_github_timestamp(created_at)
            if created_ts + 2 < started_after:
                continue

            return run

        return None

    def download_battle_video(self, run_id: int, destination: Path) -> Path:
        artifacts = self._list_artifacts(run_id)
        artifact = next((a for a in artifacts if a.get("name") == ARTIFACT_NAME), None)
        if artifact is None:
            raise RuntimeError(f"Artifact '{ARTIFACT_NAME}' was not found for run {run_id}.")

        archive_url = artifact["archive_download_url"]
        response = self.session.get(archive_url, timeout=120)
        response.raise_for_status()

        zip_path = destination / "battle-video.zip"
        zip_path.write_bytes(response.content)

        with zipfile.ZipFile(zip_path, "r") as archive:
            members = [name for name in archive.namelist() if name.endswith(".mp4")]
            if not members:
                raise RuntimeError("battle-video artifact does not contain an MP4 file.")
            archive.extract(members[0], destination)
            return destination / members[0]

    def _list_artifacts(self, run_id: int) -> list[dict]:
        url = f"{GITHUB_API}/repos/{self.repo}/actions/runs/{run_id}/artifacts"
        response = self.session.get(url, timeout=30)
        response.raise_for_status()
        return response.json().get("artifacts", [])


def _parse_github_timestamp(value: str) -> float:
    # Example: 2026-06-17T12:34:56Z
    return time.mktime(time.strptime(value, "%Y-%m-%dT%H:%M:%SZ"))


def validate_battle_json(raw: str) -> str:
    try:
        payload = json.loads(raw)
    except json.JSONDecodeError as exc:
        raise ValueError(f"Invalid JSON: {exc}") from exc

    if not isinstance(payload, dict):
        raise ValueError("Battle JSON must be an object.")

    left_hp = payload.get("leftHp")
    right_hp = payload.get("rightHp")

    if not isinstance(left_hp, int) or not isinstance(right_hp, int):
        raise ValueError("leftHp and rightHp must be integers.")

    if not (MIN_HP <= left_hp <= MAX_HP and MIN_HP <= right_hp <= MAX_HP):
        raise ValueError(f"HP must be between {MIN_HP} and {MAX_HP}.")

    return json.dumps(payload, separators=(",", ":"))


def extract_json_from_message(text: str) -> str:
    text = text.strip()
    if not text:
        raise ValueError("Send JSON after /battle, e.g. /battle {\"leftHp\":80,\"rightHp\":100}")

    # Support "/battle {...}" and plain JSON messages after command
    if text.startswith("/battle"):
        text = text[len("/battle") :].strip()

    match = re.search(r"\{.*\}", text, re.DOTALL)
    if not match:
        raise ValueError("Could not find JSON object in message.")

    return validate_battle_json(match.group(0))


def build_github_client() -> GitHubRenderClient:
    token = os.environ.get("GITHUB_TOKEN")
    repo = os.environ.get("GITHUB_REPO")
    ref = os.environ.get("GITHUB_REF", "main")

    if not token or not repo:
        raise RuntimeError("GITHUB_TOKEN and GITHUB_REPO must be set.")

    return GitHubRenderClient(token=token, repo=repo, ref=ref)


async def start(update: Update, context: ContextTypes.DEFAULT_TYPE) -> None:
    await update.message.reply_text(
        "Send /battle followed by JSON.\n"
        'Example:\n/battle {"leftHp":80,"rightHp":100}'
    )


async def battle(update: Update, context: ContextTypes.DEFAULT_TYPE) -> None:
    message = update.effective_message
    if message is None:
        return

    try:
        battle_json = extract_json_from_message(message.text or "")
    except ValueError as exc:
        await message.reply_text(str(exc))
        return

    poll_interval = int(os.environ.get("POLL_INTERVAL_SECONDS", "15"))
    timeout_seconds = int(os.environ.get("RENDER_TIMEOUT_SECONDS", "900"))

    await message.reply_text("Rendering battle video in GitHub Actions...")

    try:
        github = build_github_client()
        started_at = time.time()
        github.dispatch_render(battle_json)
        run = github.wait_for_latest_run(started_at, poll_interval, timeout_seconds)

        with tempfile.TemporaryDirectory() as tmp_dir:
            video_path = github.download_battle_video(run["id"], Path(tmp_dir))
            await message.reply_video(
                video=video_path.open("rb"),
                caption=f"Battle rendered (run #{run['run_number']})",
                supports_streaming=True,
            )
    except Exception as exc:
        logger.exception("Battle render failed")
        await message.reply_text(f"Render failed: {exc}")


def main() -> None:
    token = os.environ.get("TELEGRAM_BOT_TOKEN")
    if not token:
        raise RuntimeError("TELEGRAM_BOT_TOKEN must be set.")

    application = Application.builder().token(token).build()
    application.add_handler(CommandHandler("start", start))
    application.add_handler(CommandHandler("battle", battle))
    application.add_handler(MessageHandler(filters.TEXT & ~filters.COMMAND, battle))

    logger.info("Battle Telegram bot started.")
    application.run_polling(allowed_updates=Update.ALL_TYPES)


if __name__ == "__main__":
    main()
