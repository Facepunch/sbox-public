using System.Text.Json.Serialization;

/// <summary>
/// A project action or physical keyboard binding read during the contraption's control tick.
/// </summary>
public struct ClientInput
{
	private string _key;
	private string[] _buttons;
	private string _keyId;

	/// <summary>
	/// The physical buttons to hold together when the captured combination has no project action.
	/// </summary>
	[Property]
	public string Key
	{
		readonly get => _key;
		set
		{
			_buttons = value?.Split( '+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries );
			_key = _buttons is { Length: > 0 } ? string.Join( "+", _buttons ) : null;
			_keyId = _key is null ? null : "key:" + string.Join( "+", _buttons.Order( StringComparer.OrdinalIgnoreCase ) );
		}
	}

	/// <summary>
	/// The project input action bound to this control.
	/// </summary>
	[Property]
	public string Action { get; set; }

	internal readonly string BindingId => _keyId ?? Action;

	/// <summary>
	/// Whether this input is bound to a physical key or key combination.
	/// </summary>
	[JsonIgnore, Hide]
	public readonly bool HasKey => Key is not null;

	/// <summary>
	/// The individual buttons of <see cref="Key"/>, in capture order. Treat the array as read-only.
	/// </summary>
	[JsonIgnore, Hide]
	public readonly string[] Keys => _buttons ?? [];

	/// <summary>
	/// Whether this control has a binding.
	/// </summary>
	[JsonIgnore, Hide]
	public readonly bool IsEnabled => HasKey || !string.IsNullOrWhiteSpace( Action );

	/// <summary>
	/// Whether the driver is holding the bound action.
	/// </summary>
	public readonly bool Down() => Down( ControlContext.Connection );

	/// <summary>
	/// Whether the driver pressed the bound action during this tick.
	/// </summary>
	public readonly bool Pressed() => Pressed( ControlContext.Connection );

	/// <summary>
	/// Whether the driver released the bound action during this tick.
	/// </summary>
	public readonly bool Released() => HasKey
		? ControlContext.Connection?.KeyReleased( _buttons ) == true
		: ControlContext.Connection?.Released( Action ) == true;

	/// <summary>
	/// How far the driver is pressing the action, from zero to one.
	/// </summary>
	public readonly float GetAnalog() => GetAnalog( ControlContext.Player, ControlContext.Connection );

	internal readonly float GetAnalog( Player driver, Connection connection )
	{
		if ( !Down( connection ) ) return 0f;
		if ( HasKey ) return 1f;

		var amounts = driver?.ActionAmounts;
		return amounts is not null && amounts.TryGetValue( Action, out var amount ) ? amount : 1f;
	}

	/// <summary>
	/// Whether the local player is holding the binding.
	/// </summary>
	public readonly bool LocalDown() => Down( Connection.Local );

	/// <summary>
	/// Whether the local player pressed the binding this frame.
	/// </summary>
	public readonly bool LocalPressed() => Pressed( Connection.Local );

	private readonly bool Down( Connection connection ) => HasKey
		? connection?.KeyDown( _buttons ) == true
		: connection?.Down( Action ) == true;

	private readonly bool Pressed( Connection connection ) => HasKey
		? connection?.KeyPressed( _buttons ) == true
		: connection?.Pressed( Action ) == true;
}
