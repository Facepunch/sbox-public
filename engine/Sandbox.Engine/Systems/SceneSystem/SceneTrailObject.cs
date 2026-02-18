
using Sandbox.Rendering;

namespace Sandbox;

/// <summary>
/// I don't want to expose SceneObjects to people anymore. They should be using the scene system.
/// It's better if we can keep things internal so we don't have the burden of supporting api layout.
/// </summary>
internal class SceneTrailObject : SceneLineObject
{
	record struct TrailPoint( Vector3 Position, Vector3 Normal, float Uv, float Delta );

	List<TrailPoint> _trail = new List<TrailPoint>( 32 );
	TrailPoint? lastPoint;

	/// <summary>
	/// Total maximum points we're allowing
	/// </summary>
	public int MaxPoints { get; set; } = 32;

	/// <summary>
	/// Wait until we're this far away before adding a new point
	/// </summary>
	public float PointDistance { get; set; } = 5;

	/// <summary>
	/// Texture details, in a nice stuct to hide the bs
	/// </summary>
	public TrailTextureConfig Texturing { get; set; } = TrailTextureConfig.Default;

	public Gradient TrailColor { get; set; } = global::Color.White;
	public Curve Width { get; set; } = 10;
	public Color LineTint { get; set; } = Color.White;
	public float LineScale { get; set; } = 1;

	public BlendMode BlendMode
	{
		get => Attributes.GetComboEnum( "D_BLENDMODE", BlendMode.Normal );
		set => Attributes.SetComboEnum( "D_BLENDMODE", value );
	}

	/// <summary>
	/// How long the trail lasts - or 0 for infinite
	/// </summary>
	public float LifeTime { get; set; } = 1.0f;

	float scrollTime = 0;

	public SceneTrailObject( SceneWorld sceneWorld ) : base( sceneWorld )
	{
		BlendMode = BlendMode.Normal;
	}

	public bool IsEmpty => _trail.Count <= 1;
	public int PointCount => _trail.Count;

	/// <summary>
	/// Try to add a position to this trail. Returns true on success.
	/// </summary>
	public bool TryAddPosition( Vector3 worldPosition )
	{
		return TryAddPosition( worldPosition, Vector3.Up );
	}

	/// <summary>
	/// Try to add a position to this trail. Returns true on success.
	/// </summary>
	public bool TryAddPosition( Vector3 worldPosition, Vector3 worldNormal )
	{
		float distance = lastPoint?.Position.Distance( worldPosition ) ?? 0;

		if ( lastPoint.HasValue && distance < PointDistance )
			return false;

		float uv = lastPoint?.Uv ?? 0;

		uv += distance;

		lastPoint = new TrailPoint( worldPosition, worldNormal, uv, 1 );
		_trail.Add( lastPoint.Value );

		while ( _trail.Count > MaxPoints )
		{
			_trail.RemoveAt( 0 );
		}

		return true;
	}

	/// <summary>
	/// Advance the time for this trail. Will fade out points and scoll the texture.
	/// </summary>
	public void AdvanceTime( float f )
	{
		scrollTime += f * Texturing.Scroll;

		if ( LifeTime <= 0 )
			return;

		f = f / LifeTime;

		for ( int i = 0; i < _trail.Count; i++ )
		{
			var e = _trail[i];
			e.Delta -= f;

			if ( e.Delta <= 0 )
			{
				_trail.RemoveAt( i );
				i--;
				continue;
			}

			_trail[i] = e;
		}
	}

	/// <summary>
	/// Build the vertices for this object.
	/// TODO: We can move this to build automatically in a thread
	/// </summary>
	public void Build()
	{
		var count = _trail.Count();
		if ( count <= 1 )
		{
			Clear();
			return;
		}

		Material = Texturing.Material;

		StartLine();

		for ( int j = count - 1; j >= 0; j-- )
		{
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
			AddLinePoint( p.Position, p.Normal, TrailColor.Evaluate( delta ) * LineTint, width * LineScale, uv );
		}

		EndLine();
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
