namespace Borealis;

/// <summary>
/// Which casters a shadow view takes, by <see cref="RenderObject.IsStatic"/>.
/// </summary>
internal enum StaticFilter
{
	Any,

	/// <summary>
	/// Rendering a light's static cache.
	/// </summary>
	OnlyStatic,

	/// <summary>
	/// Rendering on top of a static cache, or for a baked light.
	/// </summary>
	NoStatic,

	/// <summary>
	/// No casters: the view is only its static cache.
	/// </summary>
	Nothing,

	/// <summary>
	/// Rendering the sun's cached far cascade: static casters, and those settled by <see cref="ViewPass.SettledStamp"/>
	/// (<see cref="Shadows.SettledCasters"/>).
	/// </summary>
	Settled,

	/// <summary>
	/// <see cref="Settled"/> for a baked sun: only the settled casters that aren't static.
	/// </summary>
	SettledNoStatic,

	/// <summary>
	/// Rendering on top of the sun's cached far cascade: casters that aren't static and weren't settled by
	/// <see cref="ViewPass.SettledStamp"/>.
	/// </summary>
	Unsettled,
}

/// <summary>
/// Pooled main or shadow view with per-feature culling and draw state, equivalent to native's <c>CSceneView</c>.
/// </summary>
internal sealed class ViewPass
{
	List<int>[] visible = [];
	object[] state = [];

	/// <summary>
	/// The camera drawn from.
	/// </summary>
	public RenderView View { get; set; }

	/// <summary>
	/// View used for LOD and size culling. Shadows use the main view, matching native.
	/// </summary>
	public RenderView Root { get; set; }

	/// <summary>
	/// Draws shadow casters into <see cref="Target"/>'s depth only, in the <see cref="ShadowLayer"/>.
	/// </summary>
	public bool IsShadow { get; set; }

	/// <summary>
	/// Shadow map and texture resolved during setup.
	/// </summary>
	public ShadowMap TargetMap { get; set; }
	public GpuTexture Target { get; set; }

	/// <summary>
	/// A static cache the target starts from instead of being cleared.
	/// </summary>
	public ShadowMap CachedStatic { get; set; }

	public StaticFilter StaticFilter { get; set; }

	/// <summary>
	/// A sun cascade that size culls its casters by its own texel size rather than by <see cref="Root"/>'s screen
	/// (<see cref="Culling.SizeCull.ForPass"/>), so what it draws doesn't depend on where the camera is.
	/// </summary>
	public bool TexelSized { get; set; }

	/// <summary>
	/// Picks its meshes' LODs by its own texels, how many a world unit covers, rather than by <see cref="Root"/>'s screen;
	/// 0 for the root's.
	/// </summary>
	public float LodTexelsPerUnit { get; set; }

	/// <summary>
	/// The <see cref="Shadows.SettledCasters"/> stamp a <see cref="StaticFilter.Settled"/> or <see cref="StaticFilter.Unsettled"/>
	/// view splits casters at.
	/// </summary>
	public ulong SettledStamp { get; set; }

	/// <summary>
	/// <see cref="SettledStamp"/> for restless casters (<see cref="Shadows.SettledCasters.RestlessStamp"/>).
	/// </summary>
	public ulong RestlessStamp { get; set; }

	/// <summary>
	/// An <see cref="StaticFilter.Unsettled"/> view counts here the casters it drew that have settled since, which its cache
	/// would hold if it were drawn again.
	/// </summary>
	public Shadows.CasterCount NewlySettled { get; set; }

	/// <summary>
	/// <see cref="CachedStatic"/> is held wrapped around by this many texels (<see cref="Shadows.ShadowViewDesc.CacheOffset"/>).
	/// </summary>
	public Vector2Int CacheOffset { get; set; }

	/// <summary>
	/// A cleared depth tile copied over the view's viewport instead of clearing the target, for a view of part of it.
	/// </summary>
	public ShadowMap Blank { get; set; }

	/// <summary>
	/// While <see cref="DebugViews.CullReasons"/> is on, the main view's <see cref="CullReason"/> by world index for meshes it
	/// culled on the CPU and draws anyway; null otherwise, and for every other view.
	/// </summary>
	public byte[] CullReasons { get; set; }

	/// <summary>
	/// By world index, what the resident lists draw (<see cref="Features.AggregateRenderFeature.ResidentMembers"/>), which this
	/// view's culling leaves out: its resident list draws them. Null when it culls every object.
	/// </summary>
	internal byte[] ResidentMembers { get; set; }

	/// <summary>
	/// By world index, the objects this main view's resident list draws (the GPU culls them), which its collect leaves out;
	/// null when it collects every object.
	/// </summary>
	internal bool[] ResidentHolds { get; set; }

	/// <summary>
	/// By node of the tree the view queries, the subtrees with an object it doesn't leave out (<see cref="Culling.SpatialTree.Mark"/>):
	/// its query skips the rest. Null when it queries every object.
	/// </summary>
	internal bool[] TreeKept { get; set; }

	/// <summary>Whether a resident caster this view takes was in its frustum: its culling left it out, but it has casters.</summary>
	internal bool ResidentInView { get; set; }

	/// <summary>
	/// Cube face or array layer of <see cref="Target"/>.
	/// </summary>
	public int TargetSlice { get; set; }

	/// <summary>
	/// Rasterizer depth bias the shadow draws use - native's <c>RsDepthBiasStateOverride_t</c>.
	/// </summary>
	public int DepthBias { get; set; }
	public float SlopeScaledDepthBias { get; set; }

	/// <summary>
	/// Shared query sphere for a light's shadow views. Null uses the view frustum.
	/// </summary>
	public (Vector3 Center, float Radius)? LightSphere { get; set; }

	/// <summary>
	/// The objects a feature drew in this view, by world index.
	/// </summary>
	public List<int> Visible( RenderFeature feature ) => visible[feature.Index];

	/// <summary>
	/// Every feature's <see cref="Visible"/> list, by feature index.
	/// </summary>
	internal List<int>[] VisibleByFeature => visible;

	/// <summary>
	/// Lazily created per-feature state retained with this pass.
	/// </summary>
	public T State<T>( RenderFeature feature ) where T : class, new()
	{
		return (T)(state[feature.Index] ??= new T());
	}

	/// <summary>
	/// Clear for reuse, with a list for each of <paramref name="featureCount"/> features.
	/// </summary>
	public void Reset( int featureCount )
	{
		if ( visible.Length < featureCount )
		{
			var old = visible.Length;
			Array.Resize( ref visible, featureCount );
			Array.Resize( ref state, featureCount );
			for ( int i = old; i < featureCount; i++ )
				visible[i] = new List<int>();
		}

		foreach ( var list in visible )
			list.Clear();

		IsShadow = false;
		TargetMap = null;
		Target = null;
		CachedStatic = null;
		StaticFilter = StaticFilter.Any;
		TexelSized = false;
		LodTexelsPerUnit = 0;
		SettledStamp = 0;
		RestlessStamp = 0;
		NewlySettled = null;
		ResidentMembers = null;
		ResidentHolds = null;
		TreeKept = null;
		ResidentInView = false;
		CacheOffset = default;
		Blank = null;
		TargetSlice = 0;
		DepthBias = 0;
		SlopeScaledDepthBias = 0;
		LightSphere = null;
	}
}
