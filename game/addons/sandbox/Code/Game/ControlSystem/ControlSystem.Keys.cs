/// <summary>
/// Raw keys for <see cref="ClientInput.Key"/>. The engine only networks input actions, so the host tells each
/// driver which keys its contraption is bound to, and the driver reports just those - only when they change.
/// Nothing here allocates per tick or per frame. Only a change does - what's held, what's driven - plus reading
/// the bound keys again once a second, since reflection boxes each <see cref="ClientInput"/>.
/// </summary>
public sealed partial class ControlSystem
{
	private sealed class KeyState
	{
		public readonly HashSet<string> Watched = new( StringComparer.OrdinalIgnoreCase );
		public HashSet<string> Held = new( StringComparer.OrdinalIgnoreCase );
		public HashSet<string> NextHeld = new( StringComparer.OrdinalIgnoreCase );

		// Went down or up since the last control tick, then what this tick reads
		public readonly HashSet<string> PendingPressed = new( StringComparer.OrdinalIgnoreCase );
		public readonly HashSet<string> PendingReleased = new( StringComparer.OrdinalIgnoreCase );
		public readonly HashSet<string> Pressed = new( StringComparer.OrdinalIgnoreCase );
		public readonly HashSet<string> Released = new( StringComparer.OrdinalIgnoreCase );

		/// <summary>
		/// What we last read keys from - reading them boxes every <see cref="ClientInput"/>, so only when this
		/// changes, or now and then in case a binding was edited.
		/// </summary>
		public readonly List<IPlayerControllable> Controllables = new();
		public RealTimeSince KeysRead;

		public void BeginTick()
		{
			Pressed.Clear();
			Pressed.UnionWith( PendingPressed );
			PendingPressed.Clear();

			Released.Clear();
			Released.UnionWith( PendingReleased );
			PendingReleased.Clear();
		}
	}

	/// <summary>
	/// How often a driver's bound keys are read again when their contraption hasn't changed.
	/// </summary>
	private const float KeysRereadInterval = 1f;

	// Host: per driver, plus buffers reused every tick
	private readonly Dictionary<Connection, KeyState> _keyStates = new();
	private readonly HashSet<Connection> _watchedThisTick = new();
	private readonly List<Connection> _unwatched = new();
	private readonly List<IPlayerControllable> _controllables = new();
	private readonly HashSet<string> _collectedKeys = new( StringComparer.OrdinalIgnoreCase );
	private readonly Dictionary<Type, PropertyDescription[]> _inputProperties = new();

	// Driver: what the host asked us to report, and what we last told it was held
	private string[] _watchedKeys = [];
	private string[] _sentHeldKeys = [];

	/// <summary>
	/// Host, every control tick: these are what this driver's driving. Reads their bound keys if needed,
	/// and tells the driver only when the set changes.
	/// </summary>
	private void WatchKeys( Connection connection, List<IPlayerControllable> controllables )
	{
		_watchedThisTick.Add( connection );

		if ( !_keyStates.TryGetValue( connection, out var state ) )
			_keyStates[connection] = state = new KeyState();

		state.BeginTick();

		if ( state.KeysRead < KeysRereadInterval && SameControllables( state.Controllables, controllables ) )
			return;

		state.Controllables.Clear();
		state.Controllables.AddRange( controllables );
		state.KeysRead = 0;

		_collectedKeys.Clear();
		foreach ( var controllable in controllables )
		{
			if ( controllable is Component component && component.IsValid() )
				CollectKeys( component, _collectedKeys );
		}

		if ( state.Watched.SetEquals( _collectedKeys ) ) return;

		state.Watched.Clear();
		state.Watched.UnionWith( _collectedKeys );
		state.Held.IntersectWith( _collectedKeys );

		var keys = new string[_collectedKeys.Count];
		_collectedKeys.CopyTo( keys );
		SendWatchedKeys( connection, keys );
	}

	private static bool SameControllables( List<IPlayerControllable> a, List<IPlayerControllable> b )
	{
		if ( a.Count != b.Count ) return false;

		for ( var i = 0; i < a.Count; i++ )
		{
			if ( !ReferenceEquals( a[i], b[i] ) ) return false;
		}

		return true;
	}

	/// <summary>
	/// Host, after the control tick: anyone we didn't watch keys for has stood up, or gone.
	/// </summary>
	private void StopUnwatchedKeys()
	{
		_unwatched.Clear();

		foreach ( var (connection, _) in _keyStates )
		{
			if ( !_watchedThisTick.Contains( connection ) )
				_unwatched.Add( connection );
		}

		foreach ( var connection in _unwatched )
		{
			_keyStates.Remove( connection );
			SendWatchedKeys( connection, [] );
		}

		_watchedThisTick.Clear();
	}

	private void SendWatchedKeys( Connection connection, string[] keys )
	{
		if ( connection == Connection.Local )
		{
			SetWatchedKeys( keys );
			return;
		}

		using ( Rpc.FilterInclude( connection ) )
			RpcSetWatchedKeys( keys );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private static void RpcSetWatchedKeys( string[] keys ) => Current?.SetWatchedKeys( keys );

	private void SetWatchedKeys( string[] keys )
	{
		_watchedKeys = keys ?? [];
		_sentHeldKeys = [];
	}

	/// <summary>
	/// Driver, every frame: if the watched keys held down changed, tell the host.
	/// </summary>
	private void ReportWatchedKeys()
	{
		if ( _watchedKeys.Length == 0 && _sentHeldKeys.Length == 0 ) return;
		if ( Scene != Game.ActiveScene ) return;

		// Compare against what we sent without building anything
		var count = 0;
		var changed = false;

		foreach ( var key in _watchedKeys )
		{
			if ( !Input.Keyboard.Down( key ) ) continue;

			if ( count >= _sentHeldKeys.Length || !ReferenceEquals( _sentHeldKeys[count], key ) )
				changed = true;

			count++;
		}

		if ( !changed && count == _sentHeldKeys.Length ) return;

		var held = count == 0 ? Array.Empty<string>() : new string[count];
		var index = 0;

		foreach ( var key in _watchedKeys )
		{
			if ( index < count && Input.Keyboard.Down( key ) )
				held[index++] = key;
		}

		_sentHeldKeys = held;

		if ( Networking.IsHost ) OnHeldKeys( Connection.Local, held );
		else RpcHeldKeys( held );
	}

	[Rpc.Host]
	private static void RpcHeldKeys( string[] held ) => Current?.OnHeldKeys( Rpc.Caller, held );

	private void OnHeldKeys( Connection connection, string[] held )
	{
		if ( connection is null || held is null ) return;
		if ( !_keyStates.TryGetValue( connection, out var state ) ) return;

		var next = state.NextHeld;
		next.Clear();

		// Only keys we asked for - anything else is dropped
		foreach ( var key in held )
		{
			if ( state.Watched.Contains( key ) )
				next.Add( key );
		}

		foreach ( var key in next )
		{
			if ( !state.Held.Contains( key ) )
				state.PendingPressed.Add( key );
		}

		foreach ( var key in state.Held )
		{
			if ( !next.Contains( key ) )
				state.PendingReleased.Add( key );
		}

		state.NextHeld = state.Held;
		state.Held = next;
	}

	/// <summary>
	/// The raw keys bound by every <see cref="ClientInput"/> on this component.
	/// </summary>
	private void CollectKeys( Component component, HashSet<string> keys )
	{
		var type = component.GetType();
		if ( !_inputProperties.TryGetValue( type, out var properties ) )
		{
			properties = Game.TypeLibrary.GetType( type )?.Properties
				.Where( p => p.PropertyType == typeof( ClientInput ) )
				.ToArray() ?? [];

			_inputProperties[type] = properties;
		}

		foreach ( var property in properties )
		{
			if ( property.GetValue( component ) is not ClientInput input ) continue;

			foreach ( var key in input.Keys )
				keys.Add( key );
		}
	}

	/// <summary>
	/// Every key in the combo is held by this driver.
	/// </summary>
	internal bool KeysDown( Connection connection, string[] keys )
	{
		if ( keys.Length == 0 || connection is null || !_keyStates.TryGetValue( connection, out var state ) ) return false;

		foreach ( var key in keys )
		{
			if ( !state.Held.Contains( key ) ) return false;
		}

		return true;
	}

	/// <summary>
	/// The combo's held, or was tapped, and one of its keys went down this tick.
	/// </summary>
	internal bool KeysPressed( Connection connection, string[] keys )
	{
		if ( keys.Length == 0 || connection is null || !_keyStates.TryGetValue( connection, out var state ) ) return false;

		var any = false;
		foreach ( var key in keys )
		{
			var pressed = state.Pressed.Contains( key );
			if ( !pressed && !state.Held.Contains( key ) ) return false;
			any |= pressed;
		}

		return any;
	}

	/// <summary>
	/// One of the combo's keys went up this tick, and the rest were held or let go with it.
	/// </summary>
	internal bool KeysReleased( Connection connection, string[] keys )
	{
		if ( keys.Length == 0 || connection is null || !_keyStates.TryGetValue( connection, out var state ) ) return false;

		var any = false;
		foreach ( var key in keys )
		{
			var released = state.Released.Contains( key );
			if ( !released && !state.Held.Contains( key ) ) return false;
			any |= released;
		}

		return any;
	}
}
