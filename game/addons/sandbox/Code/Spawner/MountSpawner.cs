using Sandbox.Mounting;

/// <summary>
/// Like <see cref="PropSpawner"/>, but attaches <see cref="MountMetadata"/> to the spawned object
/// so clients without the mount installed can show a fallback cube and install prompt.
/// </summary>
public sealed class MountSpawner : ISpawner
{
	/// <summary>
	/// What the requesting client resolved locally, so a host without the mount still spawns
	/// it with the right size and game title.
	/// </summary>
	record MountInfo( string GameTitle, Vector3 BoundsMins, Vector3 BoundsMaxs );

	static readonly BBox PlaceholderBounds = new( -Vector3.One * 8f, Vector3.One * 8f );
	const float MaxBoundsSize = 4096f;

	public string DisplayName { get; private set; }
	public string Icon => Path;
	public string Data => Path;
	public BBox Bounds => HasModel ? Model.Bounds : _fallbackBounds;
	public bool IsReady => Model.IsValid();
	public Task<bool> Loading { get; }

	public Model Model { get; private set; }
	public string Path { get; }
	public string GameTitle { get; private set; }

	/// <summary>
	/// True if the mounted model loaded locally.
	/// </summary>
	public bool HasModel => Model.IsValid() && !Model.IsError;

	BBox _fallbackBounds = PlaceholderBounds;

	public MountSpawner( string path, string metadata = null )
	{
		Path = path;

		if ( MountUtility.TryParse( path, out string sourceName ) )
		{
			var source = Directory.Get( sourceName );
			if ( source is null )
			{
				Log.Warning( $"Couldn't find source \"{sourceName}\"" );
			}
			else
			{
				GameTitle = source.Title;
			}
		}

		ApplyMetadata( metadata );

		DisplayName = System.IO.Path.GetFileNameWithoutExtension( path );
		Loading = LoadAsync();
	}

	void ApplyMetadata( string json )
	{
		if ( string.IsNullOrEmpty( json ) )
			return;

		var meta = Json.Deserialize<MountInfo>( json );
		if ( meta is null )
		{
			Log.Warning( $"[MountSpawner] Failed to deserialize metadata for '{Path}': {json}" );
			return;
		}

		if ( string.IsNullOrEmpty( GameTitle ) )
			GameTitle = meta.GameTitle;

		// This comes from a client, so don't trust anything degenerate or absurdly large
		var bounds = new BBox( meta.BoundsMins, meta.BoundsMaxs );
		var size = bounds.Size;
		if ( size.x > 0f && size.y > 0f && size.z > 0f && size.x <= MaxBoundsSize && size.y <= MaxBoundsSize && size.z <= MaxBoundsSize )
			_fallbackBounds = bounds;
	}

	private async Task<bool> LoadAsync()
	{
		Model = await ResourceLibrary.LoadAsync<Model>( Path );
		Log.Info( $"[MountSpawner] path='{Path}' model={(HasModel ? "loaded" : "missing")} title='{GameTitle}'" );
		return true; // missing model uses placeholder
	}

	/// <summary>
	/// What we know about this mount (title and bounds), serialized to pass through spawn RPCs.
	/// </summary>
	public string Metadata => Json.Serialize( new MountInfo( GameTitle, Bounds.Mins, Bounds.Maxs ) );

	/// <summary>
	/// Resolve mount metadata on this machine, before asking the host to spawn it.
	/// </summary>
	public static async Task<string> ResolveMetadata( string path )
	{
		var spawner = new MountSpawner( path );
		await spawner.Loading;
		return spawner.Metadata;
	}

	public void DrawPreview( Transform transform, Material overrideMaterial )
	{
		var bounds = Bounds;
		var t = transform;
		t = new Transform( t.PointToWorld( bounds.Center ), t.Rotation, t.Scale * ( bounds.Size / 50f ) );
		Game.ActiveScene.DebugOverlay.Model( HasModel ? Model : Model.Cube, transform: t, overlay: false, materialOveride: overrideMaterial );
	}

	public Task<List<GameObject>> Spawn( Transform transform, Player player )
	{
		var bounds = Bounds;
		transform.Position += transform.Up * -bounds.Mins.z;

		var go = new GameObject( false, "prop" );
		go.Tags.Add( "removable" );
		go.WorldTransform = transform;

		var prop = go.AddComponent<Prop>();
		prop.Model = Model;

		if ( HasModel )
		{
			if ( (Model.Physics?.Parts?.Count ?? 0) == 0 )
			{
				var collider = go.AddComponent<BoxCollider>();
				collider.Scale = bounds.Size;
				collider.Center = bounds.Center;
				go.AddComponent<Rigidbody>();
			}
		}
		else
		{
			var collider = go.AddComponent<BoxCollider>();
			collider.Scale = bounds.Size;
			collider.Center = bounds.Center;
			go.AddComponent<Rigidbody>();
		}

		var meta = go.AddComponent<MountMetadata>();
		meta.GameTitle = GameTitle;
		meta.BoundsSize = bounds.Size;
		meta.BoundsCenter = bounds.Center;

		Ownable.Set( go, player.Network.Owner );
		go.NetworkSpawn( true, null );

		return Task.FromResult( new List<GameObject> { go } );
	}
}
