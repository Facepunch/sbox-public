//
// This is a script. Addons can't ship C# from a Code folder, so their logic lives in .csx files
// inside Assets. Each one is plain C#, and the common namespaces (Sandbox, System.Linq and so on)
// are already imported.
//
// pet_melon.prefab has this component on it, found by its class name. If you rename the class,
// update the component in the prefab too. pet_melon.sent is what puts the prefab in the spawn menu.
//

/// <summary>
/// A melon that rolls after the nearest player, and waits when it gets close.
/// </summary>
public sealed class PetMelon : Component
{
	/// <summary>
	/// The physics body we push around. [RequireComponent] finds the Rigidbody on this GameObject,
	/// and adds one if there isn't one.
	/// </summary>
	[RequireComponent] public Rigidbody Body { get; set; }

	/// <summary>
	/// How fast it moves towards the player, in units per second. [Property] shows this in the
	/// inspector and saves it in the prefab.
	/// </summary>
	[Property] public float Speed { get; set; } = 70f;

	/// <summary>
	/// It stops chasing when the player is this close, so it doesn't crowd them.
	/// </summary>
	[Property] public float StopDistance { get; set; } = 256f;

	/// <summary>
	/// Seconds since we last looked for a player. A TimeSince counts up by itself.
	/// </summary>
	TimeSince _timeSinceLastCheck;

	/// <summary>
	/// The player we're following. Null when nobody is around.
	/// </summary>
	PlayerController _targetPlayer;

	/// <summary>
	/// Called every physics step. Anything that moves a physics body belongs here rather than in OnUpdate.
	/// </summary>
	protected override void OnFixedUpdate()
	{
		// The melon is networked. Only the machine simulating it should move it, everyone else
		// is sent its position.
		if ( IsProxy ) return;

		// Searching the scene every step is wasteful. Once a second is plenty.
		if ( _timeSinceLastCheck > 1f )
		{
			_timeSinceLastCheck = 0f;
			_targetPlayer = FindClosestPlayer();
		}

		// IsValid is false for null and for anything that has been destroyed, like a player who left.
		if ( !_targetPlayer.IsValid() ) return;

		// The offset from us to the player. Its length is how far away they are.
		var offset = _targetPlayer.WorldPosition - Body.WorldPosition;
		if ( offset.Length < StopDistance ) return;

		// Normal is the same direction with a length of 1, so multiplying by Speed gives a steady pace.
		var direction = offset.Normal;
		Body.Velocity = direction * Speed;

		// Spin it too, so it looks like it's rolling instead of sliding.
		Body.AngularVelocity = new Vector3( direction.Dot( Vector3.Right ), 0f, direction.Dot( Vector3.Up ) ) * Speed * 0.25f;
	}

	/// <summary>
	/// The player nearest to the melon, or null if there are no players.
	/// </summary>
	PlayerController FindClosestPlayer()
	{
		// DistanceSquared sorts the same as Distance and is cheaper to work out.
		return Scene.GetAllComponents<PlayerController>()
			.OrderBy( player => player.WorldPosition.DistanceSquared( Body.WorldPosition ) )
			.FirstOrDefault();
	}
}
