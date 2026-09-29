namespace Sandbox.Rendering;

/// <summary>
/// The kind of light a shadow is for - the values of native's <c>LightType_t</c>.
/// </summary>
internal enum ShadowLightType
{
	Point = 1,
	Directional = 2,
	Spot = 3,
}

/// <summary>
/// A light as <see cref="ShadowMapper"/> sees it. Filled in from a native <see cref="SceneLight"/>, or from
/// a light in another renderer, so the same shadow code serves both.
/// </summary>
internal struct ShadowLight
{
	/// <summary>
	/// Identifies the light in the shadow map cache - the <see cref="SceneLight"/>, or whatever the other
	/// renderer's light is. Held weakly.
	/// </summary>
	public object Key;

	public ShadowLightType Type;
	public Vector3 Position;
	public Rotation Rotation;

	/// <summary>
	/// Changes whenever the light moves, which invalidates its cached shadow.
	/// </summary>
	public int TransformVersion;

	public float Radius;

	/// <summary>
	/// A spot light's outer cone angle from its axis, in degrees.
	/// </summary>
	public float ConeOuter;

	public float Hardness;
	public float Bias;

	/// <summary>
	/// Baked lights leave static casters to the lightmaps.
	/// </summary>
	public bool Baked;

	/// <summary>
	/// A light that doesn't move keeps its static casters in a cache, and only re-renders dynamic ones.
	/// </summary>
	public bool Static;

	/// <summary>
	/// Directional: whether it casts at all.
	/// </summary>
	public bool ShadowsEnabled;

	/// <summary>
	/// Directional: the way the light travels.
	/// </summary>
	public Vector3 Direction;

	/// <summary>
	/// Directional: color and fog strength, for <see cref="GPUDirectionalLight"/>.
	/// </summary>
	public Vector3 Color;
	public float FogStrength;

	public int Cascades;
	public float CascadeSplitRatio;
}

/// <summary>
/// The view shadows are being made for: sun cascades are fit to it, and local shadow maps sized by it.
/// </summary>
internal struct ShadowViewInfo
{
	/// <summary>
	/// Inverse of the view's reverse-Z world to projection, row-vector convention, absolute world space.
	/// </summary>
	public System.Numerics.Matrix4x4 InverseViewProjection;

	public Vector3 CameraPosition;

	/// <summary>
	/// The main viewport's size in pixels.
	/// </summary>
	public Vector2 ViewportSize;

	/// <summary>
	/// A 3D skybox view: fully static and baked, so the sun lights it but casts no cascades.
	/// </summary>
	public bool IsSkybox;
}

/// <summary>
/// One view to render into a shadow map: a sun cascade, a spot light, or a cube face.
/// </summary>
internal struct ShadowViewDesc
{
	public string Name;

	public Vector3 Position;
	public Rotation Rotation;

	public bool Orthographic;

	/// <summary>
	/// Perspective: the square view's field of view, in degrees.
	/// </summary>
	public float FieldOfView;

	/// <summary>
	/// Orthographic: the view's size in world units.
	/// </summary>
	public float Width, Height;

	public float ZNear, ZFar;

	/// <summary>
	/// The square viewport, in pixels.
	/// </summary>
	public int Resolution;

	public ShadowMap Target;

	/// <summary>
	/// The cube face to render, for a cube target.
	/// </summary>
	public int Slice;

	public SceneObjectFlags RequiredFlags;
	public SceneObjectFlags ExcludedFlags;

	/// <summary>
	/// Rasterizer depth bias - native's <c>RsDepthBiasStateOverride_t</c>.
	/// </summary>
	public int DepthBias;
	public float SlopeScaledDepthBias;

	/// <summary>
	/// Casters entirely inside an orthographic box <see cref="ExclusionSize"/> across at
	/// <see cref="ExclusionCenter"/>, turned like this view, are left out - they're in an earlier cascade.
	/// </summary>
	public bool HasExclusion;
	public Vector3 ExclusionCenter;
	public float ExclusionSize;

	/// <summary>
	/// Static casters already rendered into this map: it's copied in instead of clearing, and only
	/// dynamic casters are rendered on top.
	/// </summary>
	public ShadowMap CachedStatic;
}

/// <summary>
/// Renders the shadow views <see cref="ShadowMapper"/> asks for - native's scenesystem, or another
/// renderer.
/// </summary>
internal interface IShadowRenderer
{
	/// <summary>
	/// Render, or queue for rendering, a shadow view. Returns its world to projection matrix - reverse-Z,
	/// row-vector convention, absolute world space - which is what shaders look the shadow map up with.
	/// </summary>
	System.Numerics.Matrix4x4 RenderShadowView( in ShadowViewDesc view );

	/// <summary>
	/// The depth target a sun cascade renders into this frame.
	/// </summary>
	ShadowMap GetCascadeTarget( int cascade, int resolution );

	/// <summary>
	/// Bindless index of a directional light's contact shadow mask, or 0 for none.
	/// </summary>
	uint GetShadowMaskIndex( object light );
}

/// <summary>
/// A shadow map's depth texture, created the first time it's needed - so working out shadows is pure CPU
/// work, and a renderer can create the textures later, when it records.
/// </summary>
internal sealed class ShadowMap : IDisposable
{
	Texture texture;

	public int Resolution { get; }
	public bool IsCube { get; }
	public ImageFormat Format { get; }

	public ShadowMap( int resolution, bool isCube, ImageFormat format )
	{
		Resolution = resolution;
		IsCube = isCube;
		Format = format;
	}

	/// <summary>
	/// A shadow map for a texture that already exists.
	/// </summary>
	public ShadowMap( Texture texture )
	{
		this.texture = texture;
		Resolution = texture.Width;
		IsCube = false;
		Format = texture.ImageFormat;
	}

	public bool HasTexture => texture is not null;

	public Texture Texture => texture ??= Create();

	Texture Create()
	{
		if ( IsCube )
			return Texture.CreateCube( Resolution, Resolution, Format ).AsRenderTarget().Finish();

		return Texture.CreateRenderTarget( $"ShadowPool_{Resolution}", Format, new Vector2( Resolution ) );
	}

	public void Dispose()
	{
		texture?.Dispose();
		texture = null;
	}
}
