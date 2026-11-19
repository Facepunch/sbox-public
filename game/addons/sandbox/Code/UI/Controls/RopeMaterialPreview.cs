namespace Sandbox.UI;

/// <summary>
/// One render behind a whole row of rope material swatches: a rope per swatch, side by side in a single scene,
/// each lined up under its swatch and seen from above like a rope lying on the ground. One scene rather
/// than a panel per swatch, since only the first few of those ever drew.
/// </summary>
public sealed class RopeMaterialPreview : ScenePanel
{
	/// <summary>
	/// A rope takes this much of its swatch's width.
	/// </summary>
	const float Fill = 0.7f;

	/// <summary>
	/// World units from the top of the render to the bottom.
	/// </summary>
	const float ViewHeight = 40f;

	/// <summary>
	/// Texture repeat along the rope for each unit of its radius - what looked like a placed rope when compared.
	/// </summary>
	const float TexturePerRadius = 32f / 12f;

	/// <summary>
	/// Roughly the swatch row's own background, it shows in the gaps.
	/// </summary>
	static readonly Color Background = new( 0.16f, 0.16f, 0.17f, 1f );

	sealed class Rope
	{
		public Panel Cell;
		public string Path;
		public bool Glow;
		public LineRenderer Line;
		public int Request;
		public Vector2 LaidOutFor;
	}

	readonly List<Rope> _ropes = new();
	CameraComponent _camera;
	Vector2 _size;
	Color _tint = Color.White;

	/// <summary>
	/// Multiplies every rope's color, like the rope tool's tint does on a placed rope.
	/// </summary>
	public Color Tint
	{
		get => _tint;
		set
		{
			if ( _tint == value ) return;
			_tint = value;

			foreach ( var rope in _ropes )
				if ( rope.Line.IsValid() ) rope.Line.Color = value;
		}
	}

	public RopeMaterialPreview()
	{
		RenderOnce = false;
	}

	/// <summary>
	/// The swatches to draw a rope under, in order, with the material each one shows.
	/// </summary>
	public void SetSwatches( IReadOnlyList<(Panel Cell, string Path, bool Glow)> swatches )
	{
		EnsureScene();

		while ( _ropes.Count > swatches.Count )
		{
			_ropes[^1].Line?.GameObject?.Destroy();
			_ropes.RemoveAt( _ropes.Count - 1 );
		}

		for ( var i = 0; i < swatches.Count; i++ )
		{
			if ( i >= _ropes.Count ) _ropes.Add( new Rope { Line = CreateLine() } );

			var rope = _ropes[i];
			var (cell, path, glow) = swatches[i];

			if ( rope.Cell != cell ) rope.LaidOutFor = default;
			rope.Cell = cell;

			if ( rope.Glow != glow || rope.Path != path )
			{
				rope.Glow = glow;
				ApplyStyle( rope );
			}

			if ( rope.Path != path )
			{
				rope.Path = path;
				_ = LoadInto( rope );
			}
		}
	}

	void EnsureScene()
	{
		if ( _camera.IsValid() ) return;

		using ( RenderScene.Push() )
		{
			// straight down at ropes lying along X, with X as the frame's up so they run top to bottom
			_camera = new GameObject( true, "camera" ).AddComponent<CameraComponent>();
			_camera.WorldPosition = new Vector3( 0f, 0f, 100f );
			_camera.WorldRotation = Rotation.LookAt( Vector3.Down, Vector3.Forward );
			_camera.Orthographic = true;
			_camera.OrthographicHeight = ViewHeight;
			_camera.ZNear = 1f;
			_camera.ZFar = 1000f;
			_camera.BackgroundColor = Background;

			new GameObject( true, "ambient" ).AddComponent<AmbientLight>().Color = Color.White * 0.55f;

			// from above, tilted only along the ropes (X): a sideways tilt darkens one side of each rope into the
			// background and makes it look off centre in its swatch. Both edges still fall off, so they read round
			var key = new GameObject( true, "key" ).AddComponent<DirectionalLight>();
			key.WorldRotation = Rotation.LookAt( new Vector3( -0.5f, 0f, -1f ).Normal );
			key.LightColor = Color.White * 1.5f;
			key.Shadows = false;
		}
	}

	LineRenderer CreateLine()
	{
		using ( RenderScene.Push() )
		{
			var line = new GameObject( true, "rope" ).AddComponent<LineRenderer>();
			line.UseVectorPoints = true;
			line.Color = _tint;
			line.Face = SceneLineObject.FaceMode.Cylinder;
			line.CastShadows = false;
			line.Enabled = false;
			return line;
		}
	}

	static void ApplyStyle( Rope rope )
	{
		if ( !rope.Line.IsValid() ) return;

		// the way the rope tool draws beams
		rope.Line.Lighting = !rope.Glow;
		rope.Line.Opaque = !rope.Glow;
		rope.Line.Additive = rope.Glow;
	}

	async Task LoadInto( Rope rope )
	{
		var id = ++rope.Request;
		var path = rope.Path;
		Sandbox.Material material = null;

		try
		{
			material = await LoadMaterial( path );
		}
		catch ( Exception e )
		{
			Log.Warning( $"Couldn't preview rope material '{path}': {e.Message}" );
		}

		if ( !IsValid || id != rope.Request || !rope.Line.IsValid() ) return;

		if ( material is null )
			Log.Warning( $"Rope preview has no material for '{path}'" );

		rope.Line.Enabled = material is not null;
		if ( material is not null )
			rope.Line.Texturing = rope.Line.Texturing with { Material = material, WorldSpace = true };

		rope.LaidOutFor = default;
	}

	/// <summary>
	/// Workshop materials are made for models, they go through the same conversion the placed rope does.
	/// </summary>
	static async Task<Sandbox.Material> LoadMaterial( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) ) return null;
		if ( LineMaterial.IsPackageIdent( path ) ) return await LineMaterial.LoadAsync( path );

		return Sandbox.Material.Load( path );
	}

	public override void Tick()
	{
		base.Tick();

		if ( !_camera.IsValid() ) return;

		var size = Box.Rect.Size;
		if ( size.x <= 0f || size.y <= 0f ) return;

		var resized = size != _size;
		_size = size;

		// without this the projection takes the screen's aspect, not the panel's, and squeezes the row sideways
		if ( resized ) _camera.CustomSize = size;

		// world units per screen pixel, the same both ways with an orthographic camera
		var unitsPerPixel = ViewHeight / size.y;
		var right = _camera.WorldRotation.Right;
		var along = _camera.WorldRotation.Up;

		foreach ( var rope in _ropes )
		{
			if ( !rope.Line.IsValid() || !rope.Cell.IsValid() ) continue;

			// only lay a rope out again when its swatch moved or changed size
			var cell = rope.Cell.Box.Rect;
			var key = new Vector2( cell.Left - Box.Rect.Left, cell.Width );
			if ( !resized && key == rope.LaidOutFor ) continue;
			rope.LaidOutFor = key;

			var offset = (cell.Center.x - Box.Rect.Left - size.x * 0.5f) * unitsPerPixel;
			var centre = right * offset;
			var radius = cell.Width * Fill * 0.5f * unitsPerPixel;

			// a line's Width is its radius, the rope tool sets it from Radius
			rope.Line.Width = radius;
			rope.Line.VectorPoints = [centre - along * 200f, centre + along * 200f];
			rope.Line.Texturing = rope.Line.Texturing with { WorldSpace = true, UnitsPerTexture = radius * TexturePerRadius };
		}
	}
}
