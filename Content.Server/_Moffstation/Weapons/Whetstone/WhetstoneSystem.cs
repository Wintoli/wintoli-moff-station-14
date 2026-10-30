using Content.Shared._Moffstation.Weapons.Whetstone;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Tools.Systems;
using Content.Shared.Weapons.Melee;

namespace Content.Server._Moffstation.Weapons.Whetstone;

public sealed partial class WhetstoneSystem : EntitySystem
{
    private const string SlicingQuality = "Slicing";

    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private SharedToolSystem _toolSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MeleeWeaponComponent, InteractUsingEvent>(OnMeleeInteractUsing);
        SubscribeLocalEvent<DamageOtherOnHitComponent, InteractUsingEvent>(OnThrowInteractUsing);
        SubscribeLocalEvent<MeleeWeaponComponent, WhetstoneDoAfterEvent>(OnSharpenDoAfter);
        SubscribeLocalEvent<DamageOtherOnHitComponent, WhetstoneDoAfterEvent>(OnSharpenDoAfter);
    }

    private void OnMeleeInteractUsing(Entity<MeleeWeaponComponent> target, ref InteractUsingEvent args) =>
        TryStartSharpening(target.Owner, ref args);

    private void OnThrowInteractUsing(Entity<DamageOtherOnHitComponent> target, ref InteractUsingEvent args) =>
        TryStartSharpening(target.Owner, ref args);

    private void TryStartSharpening(EntityUid target, ref InteractUsingEvent args)
    {
        if (args.Handled || !HasComp<WhetstoneComponent>(args.Used))
            return;

        args.Handled = true;

        if (HasComp<SharpenedComponent>(target))
        {
            _popup.PopupEntity(Loc.GetString("moffstation-whetstone-already-sharpened"), target, args.User);
            return;
        }

        if (!CanSharpen(target))
        {
            _popup.PopupEntity(Loc.GetString("moffstation-whetstone-invalid-target"), target, args.User);
            return;
        }

        var doAfterArgs = new DoAfterArgs(
            EntityManager,
            args.User,
            TimeSpan.FromSeconds(2),
            new WhetstoneDoAfterEvent(),
            target,
            target,
            args.Used)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
            DuplicateCondition = DuplicateConditions.SameTool,
        };

        _doAfter.TryStartDoAfter(doAfterArgs);
    }

    private void OnSharpenDoAfter(Entity<MeleeWeaponComponent> target, ref WhetstoneDoAfterEvent args) =>
        CompleteSharpening(target.Owner, ref args);

    private void OnSharpenDoAfter(Entity<DamageOtherOnHitComponent> target, ref WhetstoneDoAfterEvent args) =>
        CompleteSharpening(target.Owner, ref args);

    private void CompleteSharpening(EntityUid target, ref WhetstoneDoAfterEvent args)
    {
        if (args.Cancelled ||
            args.Used is not { } used ||
            !HasComp<WhetstoneComponent>(used))
            return;

        if (HasComp<SharpenedComponent>(target))
            return;

        if (!CanSharpen(target))
        {
            _popup.PopupEntity(Loc.GetString("moffstation-whetstone-invalid-target"), target, args.User);
            return;
        }

        if (TryComp<MeleeWeaponComponent>(target, out var melee))
        {
            SharpenDamage(melee.Damage);
            DirtyField(target, melee, nameof(MeleeWeaponComponent.Damage));
        }

        if (TryComp<DamageOtherOnHitComponent>(target, out var thrown))
            SharpenDamage(thrown.Damage);

        _toolSystem.TryMultiplySpeedModifier(target, SlicingQuality, 1.2f);

        EnsureComp<SharpenedComponent>(target);
        DullWhetstone(used);
        _popup.PopupEntity(
            Loc.GetString("moffstation-whetstone-success", ("item", Name(target))),
            target,
            args.User);
    }

    private void DullWhetstone(EntityUid whetstone)
    {
        _metaData.SetEntityName(whetstone, Loc.GetString("moffstation-dull-whetstone-name"));
        _metaData.SetEntityDescription(whetstone, Loc.GetString("moffstation-dull-whetstone-desc"));
        RemComp<WhetstoneComponent>(whetstone);
    }

    private bool CanSharpen(EntityUid target) =>
        TryComp<MeleeWeaponComponent>(target, out var melee) && HasSharpenableDamage(melee.Damage) ||
        TryComp<DamageOtherOnHitComponent>(target, out var thrown) && HasSharpenableDamage(thrown.Damage);

    private static bool HasSharpenableDamage(DamageSpecifier damage) =>
        (damage.DamageDict.TryGetValue("Slash", out var slashDamage) && slashDamage > FixedPoint2.Zero) ||
        (damage.DamageDict.TryGetValue("Piercing", out var piercingDamage) && piercingDamage > FixedPoint2.Zero);

    private static void SharpenDamage(DamageSpecifier damage)
    {
        if (damage.DamageDict.TryGetValue("Slash", out var slashDamage) && slashDamage > FixedPoint2.Zero)
            damage.DamageDict["Slash"] += FixedPoint2.New(4);

        if (damage.DamageDict.TryGetValue("Piercing", out var piercingDamage) && piercingDamage > FixedPoint2.Zero)
            damage.DamageDict["Piercing"] += FixedPoint2.New(4);
    }
}
