using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Basalt.Shadows;

/// <summary>
/// ShadowMapper works out a view's shadows: which lights get shadow maps, at what size, which ones re-render
/// this frame, how the sun's cascades fit the view, and the GPU data the lighting shaders read.
///
/// It doesn't render anything itself - it asks its <see cref="Renderer"/> for each shadow view. Its settings come
/// from <see cref="RenderHost.Shadows"/>, and its frames from <see cref="RenderHost.FrameCount"/>.
/// </summary>
internal partial class ShadowMapper
{
	static ShadowSettings Settings => RenderHost.Current.Shadows;

	static int MaxShadows => Settings.MaxShadows;
	static int MaxResolution => Settings.MaxResolution;
	internal static int ShadowFilter => Settings.Filter;
	static int MaxCascades => Settings.MaxCascades;
	static int MaxCascadeResolution => Settings.MaxCascadeResolution;
	static float CascadeDistance => Settings.CascadeDistance;
	static bool CSMEnabled => Settings.CascadesEnabled;
	static bool LocalShadowsEnabled => Settings.LocalEnabled;
	static int MaxUpdatesPerFrame => Settings.MaxUpdatesPerFrame;
	static int ShadowDepthBias => Settings.DepthBias;
	static float ShadowSlopeScale => Settings.SlopeScale;
	static float SizeCullThreshold => Settings.SizeCullThreshold;

	static ulong FrameCount => RenderHost.Current.FrameCount;

	internal const uint InvalidShadowIndex = 0xFFFFFFFF;

	/// <summary>
	/// Lifetime counters for tracking texture allocation health.
	/// Created - Disposed should equal cache + pool + in-flight at any point.
	/// If in-flight grows indefinitely, textures are being orphaned.
	/// </summary>
	internal static long TotalTexturesCreated { get; private set; }
	internal static long TotalTexturesReleased { get; private set; }
	internal static long TotalTexturesDisposed { get; private set; }

	// Absolute upper limits, no harm in increasing these
	internal const int ProjectedShadowBufferSize = 512;
	internal const int ProjectedCubeShadowBufferSize = 256;

	int ShadowsAllocated { get; set; }

	/// <summary>
	/// Renders the shadow views this mapper asks for.
	/// </summary>
	internal IShadowRenderer Renderer { get; set; }

	/// <summary>
	/// The view the shadows are for - set before asking for any.
	/// </summary>
	internal ShadowViewInfo View;

	/// <summary>
	/// The shadow map behind each entry in <see cref="GPUProjectedShadows"/>, <see cref="GPUProjectedCubeShadows"/>
	/// and the sun's cascades, so their bindless indices can be filled in once the textures exist
	/// (<see cref="ResolveTextureIndices"/>).
	/// </summary>
	readonly List<ShadowMap> ProjectedShadowMaps = new();
	readonly List<ShadowMap> ProjectedCubeShadowMaps = new();
	readonly ShadowMap[] CascadeShadowMaps = new ShadowMap[4];
	int CascadeShadowMapCount;

	/// <summary>
	/// Start a view: evict stale shadow maps, and clear the last view's shadows.
	/// </summary>
	internal void InitForView()
	{
		// Evict stale shadow maps and clean the texture pool
		Update();

		// Reset all our lists
		GPUProjectedShadows.Clear();
		GPUProjectedCubeShadows.Clear();
		ProjectedShadowMaps.Clear();
		ProjectedCubeShadowMaps.Clear();
		CascadeShadowMapCount = 0;
		GPUDirectionalLightData.CascadeCount = 0;
		GPUDirectionalLightData.Enabled = false;
		GPUDirectionalLightData.ShadowMaskTextureIndex = 0;
		ShadowsAllocated = 0;

		// Save statistics from last frame, then reset
		ProjectedShadowsRenderedLastFrame = ProjectedShadowsRendered;
		ProjectedShadowsCulledLastFrame = ProjectedShadowsCulled;
		ProjectedShadowsRendered = 0;
		ProjectedShadowsCulled = 0;
	}

	/// <summary>
	/// Fill in each shadow's bindless texture index, creating any textures that don't exist yet. Before the
	/// shadow data goes to the GPU.
	/// </summary>
	internal unsafe void ResolveTextureIndices()
	{
		var projected = CollectionsMarshal.AsSpan( GPUProjectedShadows );
		for ( int i = 0; i < projected.Length; i++ )
			projected[i].ShadowMapTextureIndex = ProjectedShadowMaps[i].Texture.BindlessIndex;

		var cubes = CollectionsMarshal.AsSpan( GPUProjectedCubeShadows );
		for ( int i = 0; i < cubes.Length; i++ )
			cubes[i].ShadowMapTextureCubeIndex = (uint)ProjectedCubeShadowMaps[i].Texture.BindlessIndex;

		for ( int i = 0; i < CascadeShadowMapCount; i++ )
			GPUDirectionalLightData.ShadowMapIndex[i] = CascadeShadowMaps[i].Texture.BindlessIndex;
	}

	/// <summary>
	/// This view's spot light shadows, as the <c>ProjectedShadows</c> buffer holds them.
	/// </summary>
	internal ReadOnlySpan<GPUProjectedShadow> ProjectedShadowData => CollectionsMarshal.AsSpan( GPUProjectedShadows );

	/// <summary>
	/// This view's point light shadows, as the <c>ProjectedCubeShadows</c> buffer holds them.
	/// </summary>
	internal ReadOnlySpan<GPUProjectedCubeShadow> ProjectedCubeShadowData => CollectionsMarshal.AsSpan( GPUProjectedCubeShadows );

	/// <summary>
	/// The sun and its cascades, as <c>DirectionalLightCB</c> holds them.
	/// </summary>
	internal ref GPUDirectionalLight DirectionalLightData => ref GPUDirectionalLightData;

	public class LightEntry
	{
		/// <summary>
		/// The light this is for, held weakly like the <see cref="Cache"/> key.
		/// </summary>
		internal WeakReference<object> Light;

		public float LastFrame;
		public ShadowMap StaticCache;
		public ShadowMap ShadowMap;
		public float ScreenSize;
		public int CurrentResolution;
		public int DesiredResolution;
		public int DebugLightIndex;
		public bool IsCube;
		public string DebugName;
		public int CachedTransformVersion;

		// The light as it was last seen, for the debug overlay
		public ShadowLightType Type;
		public Vector3 Position;
		public float Radius;
		public float Bias;
		public float ConeOuter;

		// Shadow maps don't depend on the camera: rendered at most once per engine frame, shared by every view that
		// frame (cube faces, mirrors, VR eyes), and time sliced across frames. 0 = never rendered, must render.
		public ulong RenderedFrame;
		public ulong UsedFrame;
		public bool Scheduled;
		internal GPUProjectedShadow Projected;
		internal GPUProjectedCubeShadow Cube;
	}

	static ulong FrameStamp;
	static readonly List<LightEntry> Schedule = new();

	/// <summary>
	/// Higher refreshes sooner. Screen size enters exponentially, so a light near enough to fill part of the
	/// view outranks a distant one by orders of magnitude instead of by their size ratio.
	/// </summary>
	static float Priority( LightEntry e ) => (FrameStamp - e.RenderedFrame) * MathF.Exp( 6f * e.ScreenSize );

	/// <summary>
	/// Shadow maps by light.
	/// </summary>
	public static ConditionalWeakTable<object, LightEntry> Cache = new();

	/// <summary>
	/// Every entry in <see cref="Cache"/>, to walk each frame without allocating an enumerator.
	/// </summary>
	static readonly List<LightEntry> Entries = new();

	/// <summary>
	/// Get or create the cache entry for a light, handling resolution changes and
	/// dropping the static cache if the light moved.
	/// </summary>
	static LightEntry GetOrCreateCacheEntry( in ShadowLight light, int desiredResolution, bool isCube, float flScreenSize )
	{
		if ( !Cache.TryGetValue( light.Key, out var entry ) )
		{
			entry = new()
			{
				Light = new WeakReference<object>( light.Key ),
				ShadowMap = AcquireTexture( desiredResolution, isCube ),
				CurrentResolution = desiredResolution,
				IsCube = isCube,
				DebugName = $"{light.Key}_Shadow",
			};
			Cache.AddOrUpdate( light.Key, entry );
			Entries.Add( entry );
		}

		entry.Type = light.Type;
		entry.Position = light.Position;
		entry.Radius = light.Radius;
		entry.Bias = light.Bias;
		entry.ConeOuter = light.ConeOuter;

		// Keep track of how big we actually want it, if we run low on budget we can downgrade these out of scope
		entry.DesiredResolution = desiredResolution;
		entry.ScreenSize = flScreenSize;

		// A smaller view later in the same frame (probe bake, mirror) keeps the bigger map already rendered this frame
		if ( entry.RenderedFrame == FrameCount && desiredResolution < entry.CurrentResolution )
			desiredResolution = entry.CurrentResolution;

		// Do we want a different resolution for this shadow map now?
		if ( entry.CurrentResolution != desiredResolution )
		{
			ReleaseTexture( entry.ShadowMap, entry.CurrentResolution, entry.IsCube );
			ReleaseTexture( entry.StaticCache, entry.CurrentResolution, entry.IsCube );
			entry.ShadowMap = AcquireTexture( desiredResolution, isCube );
			entry.StaticCache = null;
			entry.CurrentResolution = desiredResolution;
			entry.RenderedFrame = 0;
		}

		// The static cache is only valid for the transform it was rendered at, and a moved light must re-render
		if ( entry.CachedTransformVersion != light.TransformVersion )
		{
			entry.CachedTransformVersion = light.TransformVersion;
			ReleaseTexture( entry.StaticCache, entry.CurrentResolution, entry.IsCube );
			entry.StaticCache = null;
			entry.RenderedFrame = 0;
		}

		return entry;
	}

	internal struct PooledTexture
	{
		public ShadowMap Texture;
		public float ReturnedAt;
	}

	internal record struct PoolKey( int Resolution, bool IsCube );

	internal static readonly Dictionary<PoolKey, Queue<PooledTexture>> TexturePool = new();

	/// <summary>
	/// How long a texture sits unused in the cache before being evicted and returned to the pool.
	/// </summary>
	const float CacheEvictionTime = 2.0f;

	/// <summary>
	/// How long a texture sits unused in the pool before being disposed.
	/// </summary>
	const float PoolDisposeTime = 10.0f;

	static ShadowMap AcquireTexture( int resolution, bool isCube )
	{
		var key = new PoolKey( resolution, isCube );
		if ( TexturePool.TryGetValue( key, out var queue ) && queue.Count > 0 )
			return queue.Dequeue().Texture;

		TotalTexturesCreated++;
		return new ShadowMap( resolution, isCube, LocalShadowDepthFormat );
	}

	static void ReleaseTexture( ShadowMap texture, int resolution, bool isCube )
	{
		if ( texture is null )
			return;

		var key = new PoolKey( resolution, isCube );
		if ( !TexturePool.TryGetValue( key, out var queue ) )
		{
			queue = new Queue<PooledTexture>();
			TexturePool[key] = queue;
		}

		queue.Enqueue( new PooledTexture { Texture = texture, ReturnedAt = RealTime.Now } );
		TotalTexturesReleased++;
	}

	/// <summary>
	/// Evict shadow maps from lights that haven't been rendered recently,
	/// and dispose pooled textures that have sat idle for too long.
	/// </summary>
	public static void Update()
	{
		// Once per engine frame, not per view
		if ( FrameStamp == FrameCount )
			return;

		FrameStamp = FrameCount;
		float now = RealTime.Now;

		// Evict stale cache entries, and those whose light is gone
		for ( int i = Entries.Count - 1; i >= 0; i-- )
		{
			var entry = Entries[i];
			var alive = entry.Light.TryGetTarget( out var light );
			if ( alive && now - entry.LastFrame < CacheEvictionTime )
				continue;

			ReleaseTexture( entry.StaticCache, entry.CurrentResolution, entry.IsCube );
			ReleaseTexture( entry.ShadowMap, entry.CurrentResolution, entry.IsCube );
			entry.ShadowMap = null;
			entry.StaticCache = null;
			if ( alive ) Cache.Remove( light );

			Entries[i] = Entries[^1];
			Entries.RemoveAt( Entries.Count - 1 );
		}

		// Dispose pooled textures that have been idle for too long
		foreach ( var kvp in TexturePool )
		{
			var queue = kvp.Value;
			while ( queue.Count > 0 && now - queue.Peek().ReturnedAt >= PoolDisposeTime )
			{
				queue.Dequeue().Texture?.Dispose();
				TotalTexturesDisposed++;
			}
		}

		// Time slicing: the update budget goes to the lights that have waited longest, weighted by screen size,
		// so big lights refresh often and small ones still get a turn. New and moved lights bypass the budget.
		Schedule.Clear();
		foreach ( var entry in Entries )
		{
			entry.Scheduled = false;
			if ( entry.UsedFrame == FrameStamp - 1 )
				Schedule.Add( entry );
		}

		Schedule.Sort( static ( a, b ) => Priority( b ).CompareTo( Priority( a ) ) );
		for ( int i = 0; i < Schedule.Count && i < MaxUpdatesPerFrame; i++ )
			Schedule[i].Scheduled = true;
	}

	/// <summary>
	/// Drop every cached shadow map and pooled texture - for tests, so one test's lights going stale can't
	/// be evicted in the middle of another's.
	/// </summary>
	internal static void ResetCache()
	{
		foreach ( var entry in Entries )
		{
			entry.ShadowMap?.Dispose();
			entry.StaticCache?.Dispose();
			if ( entry.Light.TryGetTarget( out var light ) ) Cache.Remove( light );
		}

		foreach ( var queue in TexturePool.Values )
		{
			while ( queue.Count > 0 )
				queue.Dequeue().Texture?.Dispose();
		}

		Entries.Clear();
		Schedule.Clear();
		LastCascades = default;
	}

	/// <summary>
	/// Called when a light is removed from the scene. Returns its shadow map to the pool.
	/// </summary>
	public static void OnLightRemoved( object light )
	{
		if ( !Cache.TryGetValue( light, out var entry ) )
			return;

		ReleaseTexture( entry.ShadowMap, entry.CurrentResolution, entry.IsCube );
		entry.ShadowMap = null;
		ReleaseTexture( entry.StaticCache, entry.CurrentResolution, entry.IsCube );
		entry.StaticCache = null;
		Cache.Remove( light );
		Entries.Remove( entry );
	}

	/// <summary>
	/// Scale for rasterizer depth bias. Uses texel-to-depth ratio (tanθ / res),
	/// same idea as CSM Width/Far — not world-space texel size.
	/// Normalized so a 45° half-angle map at BiasScaleReferenceResolution is 1.0.
	/// </summary>
	internal static float ComputeBiasScale( float halfAngleDegrees, float range, int resolution )
	{
		const int BiasScaleReferenceResolution = 1024;

		// (tanθ / resolution) / (tan45° / referenceRes) — range is unused (cancels for depth-unit bias).
		// Cap at 1: scaling *up* for low-res/wide cones only detaches shadows (peter-panning).
		return Math.Min( 1f, MathF.Tan( halfAngleDegrees * MathF.PI / 180f )
			* BiasScaleReferenceResolution
			/ Math.Max( resolution, 1 ) );
	}

	internal static int GetDesiredResolution( float screenSizePercent, int viewportSize )
	{
		// screenSizePercent is a screen-area fraction from ComputeScreenSize.
		// Convert to linear dimension: sqrt(area) gives the fraction of the viewport edge.
		float linearSize = MathF.Sqrt( screenSizePercent ) * viewportSize;

		// Round down to nearest power of two
		int desiredSize = (int)BitOperations.RoundUpToPowerOf2( (uint)Math.Max( linearSize, 1 ) ) >> 1;

		return Math.Clamp( desiredSize, 128, MaxResolution );
	}

	/// <summary>
	/// Find a cached shadow map or create a new one for a local light and the current <see cref="View"/>.
	/// Returns an index to the shadow maps structured buffer
	/// </summary>
	internal uint FindOrCreateShadowMaps( in ShadowLight light, float flScreenSize )
	{
		if ( !LocalShadowsEnabled )
			return InvalidShadowIndex;

		// Unified shadow budget — reject if we've hit the limit
		if ( ShadowsAllocated >= MaxShadows )
		{
			ProjectedShadowsCulled++;
			return InvalidShadowIndex;
		}

		// Reflections without shadows don't wait on this frame's shadow maps
		if ( View.NoShadows )
			return InvalidShadowIndex;

		return light.Type switch
		{
			ShadowLightType.Spot => FindOrCreateProjectedShadowMap( light, flScreenSize ),
			ShadowLightType.Point => FindOrCreateProjectedCubeShadowMap( light, flScreenSize ),
			_ => InvalidShadowIndex
		};
	}

	internal int DoDirectionalLight( in ShadowLight light )
	{
		if ( CSMEnabled )
			FindOrCreateDirectionalShadowMaps( light );

		// Lighting must stay current even without shadows or when cached cascades are reused.
		GPUDirectionalLightData.Enabled = true;
		GPUDirectionalLightData.Color = new Vector4( light.Color, light.FogStrength );
		GPUDirectionalLightData.Direction = new Vector4( light.Direction, 0 );
		return 0;
	}
}
