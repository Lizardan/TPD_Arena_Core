import { simulateBattle } from "./battle-sim.js";
import { recordBattleVideo } from "./battle-renderer.js";

const statusEl = document.getElementById("status");
const progressEl = document.getElementById("progress");
const canvas = document.getElementById("battle-canvas");
const startButton = document.getElementById("start-btn");

const params = new URLSearchParams(window.location.search);
const sessionId = params.get("session");

const tg = window.Telegram?.WebApp;
if (tg) {
  tg.ready();
  tg.expand();
}

function setStatus(text) {
  statusEl.textContent = text;
}

function setProgress(current, total) {
  const pct = total > 0 ? Math.round((current / total) * 100) : 0;
  progressEl.textContent = `${pct}%`;
}

async function fetchSession() {
  if (!sessionId) {
    throw new Error("В ссылке нет параметра session.");
  }

  const response = await fetch(`/api/sessions/${encodeURIComponent(sessionId)}`);
  if (!response.ok) {
    const body = await response.json().catch(() => ({}));
    throw new Error(body.detail || "Сессия не найдена.");
  }
  return response.json();
}

async function uploadVideo(blob) {
  const formData = new FormData();
  formData.append("file", blob, `battle-${sessionId}.mp4`);

  const response = await fetch(`/api/sessions/${encodeURIComponent(sessionId)}/upload`, {
    method: "POST",
    body: formData,
  });

  if (!response.ok) {
    const body = await response.json().catch(() => ({}));
    throw new Error(body.detail || "Не удалось загрузить видео.");
  }
}

async function run() {
  startButton.disabled = true;

  try {
    setStatus("Загрузка боя...");
    const session = await fetchSession();
    const { leftHp, rightHp } = session.battle;

    setStatus("Симуляция...");
    const battle = simulateBattle(leftHp, rightHp);

    setStatus("Рендер на устройстве...");
    const blob = await recordBattleVideo(canvas, battle, setProgress);

    setStatus("Загрузка видео...");
    await uploadVideo(blob);

    setStatus("Готово! Возвращаемся в чат...");
    if (tg) {
      tg.close();
    } else {
      setStatus("Видео загружено. Можно закрыть вкладку.");
    }
  } catch (error) {
    console.error(error);
    setStatus(error.message || "Ошибка рендера.");
    startButton.disabled = false;
  }
}

if (!sessionId) {
  setStatus("Откройте страницу через кнопку в боте.");
} else if (tg) {
  run();
} else {
  startButton.addEventListener("click", run);
}
