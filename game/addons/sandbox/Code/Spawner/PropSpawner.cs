using Sandbox.Utility;

/// <summary>
/// Payload for spawning a prop model from a cloud ident.
/// </summary>
public sealed class PropSpawner : ISpawner
{
	static readonly BBox UnknownBounds = BBox.FromPositionAndSize( Vector3.Zero, 16f );
	static Material _loadingMaterial;

	public string DisplayName { get; private set; }
	public string FullIdent => Path is not null && !Path.EndsWith( ".vmdl" ) && !Path.EndsWith( ".vmdl_c" ) ? Path : null;
	public string Icon => Path;
	public string Data => Path;
	/// <inheritdoc/>
	public BBox Bounds => IsReady ? Model.Bounds : _previewBounds;
	public bool IsReady => Model is not null && !Model.IsError;
	public Task<bool> Loading { get; }

	public Model Model { get; private set; }
	public string Path { get; }

	Package _package;
	BBox _previewBounds;
	float _downloadFraction;

	BBox _lastBounds = UnknownBounds;
	BBox _drawnBox = BBox.FromHeightAndRadius( 1f, UnknownBounds.Size.x * 0.5f );
	BBox _resizeFrom;
	RealTimeUntil _resizeEnds;
	Transform _drawnTransform;
	RealTimeUntil _boxFadeEnds;

	public PropSpawner( string path )
	{
		Path = path;
		if ( FullIdent is not null )
			_previewBounds = UnknownBounds;

		Loading = LoadAsync();
	}

	private async Task<bool> LoadAsync()
	{
		// Try local/installed first, then fall back to cloud
		if ( Path.EndsWith( ".vmdl" ) || Path.EndsWith( ".vmdl_c" ) )
		{
			Model = await ResourceLibrary.LoadAsync<Model>( Path );
			if ( Model is not null )
			{
				DisplayName = Model.ResourceName;
				return true;
			}
		}

		_package = await Package.FetchAsync( Path, false );
		if ( _package?.TypeName != "model" ) return false;

		DisplayName = _package.Title;
		var mins = _package.GetMeta<Vector3?>( "RenderMins" );
		var maxs = _package.GetMeta<Vector3?>( "RenderMaxs" );
		if ( mins.HasValue && maxs.HasValue )
		{
			var bounds = new BBox( mins.Value, maxs.Value );
			if ( bounds.Size.IsFinite && bounds.Size != Vector3.Zero )
				_previewBounds = bounds;
		}

		Model = await Cloud.Load<Model>( Path );

		if ( Model is not null )
		{
			DisplayName = Model.ResourceName ?? DisplayName;
		}

		return IsReady;
	}

	/// <inheritdoc/>
	public void DrawPreview( Transform transform, Material overrideMaterial )
	{
		var scene = Game.ActiveScene;
		if ( IsReady )
		{
			scene.DebugOverlay.Model( Model, transform: transform, overlay: false, materialOveride: overrideMaterial );
			if ( _boxFadeEnds ) return;
		}
		else if ( FullIdent is null || Loading.IsCompleted )
		{
			return;
		}

		var surface = transform.Add( Vector3.Up * Bounds.Mins.z, worldSpace: false );
		DrawLoadingBox( scene, surface, finished: IsReady );
	}

	/// <summary>
	/// Draws the download preview on the placement surface. Returns false once it has faded out.
	/// </summary>
	internal bool DrawLoadingBox( Scene scene, Transform surface, bool finished )
	{
		if ( !finished )
		{
			_drawnTransform = surface;
			_boxFadeEnds = 0.3f;
		}

		if ( _boxFadeEnds ) return false;

		UpdateBoxSize( finished );
		var opacity = finished ? 1f - (float)_boxFadeEnds.Fraction : 1f;
		scene.DebugOverlay.Box( _drawnBox, new Color( 1f, 0.75f, 0.15f, opacity ), transform: _drawnTransform );

		if ( _drawnBox.Size.z > 0f )
		{
			_loadingMaterial ??= Material.Load( "materials/effects/duplicator_override_downloading.vmat" );
			var cube = _drawnTransform.ToWorld( new Transform( _drawnBox.Center, Rotation.Identity, _drawnBox.Size / Model.Cube.Bounds.Size ) );
			scene.DebugOverlay.Model( Model.Cube, Color.White.WithAlpha( opacity * opacity ), transform: cube, castShadows: false, materialOveride: _loadingMaterial );
		}

		return true;
	}

	void UpdateBoxSize( bool finished )
	{
		var bounds = Bounds;
		var boundsChanged = bounds != _lastBounds;
		if ( boundsChanged )
		{
			_resizeFrom = _drawnBox;
			_resizeEnds = 0.3f;
			_lastBounds = bounds;
		}

		if ( IsReady )
		{
			_downloadFraction = 1f;
		}
		else if ( !finished )
		{
			// The downloader can revise the byte count, but the box should only grow.
			_downloadFraction = MathF.Max( _downloadFraction, _package?.DownloadFraction ?? 0f );
		}

		var height = MathX.Lerp( MathF.Min( 1f, bounds.Size.z ), bounds.Size.z, _downloadFraction );
		_drawnBox = new BBox( bounds.Mins.WithZ( 0 ), bounds.Maxs.WithZ( height ) );
		if ( !_resizeEnds )
		{
			var t = Easing.EaseInOut( (float)_resizeEnds.Fraction );
			_drawnBox.Mins = _resizeFrom.Mins.LerpTo( _drawnBox.Mins, t );
			_drawnBox.Maxs = _resizeFrom.Maxs.LerpTo( _drawnBox.Maxs, t );
		}
	}

	/// <inheritdoc/>
	public Task<List<GameObject>> Spawn( Transform transform, Player player )
	{
		transform.Position -= transform.Up * Bounds.Mins.z;
		return SpawnAt( transform, player );
	}

	/// <summary>
	/// Spawns at the model origin already positioned by the placement preview.
	/// </summary>
	internal Task<List<GameObject>> SpawnAt( Transform transform, Player player )
	{
		var go = new GameObject( false, "prop" );
		go.Tags.Add( "removable" );
		go.WorldTransform = transform;

		var prop = go.AddComponent<Prop>();
		prop.Model = Model;

		if ( player.IsValid() )
			Ownable.Set( go, player.Network.Owner );

		if ( (Model.Physics?.Parts?.Count ?? 0) == 0 )
		{
			var collider = go.AddComponent<BoxCollider>();
			collider.Scale = Model.Bounds.Size;
			collider.Center = Model.Bounds.Center;
			go.AddComponent<Rigidbody>();
		}

		go.NetworkSpawn( true, null );

		return Task.FromResult( new List<GameObject> { go } );
	}
}
