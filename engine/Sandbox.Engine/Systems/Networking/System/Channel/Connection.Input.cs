namespace Sandbox;

public abstract partial class Connection
{
	/// <summary>
	/// Build the <see cref="UserCommand"/> for this <see cref="Connection"/>.
	/// </summary>
	internal void BuildUserCommand( ref UserCommand cmd )
	{
		cmd.Actions = Sandbox.Input.Actions;
	}

	/// <summary>
	/// Clear pending pressed and released actions for all connections in the Update context.
	/// </summary>
	internal static void ClearUpdateContextInput()
	{
		foreach ( var connection in All )
		{
			connection.Input.ClearUpdateContext();
		}
	}

	/// <summary>
	/// Clear pending pressed and released actions for all connections in the Fixed Update context.
	/// </summary>
	internal static void ClearFixedUpdateContextInput()
	{
		foreach ( var connection in All )
		{
			connection.Input.ClearFixedUpdateContext();
		}
	}

	internal struct InputState
	{
		internal struct Context
		{
			public ulong Pressed;
			public ulong Released;

			/// <summary>
			/// Clear the state of this input context.
			/// </summary>
			public void Clear()
			{
				Released = 0;
				Pressed = 0;
			}
		}

		private UserCommand _lastUserCommand;

		public ulong Actions;

		private Context _fixedUpdateContext;
		private Context _updateContext;

		public Context GetCurrentContext()
		{
			var isFixedUpdate = Game.ActiveScene?.IsFixedUpdate ?? false;
			return isFixedUpdate ? _fixedUpdateContext : _updateContext;
		}

		public void ApplyUserCommand( in UserCommand cmd )
		{
			var commandNumberDelta = cmd.CommandNumber - _lastUserCommand.CommandNumber;

			// Drop duplicates or commands that are too far behind (wrap-aware)
			if ( commandNumberDelta is 0 or > 0x7FFFFFFF )
				return;

			var pressed = (~_lastUserCommand.Actions) & cmd.Actions;
			var released = _lastUserCommand.Actions & ~cmd.Actions;

			if ( pressed != 0 )
			{
				_fixedUpdateContext.Pressed |= pressed;
				_updateContext.Pressed |= pressed;
			}

			if ( released != 0 )
			{
				_fixedUpdateContext.Released |= released;
				_updateContext.Released |= released;
			}

			Actions = cmd.Actions;

			_lastUserCommand = cmd;
		}

		public void ClearFixedUpdateContext()
		{
			_fixedUpdateContext.Clear();
		}

		public void ClearUpdateContext()
		{
			_updateContext.Clear();
		}

		public void Clear()
		{
			_lastUserCommand = default;
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
		return (Input.Actions & mask) != 0;
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
}
