namespace Basalt;

/// <summary>
/// Basalt's own debug views, which the host turns on (<see cref="RenderHost.DebugViews"/>). Each changes what frames draw;
/// with none on, frames are as they'd be without them. What they found goes to <see cref="RenderHost.PublishDebugViews"/>,
/// for the host's text: Basalt draws no text.
/// </summary>
[Flags]
public enum DebugViews
{
	/// <summary>
	/// Frames as they are.
	/// </summary>
	None = 0,

	/// <summary>
	/// Every mesh with LODs tinted by the LOD it drew at (<see cref="DebugViewReport.LodColor"/>). Meshes with one LOD keep
	/// their own tint.
	/// </summary>
	LodColors = 1,

	/// <summary>
	/// Meshes the main view culls on the CPU - too small on screen, or left out by the camera's tags - drawn anyway, tinted
	/// by why (<see cref="DebugViewReport.CullColor"/>), with every reason counted. Occlusion culling's own debug view is on
	/// meanwhile, for what it hid early and late.
	/// </summary>
	CullReasons = 2,

	/// <summary>
	/// The sun's cascades and the local lights' shadow maps, static caches included, as thumbnails along the bottom of the
	/// view, each framed by whether it rendered this frame.
	/// </summary>
	ShadowMaps = 4,
}

/// <summary>
/// Why the main view didn't draw an object (<see cref="DebugViews.CullReasons"/>).
/// </summary>
public enum CullReason : byte
{
	/// <summary>
	/// Drawn.
	/// </summary>
	None,

	/// <summary>
	/// Outside the view.
	/// </summary>
	Frustum,

	/// <summary>
	/// Too small on screen at its distance (<see cref="RenderView.SizeCullThreshold"/>), native's size cull. Basalt has no
	/// other distance or fade cull.
	/// </summary>
	Size,

	/// <summary>
	/// Left out by the view's render or exclude tags.
	/// </summary>
	Tags,
}

/// <summary>
/// What a shadow map thumbnail shows.
/// </summary>
public enum ShadowMapKind
{
	/// <summary>
	/// One of the sun's cascades.
	/// </summary>
	Cascade,

	/// <summary>
	/// A spot light's map.
	/// </summary>
	Spot,

	/// <summary>
	/// A point light's cube, its six faces in a 3 by 2 grid.
	/// </summary>
	Point,
}

/// <summary>
/// A shadow map shown by <see cref="DebugViews.ShadowMaps"/>.
/// </summary>
public struct ShadowMapInfo
{
	/// <summary>
	/// What it's for.
	/// </summary>
	public ShadowMapKind Kind;

	/// <summary>
	/// The cascade, or the light's place in this view's spot or point shadows.
	/// </summary>
	public int Index;

	/// <summary>
	/// A static light's cache of its static casters, copied in before its dynamic casters draw each time it renders.
	/// </summary>
	public bool StaticCache;

	/// <summary>
	/// Its width and height in texels.
	/// </summary>
	public int Resolution;

	/// <summary>
	/// The host frame it last rendered in (<see cref="RenderHost.FrameCount"/>).
	/// </summary>
	public ulong RenderedFrame;

	/// <summary>
	/// Whether it rendered this frame. Otherwise it's cached: its light waits for its turn (<c>r.shadows.updates</c>), or
	/// another view rendered it this frame, or it's a static cache.
	/// </summary>
	public bool Rendered;

	/// <summary>
	/// Where its thumbnail is, in the view's target pixels.
	/// </summary>
	public Rect Thumbnail;

	/// <summary>
	/// The map's depth texture.
	/// </summary>
	public GpuTexture Texture;
}

/// <summary>
/// What a frame's debug views found, published after each frame drawn into a window while any is on. One per render
/// system, filled again by the next frame it draws into a window: copy what's needed past the call.
/// </summary>
public sealed class DebugViewReport
{
	/// <summary>
	/// The LODs <see cref="Lods"/> counts.
	/// </summary>
	public const int MaxLods = 8;

	/// <summary>
	/// Which views were on.
	/// </summary>
	public DebugViews Views { get; internal set; }

	/// <summary>
	/// The view's name.
	/// </summary>
	public string ViewName { get; internal set; }

	/// <summary>
	/// The view's viewport in its target's pixels, which <see cref="ShadowMapInfo.Thumbnail"/> is in.
	/// </summary>
	public Rect Viewport { get; internal set; }

	/// <summary>
	/// The host frame it's from.
	/// </summary>
	public ulong Frame { get; internal set; }

	/// <summary>
	/// Meshes with LODs the main view drew at each LOD, the last counting that LOD and any past it.
	/// </summary>
	public int[] Lods { get; } = new int[MaxLods];

	/// <summary>
	/// Meshes the main view drew that have one LOD.
	/// </summary>
	public int SingleLod { get; internal set; }

	/// <summary>
	/// Objects in the world.
	/// </summary>
	public int Objects { get; internal set; }

	/// <summary>
	/// Objects that passed the main view's CPU culling, lights and probes included.
	/// </summary>
	public int Visible { get; internal set; }

	/// <summary>
	/// Objects culled for each <see cref="CullReason"/>, by its value; an object out for more than one counts once, for the first.
	/// </summary>
	public int[] Culled { get; } = new int[4];

	/// <summary>
	/// Whether occlusion culling ran.
	/// </summary>
	public bool OcclusionCulling { get; internal set; }

	/// <summary>
	/// Instances occlusion culling hid from the forward pass, read back a frame or two late.
	/// </summary>
	public int Occluded { get; internal set; }

	/// <summary>
	/// The shadow maps shown, in thumbnail order.
	/// </summary>
	public List<ShadowMapInfo> ShadowMaps { get; } = new();

	/// <summary>
	/// Shadow maps there wasn't room to show.
	/// </summary>
	public int ShadowMapsNotShown { get; internal set; }

	/// <summary>
	/// Copy everything into <paramref name="into"/>.
	/// </summary>
	internal void CopyTo( DebugViewReport into )
	{
		into.Views = Views;
		into.ViewName = ViewName;
		into.Viewport = Viewport;
		into.Frame = Frame;
		Lods.CopyTo( into.Lods, 0 );
		into.SingleLod = SingleLod;
		into.Objects = Objects;
		into.Visible = Visible;
		Culled.CopyTo( into.Culled, 0 );
		into.OcclusionCulling = OcclusionCulling;
		into.Occluded = Occluded;
		into.ShadowMaps.Clear();
		into.ShadowMaps.AddRange( ShadowMaps );
		into.ShadowMapsNotShown = ShadowMapsNotShown;
	}

	internal void Clear()
	{
		Array.Clear( Lods );
		Array.Clear( Culled );
		SingleLod = 0;
		Objects = 0;
		Visible = 0;
		Occluded = 0;
		OcclusionCulling = false;
		ShadowMaps.Clear();
		ShadowMapsNotShown = 0;
	}

	static readonly Color[] LodColors =
	[
		new( 0.25f, 0.85f, 0.3f ),
		new( 0.95f, 0.9f, 0.2f ),
		new( 1f, 0.55f, 0.1f ),
		new( 1f, 0.2f, 0.2f ),
		new( 0.9f, 0.25f, 0.9f ),
		new( 0.5f, 0.3f, 1f ),
		new( 0.2f, 0.5f, 1f ),
		new( 0.2f, 0.9f, 0.9f ),
	];

	/// <summary>
	/// The tint <see cref="DebugViews.LodColors"/> gives meshes drawn at <paramref name="lod"/>: green, yellow, orange, red,
	/// magenta, purple, blue, then cyan for the rest.
	/// </summary>
	public static Color LodColor( int lod ) => LodColors[Math.Clamp( lod, 0, LodColors.Length - 1 )];

	/// <summary>
	/// The tint <see cref="DebugViews.CullReasons"/> gives meshes culled for <paramref name="reason"/>: cyan for size, magenta for
	/// tags. Off-screen objects can't be seen; drawn ones keep their own tint (white).
	/// </summary>
	public static Color CullColor( CullReason reason ) => reason switch
	{
		CullReason.Size => new Color( 0.1f, 0.9f, 1f ),
		CullReason.Tags => new Color( 1f, 0.2f, 0.9f ),
		CullReason.Frustum => new Color( 0.5f, 0.5f, 0.5f ),
		_ => Color.White,
	};

	/// <summary>
	/// The frame <see cref="DebugViews.ShadowMaps"/> draws around a thumbnail: green for a map rendered this frame, grey for one
	/// that's cached, blue for a static cache.
	/// </summary>
	public static Color ShadowFrameColor( in ShadowMapInfo map ) => map.StaticCache ? new Color( 0.3f, 0.5f, 1f ) : map.Rendered ? new Color( 0.25f, 0.85f, 0.3f ) : new Color( 0.55f, 0.55f, 0.55f );
}
