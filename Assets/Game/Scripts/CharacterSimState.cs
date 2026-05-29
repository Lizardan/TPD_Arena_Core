public class CharacterSimState
{
    public int health = 100;
    public int shield = 0;          // добавлено
    public AbilitySlot[] slots;
    public enum State { Idle, Casting, Stunned }
    public State state = State.Idle;
    public int currentSlotIndex = -1;
    public float castStartTime;
    public float castFinishTime;
    public float hitTime;
    public float remainingCastAfterStun;
    public float stunEndTime;
    public int currentCastSessionId;
    public int interruptedCastSessionId;
    public SimEvent activeHitEvent;
    public SimEvent activeCastFinishedEvent;
    public const int MAX_HEALTH = 100;

    public CharacterSimState(AbilityDataSO[] abilities)
    {
        slots = new AbilitySlot[abilities.Length];
        for (int i = 0; i < abilities.Length; i++)
            slots[i] = new AbilitySlot(abilities[i]);
    }
}