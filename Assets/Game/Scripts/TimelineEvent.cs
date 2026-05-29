using System;

[Serializable]
public class TimelineEvent
{
    public float timestamp;
    public TimelineEventType eventType;
    public int actorPlayer;
    public int targetPlayer;
    public string abilityName;
    public int slotIndex;
    public int damage;           // урон, хил или щит
    public float remainingCastTime;
    public int targetHPLeft;
    public int targetShieldLeft;
    public int actorHPLeft;
    public int castSessionId;
}

public enum TimelineEventType
{
    StartCasting,
    Hit,
    HealApplied,
    ShieldApplied,
    CastInterrupted,
    StunApplied,
    StunAttemptFailed,
    StunEnded,
    ResumeCasting,
    CastFinished,
    CooldownExpired,
    BattleEnd
}