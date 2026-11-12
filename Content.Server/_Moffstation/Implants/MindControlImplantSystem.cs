using Content.Server._Moffstation.Implants.Components;
using Content.Server.Chat.Managers;
using Content.Server.Mind;
using Content.Server.Objectives;
using Content.Shared.Chat;
using Content.Shared.IdentityManagement;
using Content.Shared.Implants;
using Content.Shared.Implants.Components;
using Content.Shared.Mind;
using Content.Shared.Mindshield;
using Content.Shared.Mindshield.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Objectives.Components;
using Content.Shared.Popups;
using Robust.Shared.Player;

namespace Content.Server._Moffstation.Implants;

public sealed class MindControlImplantSystem : EntitySystem
{
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedSubdermalImplantSystem _implants = default!;
    [Dependency] private MindShieldSystem _mindShield = default!;
    [Dependency] private MindSystem _minds = default!;
    [Dependency] private ObjectivesSystem _objectives = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private IChatManager _chat = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MobStateComponent, AddImplantAttemptEvent>(OnImplantAttempt);
        SubscribeLocalEvent<MindControlImplantComponent, ImplantImplantedEvent>(
            OnImplanted,
            after: [typeof(MindShieldSystem)]);
        SubscribeLocalEvent<MindControlImplantComponent, ImplantRemovedEvent>(OnRemoved);
        SubscribeLocalEvent<SubdermalImplantComponent, ImplantImplantedEvent>(
            OnMindShieldImplanted,
            after: [typeof(MindShieldSystem)]);
    }

    private void OnImplantAttempt(Entity<MobStateComponent> target, ref AddImplantAttemptEvent args)
    {
        if (!TryComp<MindControlImplantComponent>(args.Implant, out var implant))
            return;

        implant.Leader = null;
        _mindShield.GetMindshieldStatus(target.Owner, out var mindshielded, out _);
        if (mindshielded)
        {
            args.Cancel(); //You just wasted your implanter silly (if they're mindshielded).
            return;
        }

        implant.Leader = args.User; //Woah guys the person holding the implanter is the leader.
    }

    private void OnImplanted(Entity<MindControlImplantComponent> ent, ref ImplantImplantedEvent args)
    {
        if (ent.Comp.Leader is not { } leader)
            return;

        var obligation = EnsureComp<MindControlObligationComponent>(args.Implanted);
        obligation.Leader = leader;
        obligation.Implant = ent.Owner;

        NotifyPrivately(args.Implanted, Loc.GetString("mind-control-implanted-target", ("leader", Identity.Entity(leader, EntityManager))));
        NotifyPrivately(leader, Loc.GetString("mind-control-implanted-leader", ("target", Identity.Entity(args.Implanted, EntityManager)))); //In retrospect the user may not need a notification but it w/e.

        if (!_minds.TryGetMind(args.Implanted, out var mindId, out var mind))
            return;

        if (_objectives.TryCreateObjective(mindId, mind, "MindControlFollowLeaderObjective") is not { } objective)
        {
            Log.Error($"Failed to create a mind-control obligation objective for {ToPrettyString(args.Implanted)}."); //If somehow my code means things fail horribly
            return;
        }

        _metaData.SetEntityDescription(objective, Loc.GetString("mind-control-follow-leader-description", ("leader", Identity.Entity(leader, EntityManager))));
        _minds.AddObjective(mindId, mind, objective);
        obligation.Mind = mindId;
        obligation.Objective = objective;
    }

    private void OnRemoved(Entity<MindControlImplantComponent> ent, ref ImplantRemovedEvent args) //What happens when the implanter is removed (rly only useful for admins atm)
    {
        if (!TryComp<MindControlObligationComponent>(args.Implanted, out var obligation)
            || obligation.Implant != ent.Owner)
            return;

        RemoveObligation(obligation);
        RemCompDeferred<MindControlObligationComponent>(args.Implanted);
    }

    private void OnMindShieldImplanted(Entity<SubdermalImplantComponent> implant, ref ImplantImplantedEvent args)
    {
        if (!HasComp<MindShieldImplantComponent>(implant.Owner))
            return;

        RemoveObligationIfMindshielded(args.Implanted);
    }

    private void RemoveObligationIfMindshielded(EntityUid target)
    {
        _mindShield.GetMindshieldStatus(target, out var mindshielded, out _);
        if (!mindshielded || !TryComp<MindControlObligationComponent>(target, out var obligation))
            return;

        if (TryComp<ImplantedComponent>(target, out var implanted))
            _implants.ForceRemove((target, implanted), obligation.Implant);

        RemoveObligation(obligation);
        RemCompDeferred<MindControlObligationComponent>(target);
    }

    private void NotifyPrivately(EntityUid recipient, string message)
    {
        //Give em their popup so they know things were successful.
        _popup.PopupEntity(message, recipient, recipient);

        if (!TryComp<ActorComponent>(recipient, out var actor))
            return;

        var wrapped = Loc.GetString("chat-manager-server-wrap-message", ("message", message));
        _chat.ChatMessageToOne(ChatChannel.Server, message, wrapped, recipient, false, actor.PlayerSession.Channel);
    }

    private void RemoveObligation(MindControlObligationComponent obligation) //Remove obj when mind control gone
    {
        if (obligation.Mind is not { } mindId ||
            obligation.Objective is not { } objective ||
            !TryComp<MindComponent>(mindId, out var mind))
            return;

        var index = mind.Objectives.IndexOf(objective);
        if (index >= 0)
            _minds.TryRemoveObjective(mindId, mind, index);
    }
}
