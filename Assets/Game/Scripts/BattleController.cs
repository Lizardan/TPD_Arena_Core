using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class BattleController : MonoBehaviour
{
    [Header("Player cubes and UI")]
    public PlayerUI player1UI;
    public PlayerUI player2UI;
    public GameObject player1Cube;
    public GameObject player2Cube;
    public Animator player1Animator;
    public Animator player2Animator;

    [Header("Ability sets")]
    public AbilityDataSO[] player1Abilities;
    public AbilityDataSO[] player2Abilities;
    public AbilityDataSO autoAttackData;

    [Header("UI Controls")]
    public Button calculateButton;
    public Button startBattleButton;
    public Slider timelineSlider;
    public RectTransform player1TimelineContainer;
    public RectTransform player2TimelineContainer;
    public GameObject timelineBlockPrefab;

    [Header("VFX Prefabs")]
    public GameObject stunVFXPrefab;
    public GameObject damageTextPrefab;

    [Header("Stun VFX Local")]
    public Vector3 stunVFXLocalPosPlayer1 = new Vector3(0.5f, 1.25f, -1f);
    public Vector3 stunVFXLocalPosPlayer2 = new Vector3(-0.5f, 1.25f, -1f);
    public Vector3 stunVFXLocalScale = new Vector3(0.5f, 0.5f, 0.5f);

    private List<TimelineEvent> timelineEvents;
    private float battleDuration;
    private bool battleCalculated;
    private Coroutine playbackCoroutine;

    private BattleReplayer replayer;

    private void Start()
    {
        replayer = GetComponent<BattleReplayer>();
        if (replayer == null)
        {
            Debug.LogError("BattleReplayer component not found on this GameObject!");
            return;
        }

        startBattleButton.gameObject.SetActive(false);
        timelineSlider.gameObject.SetActive(false);
        player1TimelineContainer.gameObject.SetActive(false);
        player2TimelineContainer.gameObject.SetActive(false);

        calculateButton.onClick.AddListener(CalculateBattle);
        startBattleButton.onClick.AddListener(StartBattle);
        timelineSlider.onValueChanged.AddListener(OnSliderChanged);
    }

    public void CalculateBattle()
    {
        replayer.ClearVFX();
        timelineEvents = BattleSimulator.Simulate(player1Abilities, player2Abilities, autoAttackData);
        battleCalculated = true;

        if (timelineEvents.Count == 0)
        {
            battleDuration = 0f;
            return;
        }

        battleDuration = timelineEvents[timelineEvents.Count - 1].timestamp;
        timelineSlider.minValue = 0f;
        timelineSlider.maxValue = battleDuration;
        timelineSlider.value = 0f;

        player1TimelineContainer.gameObject.SetActive(true);
        player2TimelineContainer.gameObject.SetActive(true);
        timelineSlider.gameObject.SetActive(true);
        startBattleButton.gameObject.SetActive(true);

        BuildTimelineBar(1, player1TimelineContainer);
        BuildTimelineBar(2, player2TimelineContainer);

        replayer.Initialize(timelineEvents);

        string json = JsonUtility.ToJson(new TimelineEventList { events = timelineEvents }, true);
        Debug.Log("=== Battle Timeline JSON ===\n" + json);
    }

    [System.Serializable]
    private class TimelineEventList { public List<TimelineEvent> events; }

    void BuildTimelineBar(int playerIndex, RectTransform container)
    {
        foreach (Transform child in container) Destroy(child.gameObject);
        if (timelineEvents == null || timelineEvents.Count == 0) return;

        float containerWidth = container.rect.width;
        List<Interval> intervals = GetPlayerIntervals(playerIndex);

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

            img.color = iv.isStun ? Color.gray : iv.ability.displayColor;
        }
    }

    struct Interval
    {
        public float startTime;
        public float endTime;
        public bool isStun;
        public AbilityDataSO ability;
    }

    List<Interval> GetPlayerIntervals(int playerIndex)
    {
        List<Interval> intervals = new List<Interval>();
        List<TimelineEvent> relevant = new List<TimelineEvent>();
        foreach (var ev in timelineEvents)
            if (ev.actorPlayer == playerIndex || ev.targetPlayer == playerIndex)
                relevant.Add(ev);

        relevant.Sort((a, b) =>
        {
            int cmp = a.timestamp.CompareTo(b.timestamp);
            if (cmp == 0)
            {
                int pA = BattleReplayer.EventPriority.ContainsKey(a.eventType) ? BattleReplayer.EventPriority[a.eventType] : 99;
                int pB = BattleReplayer.EventPriority.ContainsKey(b.eventType) ? BattleReplayer.EventPriority[b.eventType] : 99;
                cmp = pA.CompareTo(pB);
            }
            return cmp;
        });

        const int Idle = 0, Casting = 1, Stunned = 2;
        int state = Idle;
        AbilityDataSO currentAbility = null;
        float stateStart = 0f;

        foreach (var ev in relevant)
        {
            if (ev.actorPlayer == playerIndex)
            {
                switch (ev.eventType)
                {
                    case TimelineEventType.StartCasting:
                    case TimelineEventType.ResumeCasting:
                        if (state == Idle)
                        {
                            state = Casting;
                            currentAbility = BattleSimulator.GetAbilityByName(ev.abilityName,
                                playerIndex == 1 ? player1Abilities : player2Abilities, autoAttackData);
                            stateStart = ev.timestamp;
                        }
                        break;
                    case TimelineEventType.CastInterrupted:
                        if (state == Casting)
                        {
                            intervals.Add(new Interval { startTime = stateStart, endTime = ev.timestamp, isStun = false, ability = currentAbility });
                            state = Idle;
                        }
                        break;
                    case TimelineEventType.CastFinished:
                        if (state == Casting)
                        {
                            intervals.Add(new Interval { startTime = stateStart, endTime = ev.timestamp, isStun = false, ability = currentAbility });
                            state = Idle;
                        }
                        break;
                    case TimelineEventType.StunEnded:
                        if (state == Stunned)
                        {
                            intervals.Add(new Interval { startTime = stateStart, endTime = ev.timestamp, isStun = true, ability = null });
                            state = Idle;
                        }
                        break;
                }
            }

            if (ev.targetPlayer == playerIndex && ev.eventType == TimelineEventType.StunApplied)
            {
                if (state == Casting)
                {
                    intervals.Add(new Interval { startTime = stateStart, endTime = ev.timestamp, isStun = false, ability = currentAbility });
                }
                state = Stunned;
                stateStart = ev.timestamp;
            }
        }

        if (state != Idle)
        {
            intervals.Add(new Interval
            {
                startTime = stateStart,
                endTime = battleDuration,
                isStun = (state == Stunned),
                ability = currentAbility
            });
        }
        return intervals;
    }

    public void StartBattle()
    {
        if (!battleCalculated) return;
        if (playbackCoroutine != null) StopCoroutine(playbackCoroutine);
        playbackCoroutine = StartCoroutine(PlayBattle());
    }

    private IEnumerator PlayBattle()
    {
        float startTime = Time.time;
        while (Time.time - startTime < battleDuration)
        {
            float elapsed = Time.time - startTime;
            timelineSlider.value = elapsed;
            replayer.UpdateAtTime(elapsed);
            yield return null;
        }
        timelineSlider.value = battleDuration;
        replayer.UpdateAtTime(battleDuration);
    }

    void OnSliderChanged(float value)
    {
        if (!battleCalculated || replayer == null) return;
        replayer.UpdateAtTime(value);
    }
}