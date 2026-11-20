/// <summary>
/// Record stats for the local player
/// </summary>
internal sealed class PlayerStats : Component, Local.IPlayerEvents
{
	[RequireComponent] public Player Player { get; set; }

	float metersTravelled;
	Vector3 lastPosition;

	protected override void OnFixedUpdate()
	{
		if ( IsProxy ) return;

		var delta = WorldPosition - lastPosition;
		lastPosition = WorldPosition;


		if ( !Player.Controller.IsOnGround )
		{
			return;
		}

		var groundDelta = delta.WithZ( 0 ).Length.InchToMeter();
		if ( groundDelta > 10 ) groundDelta = 0;

		metersTravelled += groundDelta;

		if ( metersTravelled > 10 )
		{
			Sandbox.Services.Stats.Increment( "meters_walked", metersTravelled );
			metersTravelled = 0;
		}

	}

	void Local.IPlayerEvents.OnJump()
	{
		if ( IsProxy ) return;

		Sandbox.Services.Stats.Increment( "jump", 1 );
	}

	void Local.IPlayerEvents.OnDamage( PlayerDamageParams args )
	{
		if ( IsProxy ) return;

		Sandbox.Services.Stats.Increment( "damage_taken", args.Damage );
	}

	void Local.IPlayerEvents.OnDied( PlayerDiedParams args )
	{
		if ( IsProxy ) return;

		Sandbox.Services.Stats.Increment( "deaths", 1 );
	}

	void Local.IPlayerEvents.OnSuicide()
	{
		if ( IsProxy ) return;

		Sandbox.Services.Stats.Increment( "suicides", 1 );
	}

}
