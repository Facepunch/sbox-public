namespace Sandbox.Mounting;

public static class MountUtility
{
	static readonly Dictionary<string, Texture> _cache = new();
	static readonly List<RenderJob> _jobs = new();
	static readonly HashSet<RenderJob> _activeJobs = new();

	/// <summary>
	/// Is this a mount:// resource path? Resources behind such a path are provided by a
	/// mounted game and are cached/reused rather than loaded from disk.
	/// </summary>
	public static bool IsMountPath( string path )
	{
		return !string.IsNullOrWhiteSpace( path ) && path.StartsWith( "mount://", StringComparison.Ordinal );
	}

	/// <summary>
	/// Attempt to parse an ident from a mount path
	/// </summary>
	public static bool TryParse( string path, out string ident )
	{
		ident = default;

		if ( !IsMountPath( path ) )
			return false;

		var sourceName = path[8..];
		var i = sourceName.IndexOf( '/' );

		if ( i < 0 )
			return false;

		ident = sourceName[..i];
		return true;
	}

	/// <summary>
	/// Find a ResourceLoader by its mount path.
	/// </summary>
	public static ResourceLoader FindLoader( string loaderPath )
	{
		if ( !IsMountPath( loaderPath ) ) return null;

		var partIndex = loaderPath.IndexOf( '/', 8 );
		if ( partIndex <= 8 ) return null;
		var mountName = loaderPath[8..partIndex];

		var mount = Directory.Get( mountName );
		if ( mount is null ) return null;

		return mount.GetByPath( loaderPath );
	}

	/// <summary>
	/// Create a preview texture for the given resource loader.
	/// </summary>
	public static Texture GetPreviewTexture( string loaderPath )
	{
		var loader = FindLoader( loaderPath );
		if ( loader is null ) return null;
		return GetPreviewTexture( loader );
	}

	/// <summary>
	/// Create a preview texture for the given resource loader.
	/// </summary>
	public static Texture GetPreviewTexture( ResourceLoader loader )
	{
		Assert.NotNull( loader, "loader was null" );

		var path = loader.Path;

		// In-memory cache
		if ( _cache.TryGetValue( path, out var cached ) )
			return cached;

		// Disk cache
		var cacheKey = $"preview/icons/{path.Md5()}.png";
		if ( FileSystem.Cache.TryGet( cacheKey, out var data ) )
		{
			using var bitmap = Bitmap.CreateFromBytes( data );
			var tex = bitmap.ToTexture( true );
			_cache[path] = tex;
			return tex;
		}

		// Queue render job
		var job = new RenderJob( path, cacheKey );
		_jobs.Add( job );
		_cache[path] = job.Texture;

		return job.Texture;
	}

	internal static void TickPreviewRenders()
	{
		_activeJobs.RemoveWhere( x => x.IsFinished );

		while ( _jobs.Count > 0 && _activeJobs.Count < 2 )
		{
			var job = _jobs.OrderBy( x => x.Texture.LastUsed ).First();
			_jobs.Remove( job );

			_activeJobs.Add( job );
			job.Start();
		}
	}

	internal static void FlushCache()
	{
		_jobs.Clear();
		_activeJobs.Clear();
		_cache.Clear();
	}

	[ConCmd( "mount_generatethumbs", Help = "Generate a thumbnail for all scenes from a mount", Flags = ConVarFlags.Protected )]
	internal static async void GenerateThumbs( string ident )
	{
		var mount = Directory.Get( ident );
		if ( mount is null )
		{
			Log.Error( $"Mount '{ident}' not found" );
			return;
		}

		IEnumerable<ResourceLoader> scenes = mount.GetAll( ResourceType.Scene );
		Log.Info( $"Generating thumbs for {scenes.Count()} scene(s) from {mount.Title}.." );

		foreach ( var sceneLoader in scenes )
		{
			await GenerateMapThumb( sceneLoader );
		}

		Log.Info( "..complete!" );
	}

	[ConCmd( "mount_generatethumb", Help = @"Generate a thumbnail for a mount scene", Flags = ConVarFlags.Protected )]
	internal static async void GenerateMapThumb( string path )
	{
		if ( !path.EndsWith( ".scene", StringComparison.OrdinalIgnoreCase ) )
		{
			path = $"{path}.scene";
		}

		var loader = FindLoader( path );
		if ( loader is null || loader.Type != ResourceType.Scene )
		{
			Log.Error( $"Mount scene not found: '{path}'" );
			return;
		}

		await GenerateMapThumb( loader );
	}

	static async Task<bool> GenerateMapThumb( ResourceLoader sceneLoader )
	{
		SceneFile sceneFile = null;
		var scene = Scene.CreateEditorScene();

		try
		{
			using var scope = scene.Push();

			sceneFile = await sceneLoader.GetOrCreate() as SceneFile;
			if ( sceneFile is null || !scene.Load( sceneFile ) )
			{
				Log.Error( $"Failed to load scene: '{sceneLoader.Path}'" );
				return false;
			}

			await Task.Yield();

			var go = new GameObject( true, "camera" );
			var cc = go.AddComponent<CameraComponent>();
			cc.BackgroundColor = Color.Transparent;
			cc.WorldRotation = new Angles( 20, 180 + 45, 0 );
			cc.FieldOfView = 90.0f;
			cc.ZFar = 100000.0f;
			cc.ZNear = 0.1f;

			var transform = scene.FindAllWithTag( "map_preview" ).FirstOrDefault()
				?? scene.GetComponentInChildren<SpawnPoint>()?.GameObject;
			if ( transform.IsValid() )
			{
				cc.WorldTransform = transform.WorldTransform;
			}

			byte[] bytes;
			using ( var bitmap = new Bitmap( 1280, 720 ) )
			{
				cc.RenderToBitmap( bitmap );
				bytes = bitmap.ToPng();
			}

			var path = EngineFileSystem.Root.GetFullPath( "/" );
			if ( string.IsNullOrWhiteSpace( path ) ) return false;

			path += $"/mount/{sceneLoader._mount.Ident}/assets/thumbs/{sceneLoader._mount.Ident}/{System.IO.Path.GetDirectoryName( sceneLoader.RelativePath )}/";
			System.IO.Directory.CreateDirectory( path );

			path += System.IO.Path.GetFileName( sceneLoader.RelativePath ).WithExtension( ".png" );
			System.IO.File.WriteAllBytes( path, bytes );

			Log.Info( $"Written {path}" );
		}
		catch ( Exception ex )
		{
			Log.Error( ex, $"Failed to generate thumbnail for scene: '{sceneLoader.Path}'" );
		}
		finally
		{
			sceneFile?.Destroy();
			scene?.Destroy();

			// cleanup before we continue
			GC.Collect();
			GC.WaitForPendingFinalizers();
			MainThread.RunQueues();
		}

		return true;
	}
}

class RenderJob
{
	public string Path { get; }
	public Texture Texture { get; }

	Task _task;
	readonly string _cacheKey;

	public bool IsFinished => _task is null || _task.IsCompleted;

	public RenderJob( string path, string cacheKey )
	{
		_cacheKey = cacheKey;
		Path = path;

		using var bitmap = new Bitmap( 256, 256 );
		bitmap.Clear( Color.Transparent );
		Texture = bitmap.ToTexture( true );
	}

	public void Start()
	{
		_task = Run();
	}

	public async Task Run()
	{
		var modelResource = await Model.LoadAsync( Path );
		if ( !modelResource.IsValid() ) return;

		using var bitmap = new Bitmap( 512, 512 );
		SceneUtility.RenderModelBitmap( modelResource, bitmap );

		using var resized = bitmap.Resize( 256, 256 );
		Texture.Update( resized );

		FileSystem.Cache.Set( _cacheKey, resized.ToPng() );
	}
}
