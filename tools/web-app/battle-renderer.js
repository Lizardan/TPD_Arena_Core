const FPS = 24;
const WIDTH = 512;
const HEIGHT = 512;

export class BattleRenderer {
  constructor(canvas, battle, labels = {}) {
    this.canvas = canvas;
    this.ctx = canvas.getContext("2d");
    this.battle = battle;
    this.timeline = battle.events;
    this.duration = battle.duration;
    this.leftMax = battle.leftHp;
    this.rightMax = battle.rightHp;
    this.leftLabel = labels.left || "ЛЕВЫЙ";
    this.rightLabel = labels.right || "ПРАВЫЙ";
    this.canvas.width = WIDTH;
    this.canvas.height = HEIGHT;
  }

  frameCount() {
    return Math.max(1, Math.ceil(this.duration * FPS));
  }

  renderAt(time) {
    const state = this.#stateAt(time);
    this.#drawScene(state, time);
  }

  #stateAt(time) {
    let state = {
      p1Hp: this.leftMax,
      p2Hp: this.rightMax,
      p1Shield: 0,
      p2Shield: 0,
      lastHit: null,
      banner: "",
    };

    for (const event of this.timeline) {
      if (event.timestamp > time) break;
      state.p1Hp = event.p1Hp;
      state.p2Hp = event.p2Hp;
      state.p1Shield = event.p1Shield;
      state.p2Shield = event.p2Shield;

      if (event.eventType === "Hit") {
        state.lastHit = {
          target: event.targetPlayer,
          damage: event.damage,
          until: event.timestamp + 0.8,
        };
      }
      if (event.eventType === "BattleStart") state.banner = "Бой";
      if (event.eventType === "BattleEnd") state.banner = event.label || "Конец боя";
      if (event.eventType === "StartCasting") {
        state.banner = `${event.actorPlayer === 1 ? this.leftLabel : this.rightLabel}: ${event.abilityName}`;
      }
    }

    return state;
  }

  #drawScene(state, time) {
    const ctx = this.ctx;
    ctx.fillStyle = "#10131f";
    ctx.fillRect(0, 0, WIDTH, HEIGHT);

    const gradient = ctx.createLinearGradient(0, 0, WIDTH, HEIGHT);
    gradient.addColorStop(0, "#1a2340");
    gradient.addColorStop(1, "#0d111c");
    ctx.fillStyle = gradient;
    ctx.fillRect(0, 0, WIDTH, HEIGHT);

    this.#drawFighter(120, 290, "#4ea1ff", state.p1Hp, this.leftMax, state.p1Shield, this.leftLabel);
    this.#drawFighter(392, 290, "#ff6b6b", state.p2Hp, this.rightMax, state.p2Shield, this.rightLabel);

    if (state.lastHit && state.lastHit.until >= time) {
      const x = state.lastHit.target === 1 ? 120 : 392;
      ctx.fillStyle = "#ffd166";
      ctx.font = "bold 28px sans-serif";
      ctx.textAlign = "center";
      ctx.fillText(`-${state.lastHit.damage}`, x, 210);
    }

    ctx.fillStyle = "#e8ecf8";
    ctx.font = "bold 22px sans-serif";
    ctx.textAlign = "center";
    ctx.fillText(state.banner || "TPD Arena", WIDTH / 2, 56);

    ctx.font = "14px sans-serif";
    ctx.fillStyle = "#9aa7c4";
    ctx.fillText(`${state.p1Hp} HP  vs  ${state.p2Hp} HP`, WIDTH / 2, 84);
  }

  #drawFighter(x, y, color, hp, maxHp, shield, label) {
    const ctx = this.ctx;
    const bodyW = 90;
    const bodyH = 120;

    ctx.fillStyle = "#22293d";
    ctx.fillRect(x - bodyW / 2, y - bodyH, bodyW, bodyH);
    ctx.fillStyle = color;
    ctx.fillRect(x - bodyW / 2 + 8, y - bodyH + 8, bodyW - 16, bodyH - 16);

    const barW = 110;
    const barX = x - barW / 2;
    const hpRatio = Math.max(0, hp / maxHp);
    ctx.fillStyle = "#2b3145";
    ctx.fillRect(barX, y + 16, barW, 14);
    ctx.fillStyle = color;
    ctx.fillRect(barX, y + 16, barW * hpRatio, 14);

    if (shield > 0) {
      ctx.fillStyle = "#8ec5ff";
      ctx.fillRect(barX, y + 34, barW * Math.min(1, shield / 40), 8);
    }

    ctx.fillStyle = "#cbd5f1";
    ctx.font = "12px sans-serif";
    ctx.textAlign = "center";
    const shortLabel = label.length > 14 ? `${label.slice(0, 12)}…` : label;
    ctx.fillText(shortLabel, x, y + 58);
    ctx.fillText(`${hp}/${maxHp}`, x, y + 74);
  }
}

/**
 * Real-time playback (1x) with optional parallel max-speed MP4 encode on host.
 */
export async function playAndEncodeBattle(canvas, battle, options = {}) {
  const {
    battleStartAt,
    isHost = false,
    labels = {},
    onProgress,
    onStatus,
  } = options;

  const renderer = new BattleRenderer(canvas, battle, labels);
  const frameCount = renderer.frameCount();

  let encodePromise = null;
  if (isHost) {
    const offscreen = document.createElement("canvas");
    const encodeRenderer = new BattleRenderer(offscreen, battle, labels);
    encodePromise = encodeAllFramesFast(encodeRenderer, battle, frameCount);
  }

  if (battleStartAt) {
    await waitUntil(battleStartAt);
  }

  await playRealtime(renderer, battle.duration, onProgress);

  if (!isHost) return null;

  onStatus?.("Сохраняем видео...");
  return encodePromise;
}

async function playRealtime(renderer, duration, onProgress) {
  return new Promise((resolve) => {
    const startMs = performance.now();

    const tick = () => {
      const elapsed = (performance.now() - startMs) / 1000;
      const time = Math.min(elapsed, duration);
      renderer.renderAt(time);

      const frameIndex = Math.floor(time * FPS);
      onProgress?.(frameIndex, Math.ceil(duration * FPS));

      if (time >= duration) {
        resolve();
        return;
      }
      requestAnimationFrame(tick);
    };

    requestAnimationFrame(tick);
  });
}

async function encodeAllFramesFast(renderer, battle, frameCount) {
  if (typeof VideoEncoder !== "undefined" && typeof VideoFrame !== "undefined") {
    try {
      return await encodeMp4WebCodecs(renderer, frameCount, battle);
    } catch (error) {
      console.warn("WebCodecs fast encode failed:", error);
    }
  }

  throw new Error(
    "Это устройство не умеет кодировать MP4. Откройте бота в Telegram на Android или в Chrome на ПК.",
  );
}

export async function recordBattleVideo(canvas, battle, onProgress) {
  const renderer = new BattleRenderer(canvas, battle);
  const frameCount = renderer.frameCount();
  return encodeMp4WebCodecs(renderer, frameCount, battle, onProgress);
}

async function encodeMp4WebCodecs(renderer, frameCount, battle, onProgress) {
  const { Muxer, ArrayBufferTarget } = await import("https://esm.sh/mp4-muxer@5.1.3");

  const muxer = new Muxer({
    target: new ArrayBufferTarget(),
    video: { codec: "avc", width: WIDTH, height: HEIGHT },
    fastStart: "in-memory",
  });

  let encodeError = null;
  const encoder = new VideoEncoder({
    output: (chunk, meta) => muxer.addVideoChunk(chunk, meta),
    error: (error) => {
      encodeError = error;
    },
  });

  const config = {
    codec: "avc1.42E01E",
    width: WIDTH,
    height: HEIGHT,
    bitrate: 2_500_000,
    framerate: FPS,
    latencyMode: "quality",
  };

  const { supported } = await VideoEncoder.isConfigSupported(config);
  if (!supported) {
    throw new Error("Кодек H.264 не поддерживается на этом устройстве.");
  }

  encoder.configure(config);
  const frameDurationUs = Math.round(1_000_000 / FPS);

  for (let i = 0; i <= frameCount; i++) {
    const time = Math.min(i / FPS, battle.duration);
    renderer.renderAt(time);

    const frame = new VideoFrame(renderer.canvas, {
      timestamp: i * frameDurationUs,
      duration: frameDurationUs,
    });

    while (encoder.encodeQueueSize > 4) {
      await sleep(1);
    }

    encoder.encode(frame, { keyFrame: i % FPS === 0 });
    frame.close();

    if (encodeError) throw encodeError;
    onProgress?.(i, frameCount);
  }

  await encoder.flush();
  if (encodeError) throw encodeError;

  muxer.finalize();
  return new Blob([muxer.target.buffer], { type: "video/mp4" });
}

function waitUntil(timestampMs) {
  const delay = timestampMs - Date.now();
  if (delay <= 0) return Promise.resolve();
  return sleep(delay);
}

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

export { FPS, WIDTH, HEIGHT };
