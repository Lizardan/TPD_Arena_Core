using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using TMPro;

using UnityEngine;

using UnityEngine.UI;

using static TPD.Arena.TimelineStateBuilder;



namespace TPD.Arena

{

    public class BattleController : MonoBehaviour

    {

        [Header("Config")]

        public BattleConfigSO battleConfig;



        [Header("Player Controllers")]

        public PlayerController player1;

        public PlayerController player2;



        [Header("UI Controls")]

        public Button calculateButton;

        public Button startBattleButton;

        public Slider timelineSlider;

        public RectTransform player1TimelineContainer;

        public RectTransform player2TimelineContainer;

        public GameObject timelineBlockPrefab;

        public Toggle exportModeToggle;

        public TextMeshProUGUI exportStatusText;

        public TextMeshProUGUI namesText;

        public AudioListener exportAudioListener;

        [Header("Editor Debug")]
        public GameObject debugCanvas;



        private List<TimelineEvent> timelineEvents;

        private float battleDuration;

        private bool battleCalculated;

        private bool isPlaying;

        private bool miniAppPlayback;

        private Coroutine playbackCoroutine;

        private BattlePlaybackMode playbackMode = BattlePlaybackMode.Runtime;



        private BattleReplayer replayer;



        private int MaxHealth => battleConfig != null ? battleConfig.maxHealth : 100;

        private int? requestLeftHp;
        private int? requestRightHp;
        private string leftFighterName = "Левый";
        private string rightFighterName = "Правый";

        private int LeftMaxHp => requestLeftHp ?? MaxHealth;
        private int RightMaxHp => requestRightHp ?? MaxHealth;

        public BattleAbilityRegistry BattleAbilityRegistry =>
            battleConfig != null ? battleConfig.abilityRegistry : null;

        public void ApplyBattleRequest(BattleRequestJson request)
        {
            if (request == null)
                return;

            requestLeftHp = request.leftHp;
            requestRightHp = request.rightHp;
            leftFighterName = SanitizeName(request.leftName, "Левый");
            rightFighterName = SanitizeName(request.rightName, "Правый");
            player1.SetMaxHP(LeftMaxHp);
            player2.SetMaxHP(RightMaxHp);
            UpdateNamesLabel();
        }

        /// <summary>
        /// Telegram Mini App: apply JSON, hide editor UI, simulate and play battle.
        /// </summary>
        public void RunFromTelegramRequest(BattleRequestJson request)
        {
            if (request == null)
                return;

            try
            {
                BattleRequestResolver.ApplyToController(this, request);
            }
            catch (System.InvalidOperationException ex)
            {
                Debug.LogError($"[MiniApp] {ex.Message}");
                return;
            }

            miniAppPlayback = true;
            SetDebugCanvasVisible(false);
            SetExportChromeVisible(false);
            CalculateBattle();
            StartBattle();
        }

        public bool TryExportBattleVideo(string outputPath, out BattleVideoExportService.ExportResult result)
        {
            result = default;

            if (!battleCalculated || timelineEvents == null || timelineEvents.Count == 0)
            {
                result.error = "Battle is not calculated.";
                return false;
            }

            if (replayer == null)
                replayer = GetComponent<BattleReplayer>();

            HideCaptureExcludedUI();

            result = BattleVideoExportService.ExportBlocking(
                new BattleVideoExportService.ExportRequest
                {
                    timelineEvents = timelineEvents,
                    battleDuration = battleDuration,
                    p1MaxHealth = LeftMaxHp,
                    p2MaxHealth = RightMaxHp,
                    outputPathOverride = outputPath,
                    config = battleConfig,
                    replayer = replayer,
                    timelineSlider = timelineSlider,
                    captureCamera = Camera.main,
                    audioListener = exportAudioListener,
                    onExportBegin = HideCaptureExcludedUI,
                    onExportEnd = ShowCaptureExcludedUI
                });

            return result.success;
        }



        private void Start()

        {

            replayer = GetComponent<BattleReplayer>();

            if (replayer == null)

            {

                Debug.LogError("BattleReplayer component not found on this GameObject!");

                return;

            }



            if (exportAudioListener == null)

                exportAudioListener = Camera.main != null ? Camera.main.GetComponent<AudioListener>() : null;



            if (debugCanvas == null)

            {

                var foundDebugCanvas = GameObject.Find("CanvasDEBUG");

                if (foundDebugCanvas != null)

                    debugCanvas = foundDebugCanvas;

            }



            player1.SetMaxHP(LeftMaxHp);

            player2.SetMaxHP(RightMaxHp);

            if (namesText == null)
            {
                var namesGo = GameObject.Find("Text (TMP) (1)");
                if (namesGo != null)
                    namesText = namesGo.GetComponent<TextMeshProUGUI>();
            }



            startBattleButton.gameObject.SetActive(false);

            timelineSlider.gameObject.SetActive(false);

            player1TimelineContainer.gameObject.SetActive(false);

            player2TimelineContainer.gameObject.SetActive(false);



            EnsureExportUI();

            if (exportModeToggle != null)

            {

                exportModeToggle.isOn = false;

                exportModeToggle.onValueChanged.AddListener(OnExportModeToggleChanged);

            }



            calculateButton.onClick.AddListener(CalculateBattle);

            startBattleButton.onClick.AddListener(StartBattle);

            timelineSlider.onValueChanged.AddListener(OnSliderChanged);

            UpdateStartButtonLabel();

            SetExportStatus(string.Empty);
            UpdateNamesLabel();

        }



        private void EnsureExportUI()

        {

            if (exportModeToggle != null && exportStatusText != null)

                return;



            Canvas canvas = calculateButton != null

                ? calculateButton.GetComponentInParent<Canvas>()

                : null;

            if (canvas == null)

                return;



            if (exportModeToggle == null)

            {

                var toggleGo = new GameObject("ExportModeToggle", typeof(RectTransform), typeof(Toggle), typeof(Image));

                RectTransform refButton = calculateButton != null
                    ? calculateButton.GetComponent<RectTransform>()
                    : startBattleButton != null ? startBattleButton.GetComponent<RectTransform>() : null;

                Transform toggleParent = refButton != null ? refButton.parent : canvas.transform;
                toggleGo.transform.SetParent(toggleParent, false);

                var toggleRect = toggleGo.GetComponent<RectTransform>();
                const float toggleSize = 28f;
                const float toggleGap = 8f;

                if (refButton != null)
                {
                    toggleRect.anchorMin = refButton.anchorMin;
                    toggleRect.anchorMax = refButton.anchorMax;
                    toggleRect.pivot = new Vector2(0f, 0.5f);
                    toggleRect.sizeDelta = new Vector2(toggleSize, toggleSize);
                    float buttonRightX = refButton.anchoredPosition.x + refButton.sizeDelta.x * 0.5f;
                    toggleRect.anchoredPosition = new Vector2(buttonRightX + toggleGap, refButton.anchoredPosition.y);
                }
                else
                {
                    toggleRect.anchorMin = new Vector2(0.5f, 1f);
                    toggleRect.anchorMax = new Vector2(0.5f, 1f);
                    toggleRect.pivot = new Vector2(0.5f, 0.5f);
                    toggleRect.anchoredPosition = new Vector2(140f, -20f);
                    toggleRect.sizeDelta = new Vector2(toggleSize, toggleSize);
                }

                var background = toggleGo.GetComponent<Image>();
                background.color = new Color(1f, 1f, 1f, 0.15f);

                var checkmarkGo = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
                checkmarkGo.transform.SetParent(toggleGo.transform, false);

                var checkRect = checkmarkGo.GetComponent<RectTransform>();
                checkRect.anchorMin = new Vector2(0.5f, 0.5f);
                checkRect.anchorMax = new Vector2(0.5f, 0.5f);
                checkRect.pivot = new Vector2(0.5f, 0.5f);
                checkRect.anchoredPosition = Vector2.zero;
                checkRect.sizeDelta = new Vector2(20f, 20f);
                checkmarkGo.GetComponent<Image>().color = new Color(0.2f, 0.8f, 0.3f, 1f);

                exportModeToggle = toggleGo.GetComponent<Toggle>();
                exportModeToggle.targetGraphic = background;
                exportModeToggle.graphic = checkmarkGo.GetComponent<Image>();
            }
            else
            {
                LayoutExportModeToggle();
            }



            if (exportStatusText == null)

            {

                var statusGo = new GameObject("ExportStatusText", typeof(RectTransform), typeof(TextMeshProUGUI));

                statusGo.transform.SetParent(canvas.transform, false);

                var statusRect = statusGo.GetComponent<RectTransform>();

                statusRect.anchorMin = new Vector2(0.5f, 1f);

                statusRect.anchorMax = new Vector2(0.5f, 1f);

                statusRect.pivot = new Vector2(0.5f, 1f);

                statusRect.anchoredPosition = new Vector2(0f, -100f);

                statusRect.sizeDelta = new Vector2(700f, 24f);

                exportStatusText = statusGo.GetComponent<TextMeshProUGUI>();
                exportStatusText.fontSize = 14f;
                exportStatusText.color = new Color(0.85f, 0.9f, 1f, 1f);
                exportStatusText.alignment = TextAlignmentOptions.Top;
                CopyTextStyle(startBattleButton != null ? startBattleButton.GetComponentInChildren<TextMeshProUGUI>() : null, exportStatusText);

            }

        }



        private static void CopyTextStyle(TextMeshProUGUI source, TextMeshProUGUI target)
        {
            if (source == null || target == null)
                return;

            target.font = source.font;
            target.fontSharedMaterial = source.fontSharedMaterial;
        }

        private void OnExportModeToggleChanged(bool exportEnabled)

        {

            playbackMode = exportEnabled ? BattlePlaybackMode.ExportVideo : BattlePlaybackMode.Runtime;

            UpdateStartButtonLabel();

            SetExportStatus(string.Empty);

        }



        private void UpdateStartButtonLabel()

        {

            if (startBattleButton == null)

                return;



            var label = startBattleButton.GetComponentInChildren<TextMeshProUGUI>();

            if (label != null)

                label.text = playbackMode == BattlePlaybackMode.ExportVideo ? "Export MP4" : "Start Battle";

        }



        private void SetExportStatus(string message)

        {

            if (exportStatusText != null)

                exportStatusText.text = message;

        }



        public void CalculateBattle()

        {

            replayer.ClearVFX();

            timelineEvents = BattleSimulator.Simulate(

                player1.abilities, player2.abilities,

                player1.autoAttack, player2.autoAttack,

                LeftMaxHp, RightMaxHp);

            battleCalculated = true;



            if (timelineEvents.Count == 0)

            {

                battleDuration = 0f;

                return;

            }



            battleDuration = timelineEvents[timelineEvents.Count - 1].timestamp;

            if (!miniAppPlayback)
            {
                timelineSlider.minValue = 0f;
                timelineSlider.maxValue = battleDuration;
                timelineSlider.value = 0f;

                player1TimelineContainer.gameObject.SetActive(true);
                player2TimelineContainer.gameObject.SetActive(true);
                timelineSlider.gameObject.SetActive(true);
                startBattleButton.gameObject.SetActive(true);
                startBattleButton.interactable = true;

                BuildTimelineBar(1, player1TimelineContainer);
                BuildTimelineBar(2, player2TimelineContainer);
            }



            replayer.Initialize(timelineEvents, LeftMaxHp, RightMaxHp);

            ResetVisualsToIdle();

            SetExportStatus(string.Empty);

        }



        void BuildTimelineBar(int playerIndex, RectTransform container)

        {

            foreach (Transform child in container) Destroy(child.gameObject);

            if (timelineEvents == null || timelineEvents.Count == 0) return;



            float containerWidth = container.rect.width;

            var playerEvents = FilterAndSortForPlayer(timelineEvents, playerIndex);

            var abilities = playerIndex == 1 ? player1.abilities : player2.abilities;

            var autoAttack = playerIndex == 1 ? player1.autoAttack : player2.autoAttack;

            List<TimelineInterval> intervals = BuildIntervals(playerEvents, playerIndex, battleDuration, abilities, autoAttack);



            foreach (var iv in intervals)

            {

                float startPercent = iv.startTime / battleDuration;

                float widthPercent = (iv.endTime - iv.startTime) / battleDuration;



                GameObject block = Instantiate(timelineBlockPrefab, container);

                RectTransform rt = block.GetComponent<RectTransform>();

                Image img = block.GetComponent<Image>();



                rt.anchorMin = new Vector2(0, 0);

                rt.anchorMax = new Vector2(0, 1);

                rt.pivot = new Vector2(0, 0.5f);

                rt.anchoredPosition = new Vector2(startPercent * containerWidth, 0f);

                rt.sizeDelta = new Vector2(widthPercent * containerWidth, 0f);



                img.color = iv.isStun ? Color.gray : (iv.ability != null ? iv.ability.displayColor : Color.white);

            }

        }



        public void StartBattle()

        {

            if (!battleCalculated || isPlaying) return;

            if (playbackCoroutine != null) StopCoroutine(playbackCoroutine);



            playbackCoroutine = playbackMode == BattlePlaybackMode.ExportVideo

                ? StartCoroutine(ExportBattleVideo())

                : StartCoroutine(PlayBattle());

        }



        private IEnumerator PlayBattle()

        {

            isPlaying = true;
            if (miniAppPlayback)
                NotifyMiniAppBattleStarted();

            startBattleButton.interactable = false;

            calculateButton.interactable = false;

            if (exportModeToggle != null) exportModeToggle.interactable = false;

            replayer.ClearVFX();

            replayer.Initialize(timelineEvents, LeftMaxHp, RightMaxHp);

            timelineSlider.SetValueWithoutNotify(0f);

            replayer.UpdateAtTime(0f, true);



            float startTime = Time.time;

            while (Time.time - startTime < battleDuration)

            {

                float elapsed = Time.time - startTime;

                timelineSlider.SetValueWithoutNotify(elapsed);

                replayer.UpdateAtTime(elapsed, true);

                yield return null;

            }



            timelineSlider.SetValueWithoutNotify(battleDuration);

            replayer.UpdateAtTime(battleDuration, true);



            int winnerSide = ShowBattleResult();
            if (miniAppPlayback)
                NotifyMiniAppBattleFinished(winnerSide);

            FinishPlayback();

        }



        private void SetDebugCanvasVisible(bool visible)
        {
            if (debugCanvas != null)
                debugCanvas.SetActive(visible);
        }

        private void SetExportChromeVisible(bool visible)
        {
            if (calculateButton != null)
                calculateButton.gameObject.SetActive(visible);
            if (startBattleButton != null)
                startBattleButton.gameObject.SetActive(visible);
            if (exportModeToggle != null)
                exportModeToggle.gameObject.SetActive(visible);
            if (exportStatusText != null)
                exportStatusText.gameObject.SetActive(visible);
        }

        private void LayoutExportModeToggle()
        {
            if (exportModeToggle == null)
                return;

            RectTransform refButton = calculateButton != null
                ? calculateButton.GetComponent<RectTransform>()
                : startBattleButton != null ? startBattleButton.GetComponent<RectTransform>() : null;
            if (refButton == null)
                return;

            var toggleRect = exportModeToggle.GetComponent<RectTransform>();
            const float toggleSize = 28f;
            const float toggleGap = 8f;

            if (toggleRect.parent != refButton.parent)
                toggleRect.SetParent(refButton.parent, false);

            toggleRect.anchorMin = refButton.anchorMin;
            toggleRect.anchorMax = refButton.anchorMax;
            toggleRect.pivot = new Vector2(0f, 0.5f);
            toggleRect.sizeDelta = new Vector2(toggleSize, toggleSize);
            float buttonRightX = refButton.anchoredPosition.x + refButton.sizeDelta.x * 0.5f;
            toggleRect.anchoredPosition = new Vector2(buttonRightX + toggleGap, refButton.anchoredPosition.y);

            var label = exportModeToggle.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null)
                label.gameObject.SetActive(false);
        }

        private void HideCaptureExcludedUI()
        {
            SetDebugCanvasVisible(false);
            Canvas.ForceUpdateCanvases();
        }

        private void ShowCaptureExcludedUI()
        {
            SetExportChromeVisible(true);
            SetDebugCanvasVisible(true);
        }

        private IEnumerator ExportBattleVideo()
        {
            isPlaying = true;
            startBattleButton.interactable = false;
            calculateButton.interactable = false;
            if (exportModeToggle != null) exportModeToggle.interactable = false;

            SetExportStatus("Preparing export...");

            BattleVideoExportService.ExportResult exportResult = default;
            yield return BattleVideoExportService.ExportCoroutine(
                new BattleVideoExportService.ExportRequest
                {
                    timelineEvents = timelineEvents,
                    battleDuration = battleDuration,
                    p1MaxHealth = LeftMaxHp,
                    p2MaxHealth = RightMaxHp,
                    config = battleConfig,
                    replayer = replayer,
                    timelineSlider = timelineSlider,
                    captureCamera = Camera.main,
                    audioListener = exportAudioListener,
                    onProgress = (frame, total) => SetExportStatus($"Exporting frame {frame}/{total}..."),
                    onExportBegin = HideCaptureExcludedUI,
                    onExportEnd = ShowCaptureExcludedUI
                },
                result => exportResult = result);

            if (exportResult.success)
            {
                SetExportStatus($"Saved: {exportResult.outputPath}");
                Debug.Log($"Battle video exported to: {exportResult.outputPath}");
                BattleVideoEncoderProvider.NotifyExportCompleted(exportResult.outputPath);
                ShowBattleResult();
            }
            else
            {
                SetExportStatus(exportResult.error ?? "Export failed.");
                Debug.LogError(exportResult.error ?? "Battle video export failed.");
            }

            FinishPlayback();
        }



        private void FinishPlayback()

        {

            isPlaying = false;

            startBattleButton.interactable = true;

            calculateButton.interactable = true;

            if (exportModeToggle != null) exportModeToggle.interactable = true;

            startBattleButton.gameObject.SetActive(true);

        }



        private int ShowBattleResult()

        {

            replayer.UpdateAtTime(battleDuration, false);

            var s1 = GetPlayerStateAtTime(

                FilterAndSortForPlayer(timelineEvents, 1), 1, battleDuration,

                player1.abilities, player1.autoAttack, LeftMaxHp);

            var s2 = GetPlayerStateAtTime(

                FilterAndSortForPlayer(timelineEvents, 2), 2, battleDuration,

                player2.abilities, player2.autoAttack, RightMaxHp);



            if (s1.hp <= 0 && s2.hp > 0)
            {
                player2.UpdateState("Won");
                return 2;
            }

            if (s2.hp <= 0 && s1.hp > 0)
            {
                player1.UpdateState("Won");
                return 1;
            }

            return 0;

        }



        void OnSliderChanged(float value)

        {

            if (!battleCalculated || replayer == null || isPlaying) return;

            replayer.UpdateAtTime(value, false);

        }



        private void ResetVisualsToIdle()

        {

            if (player1.animator) player1.animator.Play("Idle", 0, 0f);

            if (player2.animator) player2.animator.Play("Idle", 0, 0f);

            player1.UpdateHP(LeftMaxHp, 0);

            player2.UpdateHP(RightMaxHp, 0);

            player1.UpdateState("Idle");

            player2.UpdateState("Idle");

            UpdateNamesLabel();

        }

        private void UpdateNamesLabel()
        {
            if (namesText == null)
                return;
            namesText.text = $"{leftFighterName} vs {rightFighterName}";
        }

        private static string SanitizeName(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
                return fallback;
            value = value.Trim();
            return value.Length > 64 ? value.Substring(0, 64) : value;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void TPD_Web_OnBattleStarted();

        [DllImport("__Internal")]
        private static extern void TPD_Web_OnBattleFinished(int winnerSide);
#endif

        private static void NotifyMiniAppBattleStarted()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            TPD_Web_OnBattleStarted();
#endif
        }

        private static void NotifyMiniAppBattleFinished(int winnerSide)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            TPD_Web_OnBattleFinished(winnerSide);
#endif
        }

    }

}


