using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// Payload for spawning a duplicator contraption.
/// </summary>
public sealed class DuplicatorSpawner : ISpawner
{
	public string DisplayName { get; private set; } = "#spawner.duplication";
	public string Icon { get; init; }
	public BBox Bounds => Dupe?.Bounds ?? default;
	public bool IsReady => Dupe is not null && _packagesReady;
	public Task<bool> Loading { get; }

	public string Data => Sandbox.Json.Serialize( new DupeInfo( Icon, Json ) );

	public DuplicationData Dupe { get; private set; }

	public string Json { get; private set; }

	private bool _packagesReady;

	/// <summary>
	/// The building blocks in this dupe, for limits, permissions and prewarming their models.
	/// </summary>
	public BuilderDupe.Contents BuilderBlocks => _builderBlocks ??= BuilderDupe.Read( Dupe?.Objects );
	private BuilderDupe.Contents _builderBlocks;
	private (BuilderShapeKey Key, BBox Bounds)?[] _previewBlocks;

	public DuplicatorSpawner( DuplicationData dupe, string json, string name = null, string icon = null )
	{
		Dupe = dupe;
		Json = json;
		Icon = icon;
		DisplayName = LocalizedText.Literal( name ) ?? "#spawner.duplication";
		Loading = InstallPackages();
	}

	/// <summary>
	/// Create a duplicator spawner from a storage or workshop ident.
	/// Resolution and package installation happen asynchronously via <see cref="ISpawner.Loading"/>.
	/// </summary>
	public DuplicatorSpawner( string id, string source )
	{
		Loading = ResolveAndLoad( id, source );
	}

	private async Task<bool> ResolveAndLoad( string id, string source )
	{
		if ( !ulong.TryParse( id, out var fileId ) )
			return false;

		string json;
		string name = null;

		if ( source == "workshop" )
		{
			var query = new Storage.Query { FileIds = [fileId] };

			var result = await query.Run();
			var item = result.Items?.FirstOrDefault();
			if ( item is null ) return false;

			var installed = await item.Install();
			if ( installed is null ) return false;

			json = await installed.Files.ReadAllTextAsync( "/dupe.json" );
			name = item.Title;
		}
		else
		{
			var entry = Storage.GetAll( "dupe" ).FirstOrDefault( x => x.Id.ToString() == fileId.ToString() );
			if ( entry is null ) return false;

			json = await entry.Files.ReadAllTextAsync( "/dupe.json" );
			name = entry.GetMeta<string>( "name" );
		}

		Dupe = Sandbox.Json.Deserialize<DuplicationData>( json );
		Json = json;
		DisplayName = LocalizedText.Literal( name ) ?? "#spawner.duplication";

		return await InstallPackages();
	}

	/// <summary>
	/// Create from raw dupe JSON (e.g. from a storage entry). No icon.
	/// </summary>
	public static DuplicatorSpawner FromJson( string json, string name = null, string icon = null )
	{
		var dupe = Sandbox.Json.Deserialize<DuplicationData>( json );
		return new DuplicatorSpawner( dupe, json, name, icon );
	}

	/// <summary>
	/// Creates a duplicator spawner from the serialized data string. This is what gets synced to clients, so it includes the icon and raw JSON.
	/// </summary>
	/// <param name="data"></param>
	/// <returns></returns>
	public static DuplicatorSpawner FromData( string data )
	{
		var payload = Sandbox.Json.Deserialize<DupeInfo>( data );
		var dupe = Sandbox.Json.Deserialize<DuplicationData>( payload.Json );
		return new DuplicatorSpawner( dupe, payload.Json, icon: payload.Icon );
	}

	private record DupeInfo( string Icon, string Json );

	private async Task<bool> InstallPackages()
	{
		if ( Dupe?.Packages is null || Dupe.Packages.Count == 0 )
		{
			_packagesReady = true;
			return true;
		}

		foreach ( var pkg in Dupe.Packages )
		{
			if ( Cloud.IsInstalled( pkg ) )
				continue;

			await Cloud.Load( pkg );
		}

		_packagesReady = true;
		return true;
	}

	/// <summary>
	/// Draws the duplication ghost, using a separate blueprint material for models that have not loaded yet.
	/// </summary>
	public void DrawPreview( Transform transform, Material overrideMaterial )
	{
		if ( Dupe?.PreviewModels is null ) return;

		if ( _previewBlocks?.Length != Dupe.PreviewModels.Count )
		{
			_previewBlocks = new (BuilderShapeKey, BBox)?[Dupe.PreviewModels.Count];
			for ( int i = 0; i < _previewBlocks.Length; i++ )
				_previewBlocks[i] = GetPreviewBlock( Dupe.PreviewModels[i].Builder );
		}

		for ( int i = 0; i < Dupe.PreviewModels.Count; i++ )
		{
			var model = Dupe.PreviewModels[i];
			if ( model.Builder is not null )
			{
				if ( _previewBlocks[i] is { } block ) DrawBuilderPreview( block.Key, block.Bounds, transform.ToWorld( model.Transform ), overrideMaterial );
				continue;
			}

			if ( model.Model is null )
				continue;

			if ( model.Model.IsError )
			{
				var bounds = model.Bounds;
				if ( bounds.Size.IsNearlyZero() ) continue;

				var t = transform.ToWorld( model.Transform );
				t = new Transform( t.PointToWorld( bounds.Center ), t.Rotation, t.Scale * (bounds.Size / 50) );

				var downloadingMaterial = Material.Load( "materials/effects/duplicator_override_downloading.vmat" );
				Game.ActiveScene.DebugOverlay.Model( Model.Cube, transform: t, overlay: false, materialOveride: downloadingMaterial );
			}
			else
			{
				Game.ActiveScene.DebugOverlay.Model( model.Model, transform: transform.ToWorld( model.Transform ), overlay: false, materialOveride: overrideMaterial, localBoneTransforms: model.Bones );
			}
		}
	}

	private static (BuilderShapeKey, BBox)? GetPreviewBlock( BuilderRecipe recipe )
	{
		if ( recipe is null || !recipe.TryGetKey( out var key ) ) return null;
		return (key, BuilderShapes.Get( key.Shape )?.GetBounds( key ) ?? BBox.FromPositionAndSize( Vector3.Zero, key.Size ));
	}

	private static void DrawBuilderPreview( BuilderShapeKey key, BBox bounds, Transform transform, Material overrideMaterial )
	{
		if ( BuilderModelCache.TryGet( key, out var built ) )
		{
			Game.ActiveScene.DebugOverlay.Model( built, transform: transform, overlay: false, materialOveride: overrideMaterial );
			return;
		}

		BuilderBuildQueue.Current?.Enqueue( key );

		var box = new Transform( transform.PointToWorld( bounds.Center ), transform.Rotation, transform.Scale * (bounds.Size / 50) );
		var downloadingMaterial = Material.Load( "materials/effects/duplicator_override_downloading.vmat" );
		Game.ActiveScene.DebugOverlay.Model( Model.Cube, transform: box, overlay: false, materialOveride: downloadingMaterial );
	}

	public async Task<List<GameObject>> Spawn( Transform transform, Player player )
	{
		if ( BuilderBlocks.Keys.Count > 0 && BuilderBuildQueue.Current is { } queue )
		{
			var scene = Game.ActiveScene;
			if ( !await queue.Prewarm( BuilderBlocks.Keys ) ) return new List<GameObject>();
			if ( !player.IsValid() || !scene.IsValid() || Game.ActiveScene != scene ) return new List<GameObject>();

			var owner = player.Network.Owner;
			if ( !BuilderPolicy.CanBuild( owner ) ) return new List<GameObject>();
			if ( LimitsSystem.Get( scene ) is { } limits && !limits.CheckDupeLimits( owner, this ) ) return new List<GameObject>();
		}

		var jsonObject = Sandbox.Json.ToNode( Dupe ) as JsonObject;
		SceneUtility.MakeIdGuidsUnique( jsonObject );

		var results = new List<GameObject>();

		using ( Game.ActiveScene.BatchGroup() )
		{
			foreach ( var entry in jsonObject["Objects"] as JsonArray )
			{
				if ( entry is not JsonObject obj )
					continue;

				var pos = entry["Position"]?.Deserialize<Vector3>() ?? default;
				var rot = entry["Rotation"]?.Deserialize<Rotation>() ?? Rotation.Identity;
				var scl = entry["Scale"]?.Deserialize<Vector3>() ?? Vector3.One;

				var world = transform.ToWorld( new Transform( pos, rot ) );
				world.Scale = scl;

				var go = new GameObject( false );
				go.Deserialize( obj, new GameObject.DeserializeOptions { TransformOverride = world } );

				Ownable.Set( go, player.Network.Owner );
				go.NetworkSpawn( true, null );

				results.Add( go );
			}
		}

		if ( BuilderBlocks.Count > 0 )
			LimitsSystem.Get( Game.ActiveScene )?.TrackNow( player.Network.Owner, results );

		return results;
	}
}
