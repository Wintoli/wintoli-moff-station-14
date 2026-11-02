using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos.Components;
using Content.Shared.Bed.Sleep;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Cluwne;
using Content.Shared.CombatMode.Pacification;
using Content.Shared.Electrocution;
using Content.Shared.Effects;
using Content.Shared.FixedPoint;
using Content.Shared.Movement.Systems;
using Content.Shared.ParcelWrap.Components;
using Content.Shared.Slippery;
using Content.Shared.StatusEffectNew;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Hitscan.Events;
using Content.Shared._ES.Sparks;
using Robust.Shared.Containers;
using Robust.Shared.Player;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Moffstation.Weapons;

public sealed partial class BehonkerBeamEffectSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedColorFlashEffectSystem _colorFlash = default!;
    [Dependency] private SharedElectrocutionSystem _electrocution = default!;
    [Dependency] private FlammableSystem _flammable = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private SlipperySystem _slippery = default!;
    [Dependency] private StatusEffectsSystem _statusEffects = default!;
    [Dependency] private MovementModStatusSystem _movement = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private ThrowingSystem _throwing = default!;
    [Dependency] private ESSparksSystem _sparks = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BehonkerBeamEffectComponent, HitscanRaycastFiredEvent>(OnHitscanFired);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<BehonkerBeamEffectStateComponent>();
        while (query.MoveNext(out var uid, out var timed))
        {
            if (timed.PacificationExpiresAt is { } pacificationEnd && now >= pacificationEnd)
            {
                if (timed.OwnsPacification)
                    RemComp<PacifiedComponent>(uid);

                timed.PacificationExpiresAt = null;
                timed.OwnsPacification = false;
            }

            if (timed.SlipperyExpiresAt is { } slipperyEnd && now >= slipperyEnd)
            {
                if (timed.OwnsSlippery)
                    RemComp<SlipperyComponent>(uid);

                timed.SlipperyExpiresAt = null;
                timed.OwnsSlippery = false;
            }

            if (timed.StopRepeatingShockAt is { } shockEnd && now >= shockEnd)
            {
                timed.StopRepeatingShockAt = null;
                timed.NextShockAt = null;
                timed.ShockSource = null;
            }
            else if (timed.NextShockAt is { } nextShock && now >= nextShock)
            {
                if (timed.RepeatShockInterval > TimeSpan.Zero)
                {
                    if (timed.ShockSource is { } source && !Deleted(source))
                        ApplyShock(uid, source, timed.ShockDamage, timed.ShockStunDuration);

                    timed.NextShockAt = nextShock + timed.RepeatShockInterval;
                }
                else
                {
                    timed.NextShockAt = null;
                    timed.StopRepeatingShockAt = null;
                }
            }

            if (timed.PacificationExpiresAt == null &&
                timed.SlipperyExpiresAt == null &&
                timed.StopRepeatingShockAt == null)
                RemCompDeferred<BehonkerBeamEffectStateComponent>(uid);
        }
    }

    private void OnHitscanFired(Entity<BehonkerBeamEffectComponent> beam, ref HitscanRaycastFiredEvent args)
    {
        if (args.Data.HitEntity is not { } target || Deleted(target))
            return;

        if (beam.Comp.HitFeedbackColor is { } color)
        {
            _colorFlash.RaiseEffect(color, [target], Filter.Pvs(target, entityManager: EntityManager));
        }

        var source = args.Data.Shooter ?? args.Data.Gun;
        switch (beam.Comp.Effect)
        {
            case BehonkerBeamEffect.Pacify:
                Pacify(target, beam.Comp.EffectDuration);
                break;
            case BehonkerBeamEffect.RepeatedShock:
                Shock(target, source, beam.Comp);
                break;
            case BehonkerBeamEffect.SlipAndClumsy:
                SlipAndClumsy(target, beam.Comp.EffectDuration);
                break;
            case BehonkerBeamEffect.Slow:
                _movement.TryAddMovementSpeedModDuration(
                    target,
                    MovementModStatusSystem.TaserSlowdown,
                    beam.Comp.EffectDuration,
                    beam.Comp.SpeedModifier
                );
                break;
            case BehonkerBeamEffect.Poison:
                Poison(target, beam.Comp.Reagent, beam.Comp.ReagentAmount);
                break;
            case BehonkerBeamEffect.Knockback:
                Knockback(
                    target,
                    args.Data.ShotDirection,
                    beam.Comp.KnockbackDistance,
                    beam.Comp.KnockbackSpeed,
                    source
                );
                break;
            case BehonkerBeamEffect.Sleep:
                _statusEffects.TryAddStatusEffectDuration(
                    target,
                    SleepingSystem.StatusEffectForcedSleeping,
                    beam.Comp.EffectDuration
                );
                break;
            case BehonkerBeamEffect.ParcelWrap:
                WrapTarget(target, beam.Comp.ParcelPrototype);
                break;
            case BehonkerBeamEffect.FireStacks:
                _flammable.AdjustFireStacks(target, beam.Comp.FireStacks, ignite: true);
                break;
            case BehonkerBeamEffect.Cluwne:
                EnsureComp<CluwneComponent>(target);
                break;
        }
    }

    private void Pacify(EntityUid target, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
            return;

        var hadPacification = HasComp<PacifiedComponent>(target);
        if (!hadPacification)
            EnsureComp<PacifiedComponent>(target);

        var timed = EnsureComp<BehonkerBeamEffectStateComponent>(target);
        if (hadPacification && !timed.OwnsPacification)
            return;

        timed.OwnsPacification = true;
        var expiresAt = _timing.CurTime + duration;
        if (timed.PacificationExpiresAt is not { } existing || expiresAt > existing)
            timed.PacificationExpiresAt = expiresAt;
    }

    private void Shock(EntityUid target, EntityUid source, BehonkerBeamEffectComponent beam)
    {
        _sparks.DoSparks(target, user: source, cooldown: false);
        _electrocution.TryDoElectrocution(
            target,
            null,
            beam.ShockDamage,
            beam.ShockStunDuration,
            refresh: true
        );

        if (beam.EffectDuration <= TimeSpan.Zero || beam.RepeatShockInterval <= TimeSpan.Zero)
            return;

        var timed = EnsureComp<BehonkerBeamEffectStateComponent>(target);
        var now = _timing.CurTime;
        var end = now + beam.EffectDuration;
        if (timed.StopRepeatingShockAt is not { } currentEnd || end > currentEnd)
        {
            timed.StopRepeatingShockAt = end;
            timed.NextShockAt = now + beam.RepeatShockInterval;
        }

        timed.RepeatShockInterval = beam.RepeatShockInterval;
        timed.ShockSource = source;
        timed.ShockDamage = beam.ShockDamage;
        timed.ShockStunDuration = beam.ShockStunDuration;
    }

    private void ApplyShock(EntityUid target, EntityUid source, int damage, TimeSpan stunDuration) //Silly workaround to not touch upstream files for shock vfx
    {
        if (!_electrocution.TryDoElectrocution(target, null, damage, stunDuration, refresh: true))
            return;

        _sparks.DoSparks(target, user: source);
    }

    private void SlipAndClumsy(EntityUid target, TimeSpan duration)
    {
        var hadSlippery = HasComp<SlipperyComponent>(target);
        var slippery = EnsureComp<SlipperyComponent>(target);

        _slippery.TrySlip(target, slippery, target, false);

        if (duration > TimeSpan.Zero && (!hadSlippery ||
            TryComp<BehonkerBeamEffectStateComponent>(target, out var existingTimed) && existingTimed.OwnsSlippery))
        {
            var timed = EnsureComp<BehonkerBeamEffectStateComponent>(target);
            timed.OwnsSlippery = true;
            var expiresAt = _timing.CurTime + duration;
            if (timed.SlipperyExpiresAt is not { } currentEnd || expiresAt > currentEnd)
                timed.SlipperyExpiresAt = expiresAt;
        }

        _statusEffects.TryAddStatusEffectDuration(target, "StatusEffectClumsyCluwne", duration);
    }

    private void Poison(EntityUid target, string reagent, float amount)
    {
        if (amount <= 0 || !TryComp<BloodstreamComponent>(target, out var bloodstream) ||
            !_solutions.ResolveSolution(target, bloodstream.BloodSolutionName, ref bloodstream.BloodSolution))
            return;

        _solutions.TryAddReagent(bloodstream.BloodSolution.Value, reagent, FixedPoint2.New(amount));
    }

    private void Knockback(
        EntityUid target,
        System.Numerics.Vector2 direction,
        float distance,
        float speed,
        EntityUid source
    )
    {
        if (distance <= 0 || speed <= 0 || direction == System.Numerics.Vector2.Zero)
            return;

        _throwing.TryThrow(
            target,
            System.Numerics.Vector2.Normalize(direction) * distance,
            speed,
            user: source,
            pushbackRatio: 0,
            recoil: false
        );
    }

    private void WrapTarget(EntityUid target, EntProtoId parcelPrototype)
    {
        if (!TryComp<PhysicsComponent>(target, out var physics) || physics.BodyType == BodyType.Static ||
            HasComp<WrappedParcelComponent>(target))
            return;

        var transform = Transform(target);
        var parcel = SpawnAtPosition(parcelPrototype, transform.Coordinates);
        var parcelComponent = Comp<WrappedParcelComponent>(parcel);

        if (_containers.TryGetContainingContainer(target, out var outerContainer))
        {
            _containers.Remove(target, outerContainer);
            _containers.InsertOrDrop(parcel, outerContainer);
        }

        if (!_containers.Insert(target, parcelComponent.Contents))
            PredictedDel(parcel);
    }
}
