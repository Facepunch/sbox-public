using Sandbox.Rendering;

namespace Sandbox.SceneRenderer;

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
	public Texture Target { get; set; }

	/// <summary>
	/// A static cache the target starts from instead of being cleared.
	/// </summary>
	public ShadowMap CachedStatic { get; set; }

	public StaticFilter StaticFilter { get; set; }

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
	/// Excludes casters fully covered by an earlier cascade.
	/// </summary>
	public ViewFrustum? Exclusion { get; set; }

	/// <summary>
	/// Shared query sphere for a light's shadow views. Null uses the view frustum.
	/// </summary>
	public (Vector3 Center, float Radius)? LightSphere { get; set; }

	/// <summary>
	/// The objects a feature drew in this view, by world index.
	/// </summary>
	public List<int> Visible( RenderFeature feature ) => visible[feature.Index];

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
		TargetSlice = 0;
		DepthBias = 0;
		SlopeScaledDepthBias = 0;
		Exclusion = null;
		LightSphere = null;
	}
}
