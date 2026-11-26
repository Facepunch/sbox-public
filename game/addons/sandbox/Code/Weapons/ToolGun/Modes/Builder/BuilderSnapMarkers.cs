public sealed class BuilderSnapMarkers
{
	static readonly Color Color = new( 0.2f, 0.6f, 1f );
	static readonly Color GuideColor = new( 0.78f, 0.92f, 1f );

	const float MarkerSize = 18f;
	const float MarkerBar = 3f;
	const float CursorArm = 48f;
	const float CursorWidth = 2f;
	const float GuideWidth = 3f;
	const float GuideDash = 10f;
	const float GuidePulseRate = 6f;
	const float GuideTailShare = 0.5f;
	const float GuideTailView = 0.25f;
	const float BoundsPixel = 0.002f;

	sealed class Overlay : SceneDynamicObject
	{
		public Overlay( SceneWorld world ) : base( world )
		{
			Transform = Transform.Zero;
			Material = Material.FromShader( "shaders/builder_overlay.shader" );
			Flags.CastShadows = false;
			Flags.IsOpaque = false;
			Flags.IsTranslucent = true;
			RenderLayer = SceneRenderLayer.OverlayWithoutDepth;
		}
	}

	Overlay _overlay;
	SceneWorld _world;

	readonly List<BuilderPlane> _faces = new();
	int _count;
	Vector3 _min;
	Vector3 _max;

	public void Begin( SceneWorld world )
	{
		_world = world;
		_faces.Clear();
		_count = 0;
		_min = new Vector3( float.MaxValue );
		_max = new Vector3( float.MinValue );
	}

	public void Add( in BuilderPlane plane, Vector3 eyes )
	{
		if ( !plane.HasFace ) return;

		foreach ( var face in _faces )
		{
			if ( IsSameFace( face, plane ) ) return;
		}

		_faces.Add( plane );

		var extent = plane.FaceMax - plane.FaceMin;
		var lift = plane.Normal * 0.1f;

		for ( int i = 0; i < 3; i++ )
		for ( int j = 0; j < 3; j++ )
			Point( plane.ToWorld( new Vector2( plane.FaceMin.x + extent.x * i * 0.5f, plane.FaceMin.y + extent.y * j * 0.5f ) ) + lift, eyes );
	}

	public void AddGuide( Vector3 from, Vector3 to, Vector3 direction, bool extend, Vector3 eyes )
	{
		var length = from.Distance( to );
		var tail = extend ? MathF.Max( length * GuideTailShare, to.Distance( eyes ) * GuideTailView ) : 0;
		var total = length + tail;
		if ( total < 0.01f ) return;

		var pulse = 0.94f + 0.06f * MathF.Sin( RealTime.Now * GuidePulseRate );
		Line( from, from + direction * total, length, GuideWidth, GuideDash, (GuideColor * pulse).WithAlpha( 1 ), eyes );
	}

	public void AddCursor( Vector3 position, Vector3 right, Vector3 forward, Vector3 eyes )
	{
		var lift = Vector3.Cross( right, forward ).Normal * 0.1f;
		position += lift;

		Line( position, position + right * CursorArm, 0, CursorWidth, 0, Color, eyes );
		Line( position, position - right * CursorArm, 0, CursorWidth, 0, Color, eyes );
		Line( position, position + forward * CursorArm, 0, CursorWidth, 0, Color, eyes );
		Line( position, position - forward * CursorArm, 0, CursorWidth, 0, Color, eyes );
	}

	void Line( Vector3 from, Vector3 to, float solid, float width, float dash, Color tint, Vector3 eyes )
	{
		var color = tint.ToColor32();
		var total = from.Distance( to );
		var shape = new Vector3( 0, width, dash );

		Vertex Corner( Vector3 position, float side, float along ) => new()
		{
			Position = position,
			Color = color,
			Normal = shape,
			TexCoord0 = new Vector4( from, side ),
			TexCoord1 = new Vector4( along, solid, total, 0 ),
			Tangent = new Vector4( to, 0 )
		};

		Quad( Corner( from, -1, 0 ), Corner( to, -1, total ), Corner( to, 1, total ), Corner( from, 1, 0 ) );
		Grow( from, eyes, width );
		Grow( to, eyes, width );
	}

	void Point( Vector3 position, Vector3 eyes )
	{
		var color = Color.ToColor32();
		var shape = new Vector3( 1, MarkerSize, MarkerBar );

		Vertex Corner( float x, float y ) => new()
		{
			Position = position,
			Color = color,
			Normal = shape,
			TexCoord0 = new Vector4( x, y, 0, 0 )
		};

		Quad( Corner( -1, -1 ), Corner( 1, -1 ), Corner( 1, 1 ), Corner( -1, 1 ) );
		Grow( position, eyes, MarkerSize );
	}

	void Quad( Vertex a, Vertex b, Vertex c, Vertex d )
	{
		if ( !_overlay.IsValid() ) _overlay = new Overlay( _world );
		if ( _count == 0 ) _overlay.Init( Graphics.PrimitiveType.Triangles );
		_count++;

		_overlay.AddVertex( a );
		_overlay.AddVertex( b );
		_overlay.AddVertex( c );
		_overlay.AddVertex( a );
		_overlay.AddVertex( c );
		_overlay.AddVertex( d );
	}

	void Grow( Vector3 position, Vector3 eyes, float pixels )
	{
		var margin = new Vector3( position.Distance( eyes ) * BoundsPixel * pixels + 1 );
		_min = _min.ComponentMin( position - margin );
		_max = _max.ComponentMax( position + margin );
	}

	static bool IsSameFace( in BuilderPlane a, in BuilderPlane b )
	{
		const float tolerance = 0.01f;
		return a.Origin.Distance( b.Origin ) < tolerance && Vector3.Dot( a.Normal, b.Normal ) > 0.9999f && Vector3.Dot( a.Right, b.Right ) > 0.9999f
			&& a.FaceMin.Distance( b.FaceMin ) < tolerance && a.FaceMax.Distance( b.FaceMax ) < tolerance;
	}

	public void End()
	{
		if ( _count == 0 )
		{
			Hide();
			return;
		}

		_overlay.Bounds = new BBox( _min, _max );
		_overlay.RenderingEnabled = true;
	}

	public void Hide()
	{
		if ( _overlay.IsValid() ) _overlay.RenderingEnabled = false;
	}

	public void Destroy()
	{
		_overlay?.Delete();
		_overlay = null;
	}
}
