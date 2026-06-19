(() => {
  const params = new URLSearchParams(window.location.search);
  const arenaId = params.get("arena");
  const sessionId = params.get("session");
  const isHost = params.get("host") === "1";
  const TARGET_WIDTH = 384;
  const TARGET_HEIGHT = 590;
  const TARGET_ASPECT = TARGET_WIDTH / TARGET_HEIGHT;

  if (!arenaId && !sessionId) {
    return;
  }

  function ensureTargetGameFrame() {
    const scale = Math.min(
      window.innerWidth / TARGET_WIDTH,
      window.innerHeight / TARGET_HEIGHT,
      1,
    );
    const frameWidth = Math.max(220, Math.round(TARGET_WIDTH * scale));
    const frameHeight = Math.max(320, Math.round(TARGET_HEIGHT * scale));
    const styleId = "tpd-square-frame-style";
    if (!document.getElementById(styleId)) {
      const style = document.createElement("style");
      style.id = styleId;
      style.textContent = `
html, body {
  margin: 0 !important;
  padding: 0 !important;
  width: 100% !important;
  height: 100% !important;
  background: #0d111c !important;
}
body {
  display: flex !important;
  align-items: center !important;
  justify-content: center !important;
  overflow: hidden !important;
}
#unity-container, .unity-container, .webgl-content {
  width: ${TARGET_WIDTH}px !important;
  height: ${TARGET_HEIGHT}px !important;
  aspect-ratio: ${TARGET_WIDTH} / ${TARGET_HEIGHT} !important;
  margin: 0 auto !important;
}
#unity-canvas, canvas#unity-canvas {
  width: 100% !important;
  height: 100% !important;
  display: block !important;
}
`;
      document.head.appendChild(style);
    }

    const container =
      document.getElementById("unity-container") ||
      document.querySelector(".unity-container") ||
      document.querySelector(".webgl-content");
    if (container) {
      container.style.width = `${frameWidth}px`;
      container.style.height = `${frameHeight}px`;
      container.style.aspectRatio = `${TARGET_WIDTH} / ${TARGET_HEIGHT}`;
      container.style.maxWidth = `${frameWidth}px`;
      container.style.maxHeight = `${frameHeight}px`;
      container.style.margin = "0 auto";
    }

    const canvas = resolveCanvas();
    if (canvas) {
      canvas.style.width = "100%";
      canvas.style.height = "100%";
    }
  }

  function sleep(ms) {
    return new Promise((resolve) => setTimeout(resolve, ms));
  }

  function resolveCanvas() {
    return (
      document.getElementById("unity-canvas") ||
      document.querySelector("canvas")
    );
  }

  function resolveMimeType() {
    if (typeof MediaRecorder === "undefined") return "";
    const candidates = [
      "video/mp4;codecs=h264",
      "video/mp4;codecs=avc1",
      "video/mp4",
    ];
    for (const mime of candidates) {
      if (MediaRecorder.isTypeSupported(mime)) return mime;
    }
    return "";
  }

  function fileExtFromMime(mimeType) {
    return "mp4";
  }

  function notifyUser(message) {
    try {
      if (window.Telegram?.WebApp?.showAlert) {
        window.Telegram.WebApp.showAlert(message);
        return;
      }
    } catch {}
    try {
      alert(message);
    } catch {}
  }

  async function ensureTelegramWebApp() {
    if (window.Telegram?.WebApp) return window.Telegram.WebApp;

    await new Promise((resolve) => {
      const script = document.createElement("script");
      script.src = "https://telegram.org/js/telegram-web-app.js";
      script.async = true;
      script.onload = resolve;
      script.onerror = resolve;
      document.head.appendChild(script);
    });

    return window.Telegram?.WebApp || null;
  }

  async function uploadVideoBlob(blob, mimeType) {
    const targetId = arenaId || sessionId;
    const targetType = arenaId ? "arena" : "session";
    const endpoint = arenaId
      ? `/api/arenas/${encodeURIComponent(arenaId)}/upload`
      : `/api/sessions/${encodeURIComponent(sessionId)}/upload`;

    const tg = await ensureTelegramWebApp();
    if (tg) {
      tg.ready();
      tg.expand();
    }

    const headers = {};
    const initData = tg?.initData?.trim();
    if (initData) {
      headers["X-Telegram-Init-Data"] = initData;
    }

    const ext = fileExtFromMime(mimeType);
    const filename = `${targetType}-${targetId}.${ext}`;
    const formData = new FormData();
    formData.append("file", blob, filename);

    let lastError = null;
    for (let attempt = 1; attempt <= 3; attempt++) {
      try {
        const response = await fetch(endpoint, {
          method: "POST",
          headers,
          body: formData,
        });
        if (!response.ok) {
          const body = await response.json().catch(() => ({}));
          throw new Error(body.detail || `Upload failed (${response.status})`);
        }
        if (tg) {
          await sleep(800);
          tg.close();
        }
        return;
      } catch (error) {
        lastError = error;
        await sleep(attempt * 600);
      }
    }

    throw lastError || new Error("Upload failed");
  }

  const state = {
    recording: false,
    uploadInFlight: false,
    recorder: null,
    stream: null,
    chunks: [],
    mimeType: "",
  };

  async function startRecording() {
    if (state.recording || state.uploadInFlight) return;

    if (arenaId && !isHost) {
      return;
    }

    if (typeof MediaRecorder === "undefined") {
      notifyUser("Ваш клиент Telegram не поддерживает запись MP4. Откройте арену на ПК.");
      return;
    }

    const canvas = resolveCanvas();
    if (!canvas || typeof canvas.captureStream !== "function") {
      return;
    }

    try {
      // Keep actual rendered frame deterministic for upload output.
      if (canvas.width !== TARGET_WIDTH || canvas.height !== TARGET_HEIGHT) {
        canvas.width = TARGET_WIDTH;
        canvas.height = TARGET_HEIGHT;
      }

      const stream = canvas.captureStream(24);
      const mimeType = resolveMimeType();
      if (!mimeType || !mimeType.toLowerCase().includes("mp4")) {
        notifyUser("На этом устройстве недоступна запись MP4 (H264). Используйте Telegram Desktop.");
        return;
      }
      const recorderOptions = mimeType
        ? { mimeType, videoBitsPerSecond: 1_200_000 }
        : { videoBitsPerSecond: 1_200_000 };

      const recorder = new MediaRecorder(stream, recorderOptions);
      state.chunks = [];
      state.mimeType = recorder.mimeType || mimeType || "video/webm";
      state.stream = stream;
      state.recorder = recorder;

      recorder.ondataavailable = (event) => {
        if (event.data && event.data.size > 0) {
          state.chunks.push(event.data);
        }
      };

      recorder.onstop = async () => {
        state.recording = false;
        if (state.uploadInFlight) return;

        if (!state.chunks.length) {
          return;
        }

        state.uploadInFlight = true;
        try {
          const blob = new Blob(state.chunks, { type: state.mimeType });
          await uploadVideoBlob(blob, state.mimeType);
        } catch {
          // keep silent in production; upload API returns user-visible status via bot
        } finally {
          state.uploadInFlight = false;
          state.chunks = [];
          if (state.stream) {
            for (const track of state.stream.getTracks()) {
              track.stop();
            }
          }
          state.stream = null;
          state.recorder = null;
        }
      };

      recorder.start(250);
      state.recording = true;
    } catch {
      // recorder init can fail on unsupported clients
    }
  }

  function stopRecording() {
    if (!state.recording || !state.recorder) return;
    state.recording = false;
    if (state.recorder.state !== "inactive") {
      state.recorder.stop();
    }
  }

  window.TPDMiniAppRecorder = {
    onBattleStarted: startRecording,
    onBattleFinished: stopRecording,
  };

  ensureTargetGameFrame();
  window.addEventListener("resize", ensureTargetGameFrame);
  const frameInit = () => ensureTargetGameFrame();
  setTimeout(frameInit, 300);
  setTimeout(frameInit, 1000);
})();
