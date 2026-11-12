namespace Content.Server._Moffstation.Implants.Components;

[RegisterComponent]
public sealed partial class MindControlImplantComponent : Component
{
    public EntityUid? Leader;
}

[RegisterComponent]
public sealed partial class MindControlObligationComponent : Component
{
    public EntityUid Leader;
    public EntityUid Implant;
    public EntityUid? Mind;
    public EntityUid? Objective;
}
