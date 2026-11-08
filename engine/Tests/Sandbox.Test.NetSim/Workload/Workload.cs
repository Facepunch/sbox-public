namespace NetSim;

internal record WorkloadSettings
{
	/// <summary>Unowned props the host spawns at the start.</summary>
	public int Props { get; init; } = 200;

	/// <summary>Fraction of host props that move each frame (physics).</summary>
	public float MovingProps { get; init; } = 0.3f;

	/// <summary>Per prop per second chance that one of its sync vars changes.</summary>
	public float PropChangesPerSecond { get; init; } = 0.5f;

	/// <summary>Host prop spawns and destroys per second.</summary>
	public float ChurnPerSecond { get; init; } = 2;

	/// <summary>Host Hit RPCs per second.</summary>
	public float HitsPerSecond { get; init; } = 5;

	public float ShotsPerSecond { get; init; } = 3;
	public float FootstepsPerSecond { get; init; } = 3;
	public float SpawnRequestsPerSecond { get; init; } = 0.3f;
	public float InventoryChangesPerSecond { get; init; } = 0.2f;

	/// <summary>Per client, how often it grabs a prop (takes ownership) and how long it holds it.</summary>
	public float GrabsPerSecond { get; init; } = 0.2f;
	public float GrabSeconds { get; init; } = 3;

	public bool Interpolation { get; init; } = false;
	public int Seed { get; init; } = 1;
}

/// <summary>
/// Drives game-like traffic through the public game API on every peer of a <see cref="NetSimulation"/>.
/// </summary>
internal sealed class Workload : IDisposable
{
	public static Workload Current { get; private set; }

	public WorkloadSettings Settings { get; }
	readonly NetSimulation _sim;
	readonly Random _random;
	readonly Dictionary<SimPeer, ClientState> _clients = new();
	readonly List<Guid> _hostProps = new();
	int _seq;

	sealed class ClientState
	{
		public Guid Player;
		public Guid Grabbed;
		public float GrabbedUntil;
	}

	public bool Active { get; set; } = true;

	public Workload( NetSimulation sim, WorkloadSettings settings = null )
	{
		_sim = sim;
		Settings = settings ?? new WorkloadSettings();
		_random = new Random( Settings.Seed );
		Current = this;
		RpcLog.Entries.Clear();

		_sim.As( _sim.Host, () =>
		{
			for ( var i = 0; i < Settings.Props; i++ )
				SpawnProp( RandomPosition() );
		} );
	}

	/// <summary>
	/// Give a client a player object it owns.
	/// </summary>
	public void AddPlayer( SimPeer client )
	{
		var state = new ClientState();
		_clients[client] = state;

		_sim.As( client, () =>
		{
			var go = new GameObject( true, $"player {client.Name}" );
			go.SetDeterministicId( _sim.NewId() );
			go.WorldPosition = RandomPosition();
			go.Components.Create<SimPlayer>().SetDeterministicId( _sim.NewId() );
			go.Network.Interpolation = Settings.Interpolation;
			go.NetworkSpawn();
			state.Player = go.Id;
		} );
	}

	/// <summary>
	/// Host only: spawn an unowned prop anyone can grab.
	/// </summary>
	public void SpawnProp( Vector3 position )
	{
		var go = new GameObject( true, "prop" );
		go.SetDeterministicId( _sim.NewId() );
		go.WorldPosition = position;
		go.WorldRotation = Rotation.From( _random.Next( 360 ), _random.Next( 360 ), 0 );
		var prop = go.Components.Create<SimProp>();
		prop.SetDeterministicId( _sim.NewId() );
		prop.Tint = Color.FromBytes( _random.Next( 256 ), _random.Next( 256 ), _random.Next( 256 ) );
		go.Network.Interpolation = Settings.Interpolation;
		go.Network.SetOwnerTransfer( OwnerTransfer.Takeover );
		go.NetworkSpawn( null );
		_hostProps.Add( go.Id );
	}

	Vector3 RandomPosition() => new( _random.Next( -2000, 2000 ), _random.Next( -2000, 2000 ), _random.Next( 0, 500 ) );

	bool Chance( float perSecond ) => _random.NextDouble() < perSecond * _sim.FrameTime;

	/// <summary>
	/// One frame of game activity, call before <see cref="NetSimulation.Step"/>.
	/// </summary>
	public void Tick()
	{
		if ( !Active )
			return;

		_sim.As( _sim.Host, TickHost );

		foreach ( var (client, state) in _clients )
			_sim.As( client, () => TickClient( client, state ) );
	}

	void TickHost()
	{
		var scene = Game.ActiveScene;

		for ( var i = 0; i < _hostProps.Count; i++ )
		{
			var go = scene.Directory.FindByGuid( _hostProps[i] );
			if ( !go.IsValid() )
				continue;

			var prop = go.Components.Get<SimProp>();

			// Grabbed props are moved by their owner
			if ( go.Network.OwnerId == Guid.Empty && _random.NextDouble() < Settings.MovingProps )
			{
				go.WorldPosition += new Vector3( (float)_random.NextDouble() - 0.5f, (float)_random.NextDouble() - 0.5f, -0.2f ) * 10;
				go.WorldRotation *= Rotation.FromYaw( 1 );
			}

			if ( Chance( Settings.PropChangesPerSecond ) )
			{
				switch ( _random.Next( 3 ) )
				{
					case 0: prop.Health = _random.Next( 100 ); break;
					case 1: prop.Tint = Color.FromBytes( _random.Next( 256 ), _random.Next( 256 ), _random.Next( 256 ) ); break;
					case 2: prop.Heat = _random.Next( 1000 ); break;
				}
			}
		}

		var hits = Settings.HitsPerSecond * _sim.FrameTime;
		while ( hits > 0 )
		{
			if ( hits >= 1 || _random.NextDouble() < hits )
			{
				var go = scene.Directory.FindByGuid( _hostProps[_random.Next( _hostProps.Count )] );
				go?.Components.Get<SimProp>()?.Hit( _random.Next( 50 ), ++_seq );
			}

			hits -= 1;
		}

		if ( Chance( Settings.ChurnPerSecond ) && _hostProps.Count > 0 )
		{
			var index = _random.Next( _hostProps.Count );
			var go = scene.Directory.FindByGuid( _hostProps[index] );
			_hostProps.RemoveAt( index );
			go?.Destroy();
		}

		if ( Chance( Settings.ChurnPerSecond ) )
			SpawnProp( RandomPosition() );
	}

	void TickClient( SimPeer client, ClientState state )
	{
		var scene = Game.ActiveScene;
		var go = scene.Directory.FindByGuid( state.Player );
		if ( !go.IsValid() )
			return;

		var player = go.Components.Get<SimPlayer>();

		go.WorldPosition += new Vector3( 5, 0, 0 ) * (float)Math.Sin( _sim.Frame * 0.05 );
		player.Aim = new Vector3( (float)_random.NextDouble(), (float)_random.NextDouble(), 0 ).Normal;

		if ( Chance( Settings.ShotsPerSecond ) )
		{
			player.Shoot( go.WorldPosition, player.Aim, ++_seq );
			player.Ammo = Math.Max( 0, player.Ammo - 1 );
		}

		if ( Chance( Settings.FootstepsPerSecond ) )
			player.Footstep( go.WorldPosition );

		if ( Chance( Settings.SpawnRequestsPerSecond ) )
			player.RequestSpawn( go.WorldPosition + Vector3.Forward * 100, ++_seq );

		if ( Chance( Settings.InventoryChangesPerSecond ) )
		{
			if ( player.Inventory.Count > 5 ) player.Inventory.RemoveAt( 0 );
			else player.Inventory.Add( _random.Next( 1000 ) );
		}

		// Physgun: take a prop, move it around, let go
		if ( state.Grabbed != Guid.Empty )
		{
			var grabbed = scene.Directory.FindByGuid( state.Grabbed );

			if ( !grabbed.IsValid() || !grabbed.Network.IsOwner || _sim.Now > state.GrabbedUntil )
			{
				if ( grabbed.IsValid() && grabbed.Network.IsOwner )
					grabbed.Network.DropOwnership();

				state.Grabbed = Guid.Empty;
			}
			else
			{
				grabbed.WorldPosition = go.WorldPosition + player.Aim * 100;
			}
		}
		else if ( Chance( Settings.GrabsPerSecond ) )
		{
			var props = scene.GetAllComponents<SimProp>().Where( p => p.GameObject.Network.OwnerId == Guid.Empty ).ToList();
			if ( props.Count > 0 )
			{
				var target = props[_random.Next( props.Count )].GameObject;
				if ( target.Network.TakeOwnership() )
				{
					state.Grabbed = target.Id;
					state.GrabbedUntil = (float)_sim.Now + Settings.GrabSeconds;
				}
			}
		}
	}

	/// <summary>
	/// Let go of everything held so the world can come to rest.
	/// </summary>
	public void Stop()
	{
		Active = false;

		foreach ( var (client, state) in _clients )
		{
			if ( state.Grabbed == Guid.Empty )
				continue;

			_sim.As( client, () =>
			{
				var grabbed = Game.ActiveScene.Directory.FindByGuid( state.Grabbed );
				if ( grabbed.IsValid() && grabbed.Network.IsOwner )
					grabbed.Network.DropOwnership();
			} );

			state.Grabbed = Guid.Empty;
		}
	}

	public void Dispose()
	{
		if ( Current == this )
			Current = null;
	}
}
