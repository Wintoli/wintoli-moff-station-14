using System.Linq;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Administration.Logs;
using Content.Shared.Charges.Components;
using Content.Shared.Charges.Systems;
using Content.Shared.Database;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.Wires;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;

namespace Content.Shared._Moffstation.JestographicSequencer;

///<summary>
///Handles using the jestographic sequencer on things with an "AccessReaderComponent",
///swapping their granted accesses for denied ones and vice versa.
///It can also be used on cyborgs, replacing their laws with the sequencer's lawset (see SharedSiliconLawSystem.Jestographic).
///</summary>
public sealed class JestographicSequencerSystem : EntitySystem
{
    [Dependency] private readonly ISharedAdminLogManager _adminLogger = default!;

    [Dependency] private readonly AccessReaderSystem _accessReader = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedChargesSystem _charges = default!;
    [Dependency] private readonly SharedDoorSystem _door = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<JestographicSequencerComponent, AfterInteractEvent>(OnAfterInteract);
    }

    private void OnAfterInteract(Entity<JestographicSequencerComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target)
            return;

        //Borgs get their laws swapped, but a closed panel gets its access swapped.
        var isBorg = HasComp<BorgBrainComponent>(target)
                     || HasComp<BorgChassisComponent>(target)
                     && TryComp<WiresPanelComponent>(target, out var panel)
                     && panel.Open;
        AccessReaderComponent? reader = null;
        Entity<AccessReaderComponent>? readerEnt = null;
        if (!isBorg)
        {
            if (!_accessReader.GetMainAccessReader(target, out readerEnt))
                return;

            reader = readerEnt.Value.Comp;
        }

        if (_charges.IsEmpty(ent.Owner))
        {
            _popup.PopupEntity(Loc.GetString("jestographic-sequencer-no-charges"), args.User, args.User);
            return;
        }

        if (isBorg)
        {
            //You've just been jested. A charge is only spent if it succeeds.
            var jested = new GotJestedEvent(args.User, ent.Comp.Lawset);
            RaiseLocalEvent(target, ref jested);
            if (!jested.Handled)
                return;

            _charges.TryUseCharge(ent.Owner);
            _audio.PlayPredicted(ent.Comp.ReverseSound, target, args.User);
            _adminLogger.Add(LogType.Emag,
                LogImpact.High,
                $"{ToPrettyString(args.User):player} jested {ToPrettyString(target):target} using {ToPrettyString(ent):used}");

            args.Handled = true;
            return;
        }

        if (reader == null || readerEnt == null)
            return;

        //Empty access lists allow everyone, so can't be reversed. Door is bolted instead.
        if (reader.AccessLists.Count == 0 && reader.DenyTags.Count == 0)
        {
            if (!TryComp<DoorBoltComponent>(target, out var bolt)
                || !_door.TrySetBoltDown((target, bolt), true, args.User, true))
            {
                _popup.PopupEntity(
                    Loc.GetString("jestographic-sequencer-no-access", ("target", Identity.Entity(target, EntityManager))),
                    args.User,
                    args.User);
                return;
            }

            _charges.TryUseCharge(ent.Owner);
            _audio.PlayPredicted(ent.Comp.ReverseSound, target, args.User);
            _popup.PopupEntity(
                Loc.GetString("jestographic-sequencer-bolted", ("target", Identity.Entity(target, EntityManager))),
                args.User,
                args.User,
                PopupType.Medium);

            _adminLogger.Add(LogType.Emag,
                LogImpact.High,
                $"{ToPrettyString(args.User):player} bolted {ToPrettyString(target):target} shut using {ToPrettyString(ent):used}");

            args.Handled = true;
            return;
        }

        //Everything that used to grant access now denies it
        var newDenyTags = reader.AccessLists.SelectMany(x => x).ToHashSet();

        //Everything that used to deny access now grants it.
        var newAccessLists = new List<HashSet<ProtoId<AccessLevelPrototype>>>();
        foreach (var denyTag in reader.DenyTags)
        {
            newAccessLists.Add([denyTag]);
        }

        //Cause this was a pain to figure out, when swapping, the access list becoming empty and the deny list filling would let people in with no or blank IDs, defeating the purpose of the item 
        //since people could just take off their ID to get into places. While techincally not 100% swapping all access, this makes sure that any door it is used on still requires an ID to use. 
        //However if the door had no accesses before, it will still default to bolting w/ no change at all.
        //This introduces a rare scenario where someone with an entirely blank ID could somehow not gain access to a room/item, but I think it is worth it.  
        if (newAccessLists.Count == 0)
        {
            foreach (var level in _prototype.EnumeratePrototypes<AccessLevelPrototype>())
            {
                newAccessLists.Add([level.ID]);
            }
        }

        _accessReader.SetDenyTags(readerEnt.Value, newDenyTags);
        _accessReader.TrySetAccesses(readerEnt.Value, newAccessLists);

        _charges.TryUseCharge(ent.Owner);

        _audio.PlayPredicted(ent.Comp.ReverseSound, target, args.User);
        _popup.PopupEntity(
            Loc.GetString("jestographic-sequencer-success", ("target", Identity.Entity(target, EntityManager))),
            args.User,
            args.User,
            PopupType.Medium);

        _adminLogger.Add(LogType.Emag,
            LogImpact.High,
            $"{ToPrettyString(args.User):player} reversed the accesses on {ToPrettyString(target):target} using {ToPrettyString(ent):used}");

        args.Handled = true;
    }
}
