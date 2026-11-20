// 
// This is used for adding a camera kick effect when weapon is being used. This isn't the same as actual weapon aim recoil, this is just a purely visual effect for the player
//
public partial class BaseSandboxWeapon
{
	/// <summary>Random pitch added to the aim per attack (min, max degrees).</summary>
	[Property, Group( "Recoil" )] public Vector2 RecoilPitch { get; set; } = new( -0.3f, -0.1f );

	/// <summary>Random yaw added to the aim per attack (min, max degrees).</summary>
	[Property, Group( "Recoil" )] public Vector2 RecoilYaw { get; set; } = new( -0.1f, 0.1f );

	/// <summary>Random pitch of the first-person view punch per attack (min, max degrees).</summary>
	[Property, Group( "Camera" )] public Vector2 PunchPitch { get; set; } = new( 0f, 0f );

	/// <summary>Random yaw of the first-person view punch per attack (min, max degrees).</summary>
	[Property, Group( "Camera" )] public Vector2 PunchYaw { get; set; } = new( 0f, 0f );

	/// <summary>Random roll of the first-person view punch per attack (min, max degrees).</summary>
	[Property, Group( "Camera" )] public Vector2 PunchRoll { get; set; } = new( 0.5f, 0.5f );

	/// <summary>How many times the view punch oscillates before it lapses.</summary>
	[Property, Group( "Camera" )] public float PunchFrequency { get; set; } = 2f;

	/// <summary>How long the view punch lasts, in seconds.</summary>
	[Property, Group( "Camera" )] public float PunchDuration { get; set; } = 0.3f;

	/// <summary>First-person camera shake strength per attack, in world units. Zero for none.</summary>
	[Property, Group( "Camera" )] public float ShakeAmplitude { get; set; } = 0f;

	/// <summary>New shake offsets per second.</summary>
	[Property, Group( "Camera" )] public float ShakeFrequency { get; set; } = 40f;

	/// <summary>How long the shake takes to settle, in seconds.</summary>
	[Property, Group( "Camera" )] public float ShakeDuration { get; set; } = 0.3f;

	/// <summary>
	/// Kicks the owner's aim, then punches and shakes their view if they're the local player in first person.
	/// Returns whether the view was kicked.
	/// </summary>
	protected bool KickView()
	{
		if ( !HasOwner ) return false;

		Owner.Controller.EyeAngles += new Angles( Random.Shared.Float( RecoilPitch.x, RecoilPitch.y ), Random.Shared.Float( RecoilYaw.x, RecoilYaw.y ), 0 );

		if ( Owner.Controller.ThirdPerson || !Owner.IsLocalPlayer ) return false;

		var punch = new Angles( Random.Shared.Float( PunchPitch.x, PunchPitch.y ), Random.Shared.Float( PunchYaw.x, PunchYaw.y ), Random.Shared.Float( PunchRoll.x, PunchRoll.y ) );
		Scene.Camera?.AddPunch( punch, PunchFrequency, PunchDuration );

		if ( ShakeAmplitude > 0f )
			Scene.Camera?.AddShake( ShakeAmplitude, ShakeFrequency, ShakeDuration );

		return true;
	}
}
