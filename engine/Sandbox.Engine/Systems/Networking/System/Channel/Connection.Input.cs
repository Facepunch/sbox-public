using Sandbox.Utility;

namespace Sandbox;

public abstract partial class Connection
{
	/// <summary>
	/// Build the <see cref="UserCommand"/> for this <see cref="Connection"/>.
	/// </summary>
	internal void BuildUserCommand( ref UserCommand cmd )
	{
		Sandbox.Input.CaptureNetworkInput( ref cmd );
		cmd.AimAngles = Game.ActiveScene?.Camera?.WorldRotation.Angles();
		Input.Command.AimAngles = cmd.AimAngles;
		Input.Command.MouseDelta = cmd.MouseDelta;
		Input.Command.MouseWheel = cmd.MouseWheel;
	}

	/// <summary>
	/// World-space view angles from this player's latest user command, or null when unavailable.
	/// </summary>
	public Angles? AimAngles => Input.Command.AimAngles;

	/// <summary>
	/// Horizontal and vertical mouse movement accumulated for the latest user command.
	/// </summary>
	public Vector2 MouseDelta => Input.Command.MouseDelta;

	/// <summary>
	/// Horizontal and vertical mouse wheel movement accumulated for the latest user command.
	/// </summary>
	public Vector2 MouseWheel => Input.Command.MouseWheel;

	/// <summary>
	/// Clear pending pressed and released actions for all connections in the Update context.
	/// </summary>
	internal static void ClearUpdateContextInput()
	{
		if ( Networking.System is not { } network )
		{
			Local?.Input.ClearUpdateContext();
			return;
		}

		foreach ( var entry in network.ConnectionInfo.All )
		{
			Find( entry.Key )?.Input.ClearUpdateContext();
		}
	}

	/// <summary>
	/// Clear pending pressed and released actions for all connections in the Fixed Update context.
	/// </summary>
	internal static void ClearFixedUpdateContextInput()
	{
		if ( Networking.System is not { } network )
		{
			Local?.Input.ClearFixedUpdateContext();
			return;
		}

		foreach ( var entry in network.ConnectionInfo.All )
		{
			Find( entry.Key )?.Input.ClearFixedUpdateContext();
		}
	}

	internal struct InputState
	{
		internal struct Context
		{
			public ulong Pressed;
			public ulong Released;

			internal int KeySamples;
		}

		// Keep recent changes in order so chords that went down and up between updates have both edges.
		// Physics can be paused while commands continue arriving, so bound the history per connection.
		private CircularBuffer<ushort[]> _keyHistory;

		internal readonly bool HasKeyEdge( ReadOnlySpan<string> keys, bool pressed )
		{
			var count = GetCurrentContext().KeySamples;
			if ( count == 0 ) return false;

			var down = ContainsKeys( Command.Keys, keys );
			for ( var i = _keyHistory.Size - 1; i >= _keyHistory.Size - count; i-- )
			{
				var wasDown = ContainsKeys( _keyHistory[i], keys );
				if ( down == pressed && wasDown != down ) return true;
				down = wasDown;
			}

			return false;
		}

		/// <summary>
		/// The newest accepted input sample, received from this connection.
		/// </summary>
		internal UserCommand Command;

		private Context _fixedUpdateContext;
		private Context _updateContext;

		/// <summary>
		/// Returns the input edges accumulated for the active frame or fixed update.
		/// </summary>
		public readonly Context GetCurrentContext()
		{
			var isFixedUpdate = Game.ActiveScene?.IsFixedUpdate ?? false;
			return isFixedUpdate ? _fixedUpdateContext : _updateContext;
		}

		public void ApplyUserCommand( in UserCommand cmd )
		{
			var commandNumberDelta = cmd.CommandNumber - Command.CommandNumber;

			// Drop duplicates or commands that are too far behind (wrap-aware)
			if ( commandNumberDelta is 0 or > 0x7FFFFFFF )
				return;

			var pressed = ~Command.Actions & cmd.Actions;
			var released = Command.Actions & ~cmd.Actions;
			_fixedUpdateContext.Pressed |= pressed;
			_updateContext.Pressed |= pressed;
			_fixedUpdateContext.Released |= released;
			_updateContext.Released |= released;

			ReadOnlySpan<ushort> previousKeys = Command.Keys;
			if ( !previousKeys.SequenceEqual( cmd.Keys ) )
			{
				(_keyHistory ??= new( 64 )).PushBack( Command.Keys );
				_fixedUpdateContext.KeySamples = Math.Min( _fixedUpdateContext.KeySamples + 1, _keyHistory.Size );
				_updateContext.KeySamples = Math.Min( _updateContext.KeySamples + 1, _keyHistory.Size );
			}

			Command = cmd;
		}

		public void ClearFixedUpdateContext()
		{
			_fixedUpdateContext = default;
		}

		public void ClearUpdateContext()
		{
			_updateContext = default;
		}

		public void Clear()
		{
			this = default;
		}
	}

	internal InputState Input;

	/// <summary>
	/// Action is currently pressed down for this <see cref="Connection"/>.
	/// </summary>
	public bool Down( [InputAction] string action )
	{
		// If this connection is us, just use our local input instead.
		if ( Local == this )
			return Sandbox.Input.Down( action );

		if ( string.IsNullOrWhiteSpace( action ) )
			return false;

		var index = Sandbox.Input.GetActionIndex( action );
		if ( index == -1 )
			return false;

		var mask = 1UL << index;
		return (Input.Command.Actions & mask) != 0;
	}

	/// <summary>
	/// Action was pressed for this <see cref="Connection"/> within the current update context.
	/// </summary>
	public bool Pressed( [InputAction] string action )
	{
		// If this connection is us, just use our local input instead.
		if ( Local == this )
			return Sandbox.Input.Pressed( action );

		if ( string.IsNullOrWhiteSpace( action ) )
			return false;

		var index = Sandbox.Input.GetActionIndex( action );
		if ( index == -1 )
			return false;

		var mask = 1UL << index;
		var context = Input.GetCurrentContext();

		return (context.Pressed & mask) != 0;
	}

	/// <summary>
	/// Action was released for this <see cref="Connection"/> within the current update context.
	/// </summary>
	public bool Released( [InputAction] string action )
	{
		// If this connection is us, just use our local input instead.
		if ( Local == this )
			return Sandbox.Input.Released( action );

		if ( string.IsNullOrWhiteSpace( action ) )
			return false;

		var index = Sandbox.Input.GetActionIndex( action );
		if ( index == -1 )
			return false;

		var mask = 1UL << index;
		var context = Input.GetCurrentContext();

		return (context.Released & mask) != 0;
	}

	private static bool ContainsKeys( ReadOnlySpan<ushort> held, ReadOnlySpan<string> keys )
	{
		if ( held.IsEmpty || keys.IsEmpty ) return false;

		foreach ( var key in keys )
		{
			var code = Engine.KeyTranslation.StringToButtonCode( key );
			if ( !held.Contains( (ushort)code ) ) return false;
		}

		return true;
	}

	/// <summary>
	/// Whether all named keyboard or mouse buttons are held on this connection.
	/// Pass the individual button names returned by <see cref="Sandbox.Input.Keyboard.Capture"/>.
	/// </summary>
	public bool KeyDown( params ReadOnlySpan<string> keys ) => this == Local
		? Sandbox.Input.Keyboard.ReadKeys( keys ).Down
		: ContainsKeys( Input.Command.Keys, keys );

	/// <summary>
	/// Whether the key or combination became held during the current frame or fixed update.
	/// </summary>
	public bool KeyPressed( params ReadOnlySpan<string> keys ) => this == Local
		? Sandbox.Input.Keyboard.ReadKeys( keys ).Pressed
		: Input.HasKeyEdge( keys, true );

	/// <summary>
	/// Whether the key or combination stopped being held during the current frame or fixed update.
	/// </summary>
	public bool KeyReleased( params ReadOnlySpan<string> keys ) => this == Local
		? Sandbox.Input.Keyboard.ReadKeys( keys ).Released
		: Input.HasKeyEdge( keys, false );

	/// <summary>
	/// Read a normalized controller axis. On the host, remote axes come from the latest received command.
	/// </summary>
	public float GetAnalog( InputAnalog analog )
	{
		if ( (int)analog < 0 || (int)analog >= UserCommand.AnalogCount )
			return 0f;

		return this == Local ? Sandbox.Input.GetAnalog( analog ) : Input.Command.GetAnalog( analog );
	}
}
