using Content.Shared.Examine;

namespace Content.Shared._Moffstation.Weapons.Whetstone;

public sealed class SharpenedSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SharpenedComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(Entity<SharpenedComponent> entity, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("moffstation-whetstone-sharp-examine"));
    }
}
