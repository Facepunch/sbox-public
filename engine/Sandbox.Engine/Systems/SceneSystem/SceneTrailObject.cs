
using Sandbox.Rendering;

namespace Sandbox;

/// <summary>
/// I don't want to expose SceneObjects to people anymore. They should be using the scene system.
/// It's better if we can keep things internal so we don't have the burden of supporting api layout.
/// </summary>
internal class SceneTrailObject : SceneLineObject
{
	readonly TrailData _data = new();

	/// <summary>
	/// Total maximum points we're allowing
	/// </summary>
	public int MaxPoints { get => _data.MaxPoints; set => _data.MaxPoints = value; }

	/// <summary>
	/// Wait until we're this far away before adding a new point
	/// </summary>
	public float PointDistance { get => _data.PointDistance; set => _data.PointDistance = value; }

	/// <summary>
	/// Texture details, in a nice stuct to hide the bs
	/// </summary>
	public TrailTextureConfig Texturing { get => _data.Texturing; set => _data.Texturing = value; }

	public Gradient TrailColor { get => _data.TrailColor; set => _data.TrailColor = value; }
	public Curve Width { get => _data.Width; set => _data.Width = value; }
	public Color LineTint { get => _data.LineTint; set => _data.LineTint = value; }
	public float LineScale { get => _data.LineScale; set => _data.LineScale = value; }

	public BlendMode BlendMode
	{
		get => Attributes.GetComboEnum( "D_BLENDMODE", BlendMode.Normal );
		set => Attributes.SetComboEnum( "D_BLENDMODE", value );
	}

	/// <summary>
	/// How long the trail lasts - or 0 for infinite
	/// </summary>
	public float LifeTime { get => _data.LifeTime; set => _data.LifeTime = value; }

	public SceneTrailObject( SceneWorld sceneWorld ) : base( sceneWorld )
	{
		BlendMode = BlendMode.Normal;
	}

	public bool IsEmpty => _data.IsEmpty;
	public int PointCount => _data.PointCount;

	/// <summary>
	/// Try to add a position to this trail. Returns true on success.
	/// </summary>
	public bool TryAddPosition( Vector3 worldPosition ) => _data.TryAddPosition( worldPosition );

	/// <summary>
	/// Try to add a position to this trail. Returns true on success.
	/// </summary>
	public bool TryAddPosition( Vector3 worldPosition, Vector3 worldNormal ) => _data.TryAddPosition( worldPosition, worldNormal );

	/// <summary>
	/// Advance the time for this trail. Will fade out points and scoll the texture.
	/// </summary>
	public void AdvanceTime( float f ) => _data.AdvanceTime( f );

	/// <summary>
	/// Build the vertices for this object.
	/// TODO: We can move this to build automatically in a thread
	/// </summary>
	public void Build()
	{
		var count = _data.PointCount;
		if ( count <= 1 )
		{
			Clear();
			return;
		}

		Material = Texturing.Material;

		StartLine();

		for ( int j = count - 1; j >= 0; j-- )
		{
			var p = _data.EvaluatePoint( j );
			AddLinePoint( p.Position, p.Normal, p.Color, p.Width, p.TextureCoord );
		}

		EndLine();
	}
}

/// <summary>
/// The points of a single trail, and how they age. Kept apart from any scene object so that
/// many trails can be drawn by one <see cref="SceneLineObject"/> (see <see cref="ParticleTrailRenderer"/>).
/// </summary>
internal sealed class TrailData
{
	record struct TrailPoint( Vector3 Position, Vector3 Normal, float Uv, float Delta );

	readonly List<TrailPoint> _trail = new List<TrailPoint>( 32 );
	TrailPoint? lastPoint;

	public int MaxPoints { get; set; } = 32;
	public float PointDistance { get; set; } = 5;
	public TrailTextureConfig Texturing { get; set; } = TrailTextureConfig.Default;
	public Gradient TrailColor { get; set; } = global::Color.White;
	public Curve Width { get; set; } = 10;
	public Color LineTint { get; set; } = Color.White;
	public float LineScale { get; set; } = 1;
	public float LifeTime { get; set; } = 1.0f;

	/// <summary>
	/// Nothing adds points to this trail anymore, it's only fading out.
	/// </summary>
	public bool Orphaned { get; set; }

	float scrollTime = 0;

	public bool IsEmpty => _trail.Count <= 1;
	public int PointCount => _trail.Count;

	public bool TryAddPosition( Vector3 worldPosition )
	{
		return TryAddPosition( worldPosition, Vector3.Up );
	}

	public bool TryAddPosition( Vector3 worldPosition, Vector3 worldNormal )
	{
		float distance = lastPoint?.Position.Distance( worldPosition ) ?? 0;

		if ( lastPoint.HasValue && distance < PointDistance )
			return false;

		float uv = lastPoint?.Uv ?? 0;

		uv += distance;

		lastPoint = new TrailPoint( worldPosition, worldNormal, uv, 1 );
		_trail.Add( lastPoint.Value );

		if ( _trail.Count > MaxPoints )
		{
			_trail.RemoveRange( 0, _trail.Count - MaxPoints );
		}

		return true;
	}

	public void AdvanceTime( float f )
	{
		scrollTime += f * Texturing.Scroll;

		if ( LifeTime <= 0 )
			return;

		f = f / LifeTime;

		int expired = 0;

		for ( int i = 0; i < _trail.Count; i++ )
		{
			var e = _trail[i];
			e.Delta -= f;

			if ( e.Delta <= 0 )
			{
				expired = i + 1;
				continue;
			}

			_trail[i] = e;
		}

		if ( expired > 0 )
		{
			_trail.RemoveRange( 0, expired );
		}
	}

	/// <summary>
	/// Remove every point, the trail will be empty.
	/// </summary>
	public void Clear()
	{
		_trail.Clear();
	}

	/// <summary>
	/// Evaluate the line point for trail point <paramref name="j"/>, with color, width and texture coordinate applied.
	/// </summary>
	public SceneLineObject.LinePoint EvaluatePoint( int j )
	{
		var count = _trail.Count;
		var p = _trail[j];

		float delta = 1 - ((float)j / (float)count);

		float uv = 0;

		if ( !Texturing.WorldSpace )
		{
			uv = delta * Texturing.Scale;
		}
		else
		{
			uv = p.Uv / Texturing.UnitsPerTexture;
		}

		uv += scrollTime + Texturing.Offset;

		delta = 1 - (p.Delta * (1 - delta));
		float width = Width.Evaluate( delta ) * 0.5f;

		return new SceneLineObject.LinePoint( 0, p.Position, p.Normal, TrailColor.Evaluate( delta ) * LineTint, width * LineScale, uv );
	}

	/// <summary>
	/// Write this trail as one strip into <paramref name="dest"/>, newest point first, flagged for
	/// <see cref="SceneLineObject.SetStrips"/>. Returns the number of points written, which is 0 if the
	/// trail has fewer than 2 distinct points. <paramref name="dest"/> needs room for <see cref="PointCount"/> points.
	/// </summary>
	public int WriteStrip( Span<SceneLineObject.LinePoint> dest, ref BBox bounds )
	{
		int written = 0;

		for ( int j = _trail.Count - 1; j >= 0; j-- )
		{
			var p = EvaluatePoint( j );

			if ( written > 0 && dest[written - 1].Position.DistanceSquared( p.Position ) < 0.01f )
				continue;

			bounds = written == 0 ? BBox.FromPositionAndSize( p.Position, 0 ) : bounds.AddPoint( p.Position );
			dest[written++] = p;
		}

		if ( written < 2 )
			return 0;

		dest[0].Offset |= SceneLineObject.StripStart;
		dest[written - 1].Offset |= SceneLineObject.StripEnd;

		return written;
	}
}

/// <summary>
/// Defines how a trail is going to be textured. Used by TrailRenderer.
/// </summary>
[Expose]
public struct TrailTextureConfig
{
	public TrailTextureConfig()
	{

	}

	public static TrailTextureConfig Default { get; } = new TrailTextureConfig
	{
		WorldSpace = true,
		UnitsPerTexture = 10,
		Scale = 1,
		Offset = 0,
		Scroll = 0,
		FilterMode = FilterMode.Anisotropic,
		TextureAddressMode = TextureAddressMode.Wrap
	};

	[Hide, Obsolete( "Use Material property instead" )]
	public Texture Texture { get; set; }

	[KeyProperty, Validate( nameof( DoesMaterialUseLineShader ), "Material should derive from 'line.shader'.", LogLevel.Warn )]
	public Material Material { get; set; }

	[Group( "Texture Coordinates" )]
	[Property]
	public bool WorldSpace { get; set; }

	[Group( "Texture Coordinates" )]
	[ShowIf( "WorldSpace", true )]
	[Property]
	public float UnitsPerTexture { get; set; }

	[Group( "Texture Coordinates" )]
	[ShowIf( "WorldSpace", false )]
	[Property]
	public float Scale { get; set; }

	[Group( "Texture Coordinates" )]
	[Property]
	public float Offset { get; set; }

	[Group( "Texture Coordinates" )]
	[Property]
	public float Scroll { get; set; }

	[Group( "Texture Coordinates" )]
	[Property]
	public FilterMode FilterMode { get; set; } = FilterMode.Anisotropic;

	[Group( "Texture Coordinates" )]
	[Property]
	public TextureAddressMode TextureAddressMode { get; set; } = TextureAddressMode.Wrap;

	/// <summary>
	/// If true the texture will be clamped instead of repeating
	/// </summary>
	[Hide, Obsolete( "Use Texture Address Mode property instead" )]
	public bool Clamp { get; set; }

	public bool DoesMaterialUseLineShader( Material value )
	{
		// Null is fine, in that case we will load a default material
		return value == null || value.ShaderName == "shaders/line.shader";
	}
}
