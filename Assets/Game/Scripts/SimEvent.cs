using System;

public class SimEvent : IComparable<SimEvent>
{
    public float time;
    public int priority;
    public enum SimEventType { CooldownExpired, Hit, CastFinished, StunApplied, StunEnded }
    public SimEventType type;
    public CharacterSimState character;
    public int slotIndex;
    public AbilitySlot slot;
    public float stunDuration;
    public string stunAbilityName;
    public bool cancelled;

    public int CompareTo(SimEvent other)
    {
        int cmp = time.CompareTo(other.time);
        if (cmp == 0) cmp = priority.CompareTo(other.priority);
        return cmp;
    }
}