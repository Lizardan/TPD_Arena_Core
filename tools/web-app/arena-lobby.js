/**
 * Telegram Mini App lobby: wait for 2 players, then open Unity WebGL with battle JSON in URL.
 */

const statusEl = document.getElementById("status");
const detailEl = document.getElementById("detail");

const tg = window.Telegram?.WebApp;
if (tg) {
  tg.ready();
  tg.expand();
}

const params = new URLSearchParams(window.location.search);
const arenaId = params.get("arena") || tg?.initDataUnsafe?.start_param || null;
const sessionId = params.get("session");

function setStatus(text) {
  if (statusEl) statusEl.textContent = text;
}

function setDetail(text) {
  if (detailEl) detailEl.textContent = text || "";
}

function initDataHeader() {
  const initData = tg?.initData;
  return initData ? { "X-Telegram-Init-Data": initData } : {};
}

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function encodeBattleParam(battle) {
  const json = JSON.stringify(battle);
  const bytes = new TextEncoder().encode(json);
  let binary = "";
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/g, "");
}

function roleStatusText(arena) {
  if (arena.status === "waiting") {
    if (arena.role === "player1") {
      return "Вы — левый боец";
    }
    if (arena.role === "spectator") {
      return "Зал ожидания";
    }
    return "Ожидание бойцов";
  }
  if (arena.role === "spectator") {
    return "Вы — зритель";
  }
  if (arena.isHost) {
    return "Вы — хост боя";
  }
  if (arena.role === "player1" || arena.role === "player2") {
    return "Вы — боец";
  }
  return "Бой начинается";
}

function waitingDetail(arena) {
  if (arena.status !== "waiting") {
    return "";
  }
  const p1 = arena.player1?.displayName;
  if (!p1) {
    return "Ждём первого бойца…";
  }
  return `Левый: ${p1}\nЖдём правого бойца…`;
}

function fightingDetail(arena) {
  const p1 = arena.player1?.displayName || "Игрок 1";
  const p2 = arena.player2?.displayName || "Игрок 2";
  const { leftHp, rightHp } = arena.battle || { leftHp: 100, rightHp: 100 };
  return `${p1} (${leftHp} HP) vs ${p2} (${rightHp} HP)`;
}

async function fetchArena(id) {
  const response = await fetch(`/api/arenas/${encodeURIComponent(id)}`, {
    headers: initDataHeader(),
  });
  if (!response.ok) {
    const body = await response.json().catch(() => ({}));
    throw new Error(body.detail || "Арена не найдена.");
  }
  return response.json();
}

async function joinArena(id) {
  const response = await fetch(`/api/arenas/${encodeURIComponent(id)}/join`, {
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

async function fetchSession(id) {
  const response = await fetch(`/api/sessions/${encodeURIComponent(id)}`);
  if (!response.ok) {
    const body = await response.json().catch(() => ({}));
    throw new Error(body.detail || "Сессия не найдена.");
  }
  return response.json();
}

function launchUnity({ battle, arena, session, isHost }) {
  setStatus("Загрузка игры…");
  setDetail("");

  const gameParams = new URLSearchParams();
  gameParams.set("battle", encodeBattleParam(battle));
  if (isHost) gameParams.set("host", "1");
  // arena/session omitted — Unity page must not re-enter lobby

  window.location.replace(`/game/index.html?${gameParams.toString()}`);
}

async function runArenaLobby(id) {
  if (!tg?.initData) {
    setStatus("Откройте арену через кнопку в Telegram.");
    return;
  }

  setStatus("Вход на арену…");
  let arena = await joinArena(id);
  setStatus(roleStatusText(arena));
  setDetail(waitingDetail(arena));

  while (arena.status === "waiting") {
    await sleep(1500);
    arena = await fetchArena(id);
    setStatus(roleStatusText(arena));
    setDetail(waitingDetail(arena));
  }

  if (arena.status === "fighting" && arena.battle) {
    setStatus("Оба бойца на месте!");
    setDetail(fightingDetail(arena));
    await sleep(400);
    launchUnity({
      battle: arena.battle,
      arena: arena.id,
      isHost: arena.isHost,
    });
    return;
  }

  if (arena.status === "done" || arena.status === "expired") {
    setStatus("Арена уже завершена.");
    return;
  }

  setStatus("Не удалось начать бой.");
}

async function runSessionLobby(id) {
  setStatus("Загрузка боя…");
  const session = await fetchSession(id);
  launchUnity({
    battle: session.battle,
    session: session.sessionId || id,
    isHost: true,
  });
}

async function main() {
  try {
    // /game/ must be Unity WebGL — if battle is in URL but lobby script loaded, stop loop
    if (params.get("battle") && window.location.pathname.startsWith("/game")) {
      setStatus("Unity WebGL не задеплоен.");
      setDetail("Запустите полный CI (сборка Unity) или дождитесь деплоя game/.");
      return;
    }

    if (arenaId && !params.get("battle")) {
      await runArenaLobby(arenaId);
      return;
    }
    if (sessionId && !params.get("battle")) {
      await runSessionLobby(sessionId);
      return;
    }
    setStatus("Откройте страницу через кнопку в боте.");
  } catch (error) {
    console.error(error);
    setStatus(error.message || "Ошибка.");
    setDetail("");
  }
}

main();
