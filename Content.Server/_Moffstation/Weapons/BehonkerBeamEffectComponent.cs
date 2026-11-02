using Robust.Shared.Prototypes;

namespace Content.Server._Moffstation.Weapons;

public enum BehonkerBeamEffect
{
    Pacify,
    RepeatedShock,
    SlipAndClumsy,
    Slow,
    Poison,
    Knockback,
    Sleep,
    ParcelWrap,
    FireStacks,
    Cluwne,
}

[RegisterComponent]
public sealed partial class BehonkerBeamEffectComponent : Component
{
    [DataField]
    public BehonkerBeamEffect Effect;

    [DataField]
    public Color? HitFeedbackColor; //For the beams w/o damage

    [DataField]
    public TimeSpan EffectDuration = TimeSpan.FromSeconds(5); //Base effect duration but can be changed/overriden in behonker.yml for individual lasers.

    [DataField]
    public TimeSpan RepeatShockInterval = TimeSpan.FromSeconds(0.75);

    [DataField]
    public int ShockDamage = 5;

    [DataField]
    public TimeSpan ShockStunDuration = TimeSpan.FromSeconds(0.75);

    [DataField]
    public float SpeedModifier = 0.5f;

    [DataField]
    public string Reagent = "Toxin";

    [DataField]
    public float ReagentAmount = 5;

    [DataField]
    public float KnockbackDistance = 10;

    [DataField]
    public float KnockbackSpeed = 10;

    [DataField]
    public EntProtoId ParcelPrototype = "WrappedParcelHumanoid";

    [DataField]
    public float FireStacks = 8;
}

[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class BehonkerBeamEffectStateComponent : Component
{
    //Only remove pacification when this beam applied it, not if they were already pacificed.
    [AutoPausedField]
    public TimeSpan? PacificationExpiresAt;

    public bool OwnsPacification;

    [AutoPausedField]
    public TimeSpan? SlipperyExpiresAt;

    public bool OwnsSlippery;

    [AutoPausedField]
    public TimeSpan? NextShockAt;

    [AutoPausedField]
    public TimeSpan? StopRepeatingShockAt;

    public TimeSpan RepeatShockInterval;

    public EntityUid? ShockSource;

    public int ShockDamage;

    public TimeSpan ShockStunDuration;
}
