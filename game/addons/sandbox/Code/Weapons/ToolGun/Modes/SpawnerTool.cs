/// <summary>
/// Previews and places objects into the world.
/// Accepts any <see cref="ISpawner"/> to define what to spawn.
/// The spawn menu (or any other system) sets the payload, and this mode handles
/// aiming, previewing, and placement.
/// </summary>
[Hide]
[HiddenToolMode]
[Icon( "📦" )]
[Title( "#spawner.name" )]
[ClassName( "spawner" )]
[Group( "#tool.group.building" )]
public sealed class SpawnerTool : ToolMode
{
	/// <summary>
	/// Synced payload descriptor. When this changes on any client,
	/// <see cref="OnPayloadDataChanged"/> reconstructs the <see cref="ISpawner"/> locally.
	/// </summary>
	[Sync( SyncFlags.FromHost ), Change( nameof( OnPayloadDataChanged ) )]
	public string SpawnerData { get; set; }

	/// <summary>
	/// The local spawner, built from <see cref="SpawnerData"/>.
	/// </summary>
	public ISpawner Spawner { get; private set; }

	private Material _previewMaterial;
	private Material _previewMaterialInvalid;

	/// <summary>
	/// True while the player is holding reload to rotate the preview.
	/// </summary>
	private bool _isRotating;

	/// <summary>
	/// Accumulated rotation offset applied to the spawn preview.
	/// </summary>
	private Rotation _rotationOffset = Rotation.Identity;

	private Rotation _snapRotation = Rotation.Identity;
	private Rotation _spinRotation = Rotation.Identity;
	private bool _isSnapping;

	public override string Description => Spawner is null
		? "#spawner.name"
		: LocalizedText.Literal( Game.Language.GetPhrase( "spawner.placing", new() { { "name", LocalizedText.Resolve( Spawner.DisplayName ) } } ) );

	public override string PrimaryAction => Spawner is not null ? "#spawner.spawn" : null;
	public override string ReloadAction => Spawner is not null ? "#spawner.rotate" : null;

	/// <summary>
	/// Hold snap to steer the aim onto the grid, so placement lands on grid points.
	/// </summary>
	public override bool UseSnapGrid => Spawner is not null;

	public override void OnCameraMove( Player player, ref Angles angles )
	{
		base.OnCameraMove( player, ref angles );

		if ( _isRotating )
			angles = default;
	}

	protected override void OnStart()
	{
		base.OnStart();

		_previewMaterial = Material.Load( "materials/effects/duplicator_override.vmat" );
		_previewMaterialInvalid = Material.Load( "materials/effects/duplicator_override_other.vmat" );
	}

	/// <summary>
	/// Set what this mode should spawn. Host only, syncs to all clients via <see cref="SpawnerData"/>.
	/// </summary>
	public void SetSpawner( ISpawner payload )
	{
		Spawner = payload;
		SpawnerData = SerializeSpawner( payload );
	}

	/// <summary>
	/// Clear the current payload, returning to an idle state.
	/// </summary>
	[Rpc.Host]
	private void ClearPayload()
	{
		SpawnerData = null;
	}

	/// <summary>
	/// Called on every client when <see cref="SpawnerData"/> changes.
	/// Reconstructs the <see cref="ISpawner"/> locally so each client can render the preview.
	/// </summary>
	private void OnPayloadDataChanged()
	{
		Spawner = DeserializeSpawner( SpawnerData );

		// Only the player holding it sees the loading ring - never the host on someone else's behalf
		if ( Player?.IsLocalPlayer == true ) CloudLoadTracker.Track( Spawner?.Loading );
	}

	record SpawnerPayload( string Type, string Data, string Metadata );

	/// <summary>
	/// Serialize a spawner for networking, including its <see cref="ISpawner.Metadata"/>
	/// so clients build it the same as the host, even if they're missing its content.
	/// </summary>
	private static string SerializeSpawner( ISpawner spawner )
	{
		var type = spawner switch
		{
			PropSpawner => "prop",
			EntitySpawner => "entity",
			MountSpawner => "mount",
			DuplicatorSpawner => "dupe",
			_ => null
		};

		if ( type is null )
			return null;

		return Json.Serialize( new SpawnerPayload( type, spawner.Data, spawner.Metadata ) );
	}

	/// <summary>
	/// Reconstruct an <see cref="ISpawner"/> from <see cref="SerializeSpawner"/>
	/// </summary>
	private static ISpawner DeserializeSpawner( string data )
	{
		if ( string.IsNullOrWhiteSpace( data ) )
			return null;

		var payload = Json.Deserialize<SpawnerPayload>( data );
		if ( payload is null )
			return null;

		return ISpawner.Create( payload.Type, payload.Data, metadata: payload.Metadata );
	}

	/// <summary>
	/// Handles placing, clearing, and rotating the preview while reload is held.
	/// </summary>
	public override void OnControl()
	{
		base.OnControl();

		if ( Spawner is null )
		{
			// The base stops updating the grid once UseSnapGrid goes false, so don't leave it hanging
			SnapGrid?.Hide();
			IsValidState = false;
			return;
		}

		_isRotating = Input.Down( "reload" );
		Toolgun.SetIsUsingJoystick( _isRotating );

		var isSnapping = Input.Down( "run" );
		if ( !isSnapping && _isSnapping ) _spinRotation = _snapRotation;
		_isSnapping = isSnapping;

		if ( _isRotating )
		{
			var look = Input.AnalogLook with { pitch = 0 };

			if ( _isSnapping )
			{
				if ( MathF.Abs( look.yaw ) > MathF.Abs( look.pitch ) ) look.pitch = 0;
				else look.yaw = 0;
			}

			_spinRotation = Rotation.From( look ) * _spinRotation;
			Input.Clear( "reload" );

			if ( _isSnapping )
			{
				var snapped = _spinRotation.Angles();
				_rotationOffset = snapped.SnapToGrid( 45f );
			}
			else
			{
				_rotationOffset = _spinRotation;
			}

			_snapRotation = _rotationOffset;

			Toolgun.UpdateJoystick( new Angles( look.yaw, look.pitch, 0 ) );
		}

		var placement = GetPlacementInfo();
		IsValidState = placement.Hit && Spawner.IsReady;

		if ( IsValidState && Input.Pressed( "attack1" ) )
		{
			var transform = GetSpawnTransform( placement );
			DoSpawn( transform, new Transform( placement.EndPosition, Rotation.LookAt( placement.Normal ) ) );
			_rotationOffset = Rotation.Identity;
			_spinRotation = Rotation.Identity;
		}
	}

	protected override void OnDisabled()
	{
		base.OnDisabled();

		_isRotating = false;
		Toolgun?.SetIsUsingJoystick( false );
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();

		if ( Application.IsDedicatedServer ) return;
		if ( Spawner is null ) return;
		if ( !Player.IsValid() ) return;

		// Draw preview on all clients, so everyone can see what's being placed
		DrawPreview();
	}

	private void DrawPreview()
	{
		var placement = GetPlacementInfo();
		if ( !placement.Hit ) return;

		var transform = GetSpawnTransform( placement );

		// Use a different material for other players' previews, same as the Duplicator
		var material = IsProxy
			? _previewMaterialInvalid
			: IsValidState ? _previewMaterial : _previewMaterialInvalid;

		Spawner.DrawPreview( transform, material );
	}

	private SceneTraceResult GetPlacementInfo()
	{
		return Scene.Trace.Ray( Player.EyeTransform.ForwardRay, 4096 )
			.IgnoreGameObjectHierarchy( Player.GameObject )
			.WithoutTags( "player" )
			.Run();
	}

	private Transform GetSpawnTransform( SceneTraceResult trace )
	{
		var up = trace.Normal;
		var backward = -Player.EyeTransform.Forward;
		var right = Vector3.Cross( up, backward ).Normal;
		var forward = Vector3.Cross( right, up ).Normal;
		var facingAngle = Rotation.LookAt( forward, up );

		var position = trace.EndPosition;

		// Offset by bounds so the object sits on the surface
		if ( Spawner is not null )
		{
			position += up * -Spawner.Bounds.Mins.z;
		}

		return new Transform( position, facingAngle * _rotationOffset );
	}

	[Rpc.Host]
	private async void DoSpawn( Transform transform, Transform effectTarget )
	{
		if ( Spawner is null ) return;

		var player = Player.FindForConnection( Rpc.Caller );
		if ( player is null ) return;

		var spawner = Spawner;

		var spawnData = new Global.ISpawnEvents.SpawnData
		{
			Spawner = spawner,
			Transform = transform,
			Player = player.Network.Owner
		};

		Scene.RunEvent<Global.ISpawnEvents>( x => x.OnSpawn( spawnData ) );

		if ( spawnData.Cancelled )
			return;

		var objects = await spawner.Spawn( transform, player );

		if ( objects is { Count: > 0 } )
		{
			if ( this.IsValid() ) SpawnEffects( effectTarget );

			var undo = player.Undo.Create();
			undo.Name = "#undo.spawn";
			undo.NameTokens = new() { { "name", spawner.DisplayName } };
			undo.Add( objects );

			Scene.RunEvent<Global.ISpawnEvents>( x => x.OnPostSpawn( new Global.ISpawnEvents.PostSpawnData
			{
				Spawner = spawner,
				Transform = transform,
				Player = player.Network.Owner,
				Objects = objects
			} ) );
		}
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void SpawnEffects( Transform target ) => Toolgun?.PlayToolEffects( target );
}
