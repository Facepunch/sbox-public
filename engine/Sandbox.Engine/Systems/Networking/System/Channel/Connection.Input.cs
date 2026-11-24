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
		}

		/// <summary>
		/// The newest accepted input sample, received from this connection.
		/// </summary>
		internal UserCommand Command;

		private Context _fixedUpdateContext;
		private Context _updateContext;

		public Context GetCurrentContext()
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
