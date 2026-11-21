using System.Text.Json.Serialization;

/// <summary>
/// A configurable input for contraptions driven from a seat. This contains no state:
/// it reads the active driver's connection while <see cref="IPlayerControllable.OnControl"/>
/// is running. Wired inputs are ordinary <see cref="SignalInputAttribute"/> handlers instead.
/// <para>
/// Bound to a raw <see cref="Key"/>, which <see cref="ControlSystem"/> has the driver report. Saves from before
/// raw keys are bound to an input <see cref="Action"/> instead, which still works.
/// </para>
/// </summary>
public struct ClientInput
{
	/// <summary>
	/// The key, or <c>+</c>-joined combo, by the names <see cref="Input.Keyboard.Down"/> takes.
	/// </summary>
	[Property]
	public string Key { get; set; }

	/// <summary>
	/// The input action this was bound to before raw keys. Only used when <see cref="Key"/> is empty.
	/// </summary>
	[Property]
	public string Action { get; set; }

	[JsonIgnore, Hide]
	public readonly bool HasKey => !string.IsNullOrWhiteSpace( Key );

	[JsonIgnore, Hide]
	public readonly bool IsEnabled => HasKey || !string.IsNullOrWhiteSpace( Action );

	/// <summary>
	/// The individual keys of <see cref="Key"/>. Split once per distinct binding and shared - don't modify it.
	/// </summary>
	[JsonIgnore, Hide]
	public readonly string[] Keys
	{
		get
		{
			if ( !HasKey ) return [];

			if ( !_splitKeys.TryGetValue( Key, out var keys ) )
			{
				keys = Key.Split( '+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries );
				_splitKeys[Key] = keys;
			}

			return keys;
		}
	}

	/// <summary>
	/// Bindings are a handful of short strings, so this never gets big.
	/// </summary>
	private static readonly Dictionary<string, string[]> _splitKeys = new();

	public readonly bool Down() => HasKey
		? ControlSystem.Current?.KeysDown( ControlContext.Connection, Keys ) == true
		: IsEnabled && ControlContext.Connection?.Down( Action ) == true;

	public readonly bool Pressed() => HasKey
		? ControlSystem.Current?.KeysPressed( ControlContext.Connection, Keys ) == true
		: IsEnabled && ControlContext.Connection?.Pressed( Action ) == true;

	public readonly bool Released() => HasKey
		? ControlSystem.Current?.KeysReleased( ControlContext.Connection, Keys ) == true
		: IsEnabled && ControlContext.Connection?.Released( Action ) == true;

	/// <summary>
	/// How far it's pushed, 0 to 1: a key's all the way, a gamepad's trigger or stick on an action anything between
	/// (see <see cref="Player.ActionAmounts"/>).
	/// </summary>
	public readonly float GetAnalog()
	{
		if ( !Down() ) return 0f;
		if ( HasKey ) return 1f;

		var amounts = ControlContext.Player?.ActionAmounts;
		return amounts is not null && amounts.TryGetValue( Action, out var amount ) ? amount : 1f;
	}

	/// <summary>
	/// Held by the local player - for things that run on the client rather than in the host's control tick.
	/// </summary>
	public readonly bool LocalDown()
	{
		if ( !HasKey ) return IsEnabled && Input.Down( Action );

		var keys = Keys;
		if ( keys.Length == 0 ) return false;

		foreach ( var key in keys )
		{
			if ( !Input.Keyboard.Down( key ) ) return false;
		}

		return true;
	}

	/// <summary>
	/// Went down this frame for the local player. See <see cref="LocalDown"/>.
	/// </summary>
	public readonly bool LocalPressed()
	{
		if ( !HasKey ) return IsEnabled && Input.Pressed( Action );
		if ( !LocalDown() ) return false;

		foreach ( var key in Keys )
		{
			if ( Input.Keyboard.Pressed( key ) ) return true;
		}

		return false;
	}
}
