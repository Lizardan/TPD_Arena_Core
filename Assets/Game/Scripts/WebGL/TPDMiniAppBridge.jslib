mergeInto(LibraryManager.library, {
  TPD_Web_OnBattleStarted: function () {
    try {
      if (window.TPDMiniAppRecorder && typeof window.TPDMiniAppRecorder.onBattleStarted === "function") {
        window.TPDMiniAppRecorder.onBattleStarted();
      }
    } catch (e) {
    }
  },

  TPD_Web_OnBattleFinished: function (winnerSide) {
    try {
      if (window.TPDMiniAppRecorder && typeof window.TPDMiniAppRecorder.onBattleFinished === "function") {
        window.TPDMiniAppRecorder.onBattleFinished(Number(winnerSide) || 0);
      }
    } catch (e) {
    }
  },
});
