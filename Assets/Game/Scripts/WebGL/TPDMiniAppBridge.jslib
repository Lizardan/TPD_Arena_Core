mergeInto(LibraryManager.library, {
  TPD_Web_OnBattleStarted: function () {
    try {
      if (window.TPDMiniAppRecorder && typeof window.TPDMiniAppRecorder.onBattleStarted === "function") {
        window.TPDMiniAppRecorder.onBattleStarted();
      }
    } catch (e) {
      console.warn("[TPD bridge] onBattleStarted failed:", e);
    }
  },

  TPD_Web_OnBattleFinished: function () {
    try {
      if (window.TPDMiniAppRecorder && typeof window.TPDMiniAppRecorder.onBattleFinished === "function") {
        window.TPDMiniAppRecorder.onBattleFinished();
      }
    } catch (e) {
      console.warn("[TPD bridge] onBattleFinished failed:", e);
    }
  },
});
