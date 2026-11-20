public sealed partial class FourWheelVehicle : Component.ICollisionListener, IDamageBlame
{
	Player IDamageBlame.Player => Seat?.GetOccupant()?.GetComponent<Player>();

	/// <summary>
	/// Multiplies crash damage to the driver. Zero disables impact damage.
	/// </summary>
	[Property, Group( "Impact Damage" )] public float ImpactDamageScale { get; set; } = 1f;

	/// <summary>Closing speed in inches per second below which impacts are harmless.</summary>
	[Property, Group( "Impact Damage" )] public float MaxSafeImpactSpeed { get; set; } = 350f;

	/// <summary>Closing speed that deals 100 damage at a scale of one.</summary>
	[Property, Group( "Impact Damage" )] public float FatalImpactSpeed { get; set; } = 1100f;

	private TimeSince _timeSinceDriverImpact = 1f;

	/// <summary>
	/// The vehicle hit something, on the host: closing speed in in/s and where. From the body's physics collisions and
	/// from the tyres blocking against obstacles (<see cref="ResolveWheelObstacle"/>), which aren't physics collisions.
	/// </summary>
	public event Action<float, Vector3> Impacted;

	void ICollisionListener.OnCollisionStart( Collision collision )
	{
		var other = collision.Other.GameObject;
		if ( other.IsValid() && (other.Tags.Has( "player" ) || other.Tags.Has( "playercontroller" )) ) return;

		ReportImpact( MathF.Abs( collision.Contact.NormalSpeed ), collision.Contact.Point );
	}

	private void ReportImpact( float speed, Vector3 position )
	{
		DamageDriverFromImpact( speed, position );
		Impacted?.Invoke( speed, position );
	}

	private void DamageDriverFromImpact( float speed, Vector3 position )
	{
		if ( !Networking.IsHost || ImpactDamageScale <= 0 || speed <= MaxSafeImpactSpeed
			|| FatalImpactSpeed <= MaxSafeImpactSpeed || _timeSinceDriverImpact < 0.25f ) return;

		var driver = Seat?.GetOccupant()?.GetComponent<Player>();
		if ( !driver.IsValid() || driver.Health <= 0 ) return;

		var amount = (speed - MaxSafeImpactSpeed) / (FatalImpactSpeed - MaxSafeImpactSpeed) * 100f * ImpactDamageScale;
		if ( amount < 1f ) return;

		_timeSinceDriverImpact = 0;
		var damage = new DamageInfo( amount, driver.GameObject, GameObject ) { Position = position };
		damage.Tags.Add( DamageTags.Crush );
		(driver as IDamageable).OnDamage( damage );
	}
}
