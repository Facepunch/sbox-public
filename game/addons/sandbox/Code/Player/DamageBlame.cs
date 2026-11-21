/// <summary>
/// Put this on something that acts for a player, like a vehicle for its driver, so its damage counts as theirs.
/// </summary>
public interface IDamageBlame
{
	/// <summary>
	/// Return null if nobody is responsible right now.
	/// </summary>
	Player Player { get; }
}

/// <summary>
/// Finds the player behind a piece of damage, like the shooter, the driver or whoever threw the prop.
/// Null for the world, NPCs and unowned props.
/// </summary>
public sealed class DamageBlame : GameObjectSystem<DamageBlame>
{
	/// <summary>
	/// How long a prop still counts as thrown after it's let go.
	/// </summary>
	public const float ThrowMemory = 5f;

	sealed class Handle
	{
		public Player Player;
		public RealTimeSince Since;
		public Vector3 Velocity;
	}

	// kept where the body is simulated, since that's where its impact damage happens
	readonly Dictionary<GameObject, Handle> _handled = new();
	readonly List<GameObject> _expired = new();

	public DamageBlame( Scene scene ) : base( scene )
	{
		Listen( Stage.StartFixedUpdate, 0, UpdateHandled, "UpdateDamageBlame" );
	}

	/// <summary>
	/// Call every tick while a player holds or throws something, on the machine that simulates it.
	/// </summary>
	public static void Blame( GameObject go, Player player )
	{
		if ( !go.IsValid() || !player.IsValid() ) return;
		if ( Current is not { } system ) return;

		if ( !system._handled.TryGetValue( go.Root, out var handle ) )
			system._handled[go.Root] = handle = new Handle();

		handle.Player = player;
		handle.Since = 0;
	}

	public static Vector3? GetThrownVelocity( GameObject go )
	{
		if ( !go.IsValid() || Current is not { } system ) return null;
		return system._handled.TryGetValue( go.Root, out var handle ) && handle.Since < ThrowMemory ? handle.Velocity : null;
	}

	public static Player Find( in DamageInfo damage )
	{
		var attacker = damage.Attacker;

		// exploding props pass themselves as the weapon, so a thrown barrel still counts as the thrower's
		if ( FindForObject( attacker ) is { } player ) return player;
		if ( FindForObject( damage.Weapon ) is { } thrower ) return thrower;

		// only for impacts, NPCs have owners too but they act on their own
		if ( attacker.IsValid() && damage.Tags.Contains( DamageTags.Impact ) && attacker.Root.GetComponent<Ownable>()?.Owner is { } owner )
			return Player.FindForConnection( owner );

		return null;
	}

	static Player FindForObject( GameObject go )
	{
		if ( !go.IsValid() ) return null;

		foreach ( var source in go.GetComponentsInParent<IDamageBlame>() )
		{
			if ( source.Player is { } player && player.IsValid() )
				return player;
		}

		return Current?.FindHandled( go );
	}

	Player FindHandled( GameObject go )
	{
		if ( _handled.Count == 0 ) return null;
		if ( FindHandledRoot( go.Root ) is { } player ) return player;

		// the player may be holding another part of the same welded contraption
		if ( !go.Root.Components.GetAll<Rigidbody>().Any() ) return null;

		var bodies = new HashSet<Rigidbody>();
		PhysicsHold.GetConnectedBodies( go, bodies );

		foreach ( var body in bodies )
		{
			if ( FindHandledRoot( body.GameObject.Root ) is { } welder )
				return welder;
		}

		return null;
	}

	Player FindHandledRoot( GameObject root )
	{
		if ( !_handled.TryGetValue( root, out var handled ) ) return null;
		return handled.Since < ThrowMemory && handled.Player.IsValid() ? handled.Player : null;
	}

	void UpdateHandled()
	{
		foreach ( var (go, handled) in _handled )
		{
			if ( !go.IsValid() || !handled.Player.IsValid() || handled.Since > ThrowMemory )
			{
				_expired.Add( go );
				continue;
			}

			handled.Velocity = go.GetComponent<Rigidbody>()?.Velocity ?? default;
		}

		foreach ( var go in _expired )
			_handled.Remove( go );

		_expired.Clear();
	}
}
