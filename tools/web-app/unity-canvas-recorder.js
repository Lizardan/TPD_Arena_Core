(() => {
  const params = new URLSearchParams(window.location.search);
  const arenaId = params.get("arena");
  const sessionId = params.get("session");
  const isHost = params.get("host") === "1";

  if (!arenaId && !sessionId) {
    return;
  }

  function log(message, data) {
    if (typeof data === "undefined") {
      console.log(`[MiniAppRecorder] ${message}`);
      return;
    }
    console.log(`[MiniAppRecorder] ${message}`, data);
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
      "video/webm;codecs=vp9",
      "video/webm;codecs=vp8",
      "video/webm",
    ];
    for (const mime of candidates) {
      if (MediaRecorder.isTypeSupported(mime)) return mime;
    }
    return "";
  }

  function fileExtFromMime(mimeType) {
    if (mimeType.includes("mp4")) return "mp4";
    return "webm";
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
        log(`Uploading attempt ${attempt} → ${endpoint}`);
        const response = await fetch(endpoint, {
          method: "POST",
          headers,
          body: formData,
        });
        if (!response.ok) {
          const body = await response.json().catch(() => ({}));
          throw new Error(body.detail || `Upload failed (${response.status})`);
        }
        log("Upload success");
        if (tg) {
          await sleep(800);
          tg.close();
        }
        return;
      } catch (error) {
        lastError = error;
        log("Upload attempt failed", error?.message || error);
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
      log("Skip recording for non-host arena player");
      return;
    }

    if (typeof MediaRecorder === "undefined") {
      log("MediaRecorder is not available in this WebView");
      return;
    }

    const canvas = resolveCanvas();
    if (!canvas || typeof canvas.captureStream !== "function") {
      log("Unity canvas is not ready for capture");
      return;
    }

    try {
      const stream = canvas.captureStream(30);
      const mimeType = resolveMimeType();
      const recorderOptions = mimeType ? { mimeType } : undefined;

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
          log("No recorded chunks, nothing to upload");
          return;
        }

        state.uploadInFlight = true;
        try {
          const blob = new Blob(state.chunks, { type: state.mimeType });
          log("Recording complete", { bytes: blob.size, mimeType: state.mimeType });
          await uploadVideoBlob(blob, state.mimeType);
        } catch (error) {
          log("Upload after recording failed", error?.message || error);
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

      recorder.start(1000);
      state.recording = true;
      log("Recording started", { mimeType: state.mimeType, arenaId, sessionId, isHost });
    } catch (error) {
      log("Failed to start recording", error?.message || error);
    }
  }

  function stopRecording() {
    if (!state.recording || !state.recorder) return;
    if (state.recorder.state !== "inactive") {
      state.recorder.stop();
    }
  }

  window.TPDMiniAppRecorder = {
    onBattleStarted: startRecording,
    onBattleFinished: stopRecording,
  };

  log("Bridge ready", { arenaId, sessionId, isHost });
})();
