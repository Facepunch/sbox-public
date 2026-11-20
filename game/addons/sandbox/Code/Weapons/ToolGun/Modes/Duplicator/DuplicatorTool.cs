using Sandbox.UI;
using System.Text.Json;
using System.Text.Json.Nodes;

[Icon( "✌️" )]
[Title( "#tool.name.duplicator" )]
[ClassName( "duplicator" )]
[Group( "#tool.group.building" )]
public sealed partial class DuplicatorTool : ToolMode
{
	/// <summary>
	/// When we right click, to "copy" something, we create a Duplication object
	/// and serialize it to Json and store it here.
	/// </summary>
	[Sync( SyncFlags.FromHost ), Change( nameof( JsonChanged ) )]
	public string CopiedJson { get; set; }

	DuplicatorSpawner spawner;
	LinkedGameObjectBuilder builder = new() { RejectPlayers = true };

	Rotation _rotationOffset = Rotation.Identity;
	Rotation _spinRotation = Rotation.Identity;
	Rotation _snapRotation = Rotation.Identity;
	bool _isSnapping;
	bool _isRotating;

	/// <summary>
	/// While held, the duplication's up axis follows the surface normal instead of world up
	/// </summary>
	[Sync] public bool AlignToSurface { get; set; }

	public override string Description => "#tool.hint.duplicator.description";
	public override string PrimaryAction => spawner is not null ? "#tool.hint.duplicator.place" : null;
	public override string SecondaryAction => "#tool.hint.duplicator.copy";

	/// <summary>
	/// Shows the rotation hint while a duplication is available to place.
	/// </summary>
	public override string ReloadAction => spawner is not null ? "#tool.hint.duplicator.rotate" : null;

	public override string SnapAction => spawner is not null ? "#tool.hint.duplicator.align" : null;

	public override void OnCameraMove( Player player, ref Angles angles )
	{
		base.OnCameraMove( player, ref angles );

		if ( _isRotating )
			angles = default;
	}

	/// <summary>
	/// Handles copying, placing, and rotating the duplication while reload is held.
	/// </summary>
	public override void OnControl()
	{
		base.OnControl();

		_isRotating = spawner is not null && Input.Down( "reload" );
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

		AlignToSurface = Input.Down( "snap" );

		var select = TraceSelect();
		IsValidState = IsValidTarget( select );

		if ( spawner is { IsReady: true } && Input.Pressed( "attack1" ) )
		{
			if ( !IsValidPlacementTarget( select ) )
			{
				// make invalid noise
				return;
			}

			Duplicate( GetPlacement( select ) );
			ShootEffects( select );
			return;
		}

		if ( Input.Pressed( "attack2" ) )
		{
			if ( !IsValidState )
			{
				CopiedJson = default;
				return;
			}

			var selectionAngle = new Transform( select.WorldPosition(), Player.EyeTransform.Rotation.Angles().WithPitch( 0 ) );
			Copy( select.GameObject, selectionAngle, Input.Down( "run" ) );

			ShootEffects( select );
		}
	}

	/// <summary>
	/// Save the current dupe to storage.
	/// </summary>
	public void Save()
	{
		string data = CopiedJson;
		var packages = Cloud.ResolvePrimaryAssetsFromJson( data );

		var storage = Storage.CreateEntry( "dupe" );
		storage.SetMeta( "packages", packages.Select( x => x.FullIdent ) );
		storage.Files.WriteAllText( "/dupe.json", data );

		var bitmap = new Bitmap( 1024, 1024 );
		RenderIconToBitmap( data, bitmap );

		var downscaled = bitmap.Resize( 512, 512 );
		storage.SetThumbnail( downscaled );
	}

	[Rpc.Host]
	public void Load( string json )
	{
		CopiedJson = json;
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();

		if ( Application.IsDedicatedServer )
			return;

		// this is called on every client, so we can see what the other
		// players are placing. It's kind of cool.
		DrawPreview();
	}

	[Rpc.Host]
	public void Copy( GameObject obj, Transform selectionAngle, bool additive )
	{
		if ( !additive )
			builder.Clear();

		builder.AddConnected( obj );
		builder.RemoveDeletedObjects();

		var tempDupe = DuplicationData.CreateFromObjects( builder.Objects, selectionAngle );

		CopiedJson = Json.Serialize( tempDupe );

		PlayerData.For( Rpc.Caller )?.AddStat( "tool.duplicator.copy" );
	}

	void JsonChanged()
	{
		spawner = null;

		_rotationOffset = Rotation.Identity;
		_spinRotation = Rotation.Identity;
		_snapRotation = Rotation.Identity;

		if ( string.IsNullOrWhiteSpace( CopiedJson ) )
			return;

		spawner = DuplicatorSpawner.FromJson( CopiedJson );
		if ( Player?.IsLocalPlayer == true ) CloudLoadTracker.Track( spawner.Loading );
	}

	void DrawPreview()
	{
		if ( spawner is null ) return;

		var select = TraceSelect();
		if ( !IsValidPlacementTarget( select ) ) return;

		var overlayMaterial = IsProxy ? Material.Load( "materials/effects/duplicator_override_other.vmat" ) : Material.Load( "materials/effects/duplicator_override.vmat" );
		spawner.DrawPreview( GetPlacement( select ), overlayMaterial );
	}

	Transform GetPlacement( SelectionPoint select )
	{
		var eyeRotation = Player.EyeTransform.Rotation;
		var up = AlignToSurface ? select.WorldTransform().Rotation.Forward : Vector3.Up;

		// Face the view direction projected onto the surface, falling back to view up when looking straight at it.
		var forward = eyeRotation.Forward - up * Vector3.Dot( eyeRotation.Forward, up );
		if ( forward.LengthSquared < 0.01f )
			forward = eyeRotation.Up - up * Vector3.Dot( eyeRotation.Up, up );

		var rotation = Rotation.LookAt( forward.Normal, up ) * _rotationOffset;
		var position = select.WorldPosition() - up * spawner.Bounds.Mins.z;

		return new Transform( position, rotation );
	}


	bool IsValidTarget( SelectionPoint source )
	{
		if ( !source.IsValid() ) return false;
		if ( source.IsWorld ) return false;
		if ( source.IsPlayer ) return false;

		return true;
	}

	bool IsValidPlacementTarget( SelectionPoint source )
	{
		if ( !source.IsValid() ) return false;

		return true;
	}

	[Rpc.Host]
	public async void Duplicate( Transform dest )
	{
		if ( spawner is null )
			return;

		var player = Player.FindForConnection( Rpc.Caller );
		if ( player is null ) return;

		var spawnData = new Global.ISpawnEvents.SpawnData
		{
			Spawner = spawner,
			Transform = dest,
			Player = player.Network.Owner
		};

		Scene.RunEvent<Global.ISpawnEvents>( x => x.OnSpawn( spawnData ) );

		if ( spawnData.Cancelled )
			return;

		var objects = await spawner.Spawn( dest, player );

		if ( objects is { Count: > 0 } )
		{
			var undo = player.Undo.Create();
			undo.Name = "#tool.undo.duplication";
			undo.Add( objects );

			Scene.RunEvent<Global.ISpawnEvents>( x => x.OnPostSpawn( new Global.ISpawnEvents.PostSpawnData
			{
				Spawner = spawner,
				Transform = dest,
				Player = player.Network.Owner,
				Objects = objects
			} ) );

			player.PlayerData?.AddStat( "tool.duplicator.spawn" );
		}
	}

	public static void FromStorage( Storage.Entry item )
	{
		var localPlayer = Player.FindLocalPlayer();
		if ( localPlayer == null ) return;

		var inventory = localPlayer.GetComponent<PlayerInventory>();
		if ( !inventory.IsValid() ) return;

		inventory.SetToolMode( "DuplicatorTool" );

		var toolmode = localPlayer.GetComponentInChildren<DuplicatorTool>( true );

		// we don't have a duplicator tool!
		if ( toolmode is null ) return;

		var json = item.Files.ReadAllText( "/dupe.json" );
		toolmode.Load( json );
	}

	public static async Task FromWorkshop( Storage.QueryItem item )
	{
		var notice = Notices.AddNotice( "downloading", Color.Yellow, Game.Language.GetPhrase( "tool.duplicator.installing", new() { { "name", item.Title } } ), 0 );
		notice?.AddClass( "progress" );

		var installed = await item.Install();

		notice?.Dismiss();

		if ( installed == null ) return;

		FromStorage( installed );
	}
}
