namespace Sandbox;

/// <summary>
/// Implements logic for the SoundScape system
/// </summary>
[Expose]
sealed class SceneSoundscapeSystem : GameObjectSystem<SceneSoundscapeSystem>
{
	public SceneSoundscapeSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.UpdateBones, 0, Update, "TickSoundScapes" );
	}

	SoundscapeTrigger active;
	RealTimeSince timeSinceUpdate;

	void Update()
	{
		using var _ = PerformanceStats.Timings.Audio.Scope();

		if ( Scene.IsEditor )
			return;

		if ( timeSinceUpdate < 0.2f )
			return;

		timeSinceUpdate = 0;

		var head = Scene.GetPrimaryListenerTransform();

		// Find the closest soundscape, sphere and box take priority over point.
		SoundscapeTrigger best = null;
		(int, float) bestKey = default;

		foreach ( var trigger in Scene.Query<SoundscapeTrigger>() )
		{
			if ( !trigger.TestListenerPosition( head.Position ) ) continue;

			var key = (trigger.Type == SoundscapeTrigger.TriggerType.Point ? 1 : 0, trigger.WorldPosition.DistanceSquared( head.Position ));
			if ( best is not null && key.CompareTo( bestKey ) >= 0 ) continue;

			best = trigger;
			bestKey = key;
		}

		if ( best == active )
			return;

		if ( best == null && active.StayActiveOnExit )
			return;

		if ( active is not null )
			active.Playing = false;

		active = best;

		if ( active is not null )
			active.Playing = true;


	}
}
