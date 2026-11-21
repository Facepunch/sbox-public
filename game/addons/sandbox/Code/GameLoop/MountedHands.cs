/// <summary>
/// Gives mounted Source weapons sandbox's first-person arms instead of their own gloves. The Source mount declares
/// the console variable, and it only exists while the mount is mounted, so it's set by name whenever it appears.
/// </summary>
internal sealed class MountedHandsSystem : GameObjectSystem<MountedHandsSystem>
{
	const string Variable = "source1_viewmodel_hands";
	const string Arms = "weapons/shared/first_person/v_first_person_arms_human.vmdl";

	bool _set;
	RealTimeSince _checked;

	public MountedHandsSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.StartUpdate, 0, Update, "MountedHands" );
	}

	void Update()
	{
		if ( Scene != Game.ActiveScene || _checked < 1 ) return;
		_checked = 0;

		var exists = ConsoleSystem.GetValue( Variable ) is not null;
		if ( exists && !_set ) ConsoleSystem.SetValue( Variable, Arms );
		_set = exists;
	}
}
