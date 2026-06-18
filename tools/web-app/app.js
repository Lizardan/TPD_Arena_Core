import { simulateBattle } from "./battle-sim.js";
import { playAndEncodeBattle, recordBattleVideo } from "./battle-renderer.js";

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

const arenaId = params.get("arena") || tg?.initDataUnsafe?.start_param || null;

function setStatus(text) {
  statusEl.textContent = text;
}

function setProgress(current, total) {
  const pct = total > 0 ? Math.round((current / total) * 100) : 0;
  progressEl.textContent = `${pct}%`;
}

function initDataHeader() {
  const initData = tg?.initData;
  return initData ? { "X-Telegram-Init-Data": initData } : {};
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

async function fetchArena() {
  const response = await fetch(`/api/arenas/${encodeURIComponent(arenaId)}`, {
    headers: initDataHeader(),
  });
  if (!response.ok) {
    const body = await response.json().catch(() => ({}));
    throw new Error(body.detail || "Арена не найдена.");
  }
  return response.json();
}

async function joinArena() {
  const response = await fetch(`/api/arenas/${encodeURIComponent(arenaId)}/join`, {
    method: "POST",
    headers: { "Content-Type": "application/json", ...initDataHeader() },
    body: JSON.stringify({ initData: tg?.initData || "" }),
  });
  if (!response.ok) {
    const body = await response.json().catch(() => ({}));
    throw new Error(body.detail || "Не удалось войти на арену.");
  }
  return response.json();
}

async function uploadSessionVideo(blob) {
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

async function uploadArenaVideo(blob) {
  const formData = new FormData();
  formData.append("file", blob, `arena-${arenaId}.mp4`);

  const response = await fetch(`/api/arenas/${encodeURIComponent(arenaId)}/upload`, {
    method: "POST",
    headers: initDataHeader(),
    body: formData,
  });

  if (!response.ok) {
    const body = await response.json().catch(() => ({}));
    throw new Error(body.detail || "Не удалось загрузить видео.");
  }
}

function roleStatusText(arena) {
  if (arena.status === "waiting") {
    if (arena.role === "player1") return "Вы — левый боец. Ждём правого бойца…";
    if (arena.role === "spectator") return "Вы в зале ожидания. Ждём двух бойцов…";
    return "Ждём двух бойцов на арене…";
  }
  if (arena.role === "host") return "Вы — хост. Бой скоро начнётся…";
  if (arena.role === "player1" || arena.role === "player2") return "Бой скоро начнётся…";
  return "Вы — зритель. Бой скоро начнётся…";
}

async function waitForBattleStart() {
  let arena = await joinArena();
  setStatus(roleStatusText(arena));

  while (arena.status === "waiting") {
    await sleep(1000);
    arena = await fetchArena();
    setStatus(roleStatusText(arena));
  }

  while (arena.status === "fighting" && !arena.battleStartAt) {
    await sleep(500);
    arena = await fetchArena();
  }

  return arena;
}

async function runArena() {
  startButton.disabled = true;

  try {
    if (!tg?.initData) {
      throw new Error("Откройте арену через кнопку в Telegram.");
    }

    setStatus("Вход на арену...");
    const arena = await waitForBattleStart();

    if (arena.status !== "fighting") {
      throw new Error("Бой не начался.");
    }

    const leftName = arena.player1?.displayName || "Игрок 1";
    const rightName = arena.player2?.displayName || "Игрок 2";
    const { leftHp, rightHp } = arena.battle;

    setStatus("Симуляция...");
    const battle = simulateBattle(leftHp, rightHp);

    setStatus(arena.isHost ? "Бой! (вы записываете видео)" : "Бой!");

    const blob = await playAndEncodeBattle(canvas, battle, {
      battleStartAt: arena.battleStartAt,
      isHost: arena.isHost,
      labels: { left: leftName, right: rightName },
      onProgress: setProgress,
      onStatus: setStatus,
    });

    if (arena.isHost && blob) {
      setStatus("Отправка видео в чат...");
      await uploadArenaVideo(blob);
    } else {
      setStatus("Бой завершён. Видео скоро появится в чате.");
      await sleep(1500);
    }

    if (tg) {
      tg.close();
    }
  } catch (error) {
    console.error(error);
    setStatus(error.message || "Ошибка арены.");
    startButton.disabled = false;
  }
}

async function runSession() {
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
    await uploadSessionVideo(blob);

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

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

if (arenaId) {
  if (tg) {
    runArena();
  } else {
    setStatus("Откройте арену через кнопку в Telegram.");
    startButton.addEventListener("click", runArena);
  }
} else if (sessionId) {
  if (tg) {
    runSession();
  } else {
    startButton.addEventListener("click", runSession);
  }
} else {
  setStatus("Откройте страницу через кнопку в боте.");
}
