using NativeEngine;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
namespace Sandbox.Rendering;

/// <summary>
/// ShadowMapper works out a view's shadows: which lights get shadow maps, at what size, which ones re-render
/// this frame, how the sun's cascades fit the view, and the GPU data the lighting shaders read.
///
/// It doesn't render anything itself - it asks its <see cref="Renderer"/> for each shadow view. The native
/// scenesystem's renderer (<see cref="NativeShadowRenderer"/>) is one, the managed scene renderer is another,
/// so both share the same cache, time slicing and cascade fitting.
///
/// Native mappers are owned by a CLightBinnerStandard in c++, which are pooled and reinitialized with
/// CLightBinnerStandard::InitForView. We then also get called by CLightBinnerStandard::UploadLightingToGPU.
/// </summary>
internal partial class ShadowMapper
{
	[ConVar( "r.shadows.max", Min = -1, Max = 256, Help = "Maximum number of shadow-casting local lights. Lights are sorted by screen size, least important are culled first." )]
	public static int MaxShadows { get; set; } = 256;

	[ConVar( "r.shadows.maxresolution", Min = 128, Max = 4096, Help = "Max texture size (square) for a projected light shadow map, higher is better but uses more vram, these are scaled automatically too." )]
	public static int MaxResolution { get; set; } = 1024; // Low/Med=512, High=1024, Very High=2048

	[ConVar( "r.shadows.quality", Min = 0, Max = 4, Help = "What filtering to use, higher is more GPU intensive. 0 = Off, 1 = Low, 2 = Med, 3 = High, 4 = Experimental Penumbra Shadows" )]
	public static int ShadowFilter { get; set; } = 3;

	[ConVar( "r.shadows.csm.maxcascades", Min = 1, Max = 4, Help = "Maximum number of cascades for directional light shadows." )]
	public static int MaxCascades { get; set; } = 4;

	[ConVar( "r.shadows.csm.maxresolution", Min = 512, Max = 8192, Help = "Maximum resolution for each cascade shadow map." )]
	public static int MaxCascadeResolution { get; set; } = 2048;

	[ConVar( "r.shadows.csm.distance", Min = 500, Max = 50000, Help = "Maximum distance from the camera that directional light shadows are rendered." )]
	public static float CascadeDistance { get; set; } = 15000;

	[ConVar( "r.shadows.debug", ConVarFlags.Cheat, Help = "Show shadow debug overlay with memory allocation and budget info." )]
	public static bool DebugEnabled { get; set; } = false;

	[ConVar( "r.shadows.csm.enabled", Help = "Enable directional light (CSM) shadows." )]
	public static bool CSMEnabled { get; set; } = true;

	[ConVar( "r.shadows.local.enabled", Help = "Enable local light (spot/point) shadows." )]
	public static bool LocalShadowsEnabled { get; set; } = true;

	[ConVar( "r.shadows.updates", Min = 1, Max = 256, Help = "How many local light shadow maps may be re-rendered per frame. The most stale lights (weighted by screen size) go first; new, moved and resized lights always update." )]
	public static int MaxUpdatesPerFrame { get; set; } = 8;

	[ConVar( "r.shadows.depthbias", Min = -256, Max = 0, Help = "Rasterizer constant depth bias applied during shadow map rendering. More negative = stronger bias." )]
	public static int ShadowDepthBias { get; set; } = -1;

	[ConVar( "r.shadows.slopescale", Min = -16.0f, Max = 0.0f, Help = "Rasterizer slope-scaled depth bias applied during shadow map rendering. More negative = stronger bias on angled surfaces." )]
	public static float ShadowSlopeScale { get; set; } = -1.5f;

	internal const uint InvalidShadowIndex = 0xFFFFFFFF;

	// Absolute upper limits, no harm in increasing these
	const int ProjectedShadowBufferSize = 512;
	const int ProjectedCubeShadowBufferSize = 256;

	int ShadowsAllocated { get; set; }

	/// <summary>
	/// Lifetime counters for tracking texture allocation health.
	/// Created - Disposed should equal cache + pool + in-flight at any point.
	/// If in-flight grows indefinitely, textures are being orphaned.
	/// </summary>
	internal static long TotalTexturesCreated { get; private set; }
	internal static long TotalTexturesReleased { get; private set; }
	internal static long TotalTexturesDisposed { get; private set; }

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
			projected[i].ShadowMapTextureIndex = ProjectedShadowMaps[i].Texture.Index;

		var cubes = CollectionsMarshal.AsSpan( GPUProjectedCubeShadows );
		for ( int i = 0; i < cubes.Length; i++ )
			cubes[i].ShadowMapTextureCubeIndex = (uint)ProjectedCubeShadowMaps[i].Texture.Index;

		for ( int i = 0; i < CascadeShadowMapCount; i++ )
			GPUDirectionalLightData.ShadowMapIndex[i] = CascadeShadowMaps[i].Texture.Index;
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

	internal void SetShaderAttributes( RenderAttributes attributes )
	{
		if ( attributes is null )
			return;

		ResolveTextureIndices();
		EnsureBuffers();

		attributes.Set( "ProjectedShadows", GPUProjectedShadowsBuffer );
		attributes.Set( "ProjectedCubeShadows", GPUProjectedCubeShadowsBuffer );

		attributes.SetData( "DirectionalLightCB", GPUDirectionalLightData );

		attributes.Set( "DirectionalLightDebug", DebugEnabled );
	}

	/// <summary>
	/// Called as we submit display lists, upload our shadow map buffers.
	/// </summary>
	internal void UploadToGPU()
	{
		ResolveTextureIndices();
		EnsureBuffers();

		GPUProjectedShadowsBuffer.SetData( GPUProjectedShadows );
		GPUProjectedCubeShadowsBuffer.SetData( GPUProjectedCubeShadows );
	}

	/// <summary>
	/// This ties our buffers to a specific lightbinner. They're all the same size at the end of the day, we
	/// could pool them normally. Created on first use, so a mapper can work shadows out without a GPU.
	/// </summary>
	void EnsureBuffers()
	{
		GPUProjectedShadowsBuffer ??= new( ProjectedShadowBufferSize );
		GPUProjectedCubeShadowsBuffer ??= new( ProjectedCubeShadowBufferSize );
	}

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
	/// Shadow maps by light - a <see cref="SceneLight"/>, or another renderer's light.
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
		if ( entry.RenderedFrame == Application.FrameCount && desiredResolution < entry.CurrentResolution )
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

	public static long MemorySize
	{
		get
		{
			long total = 0;
			foreach ( var kvp in Cache )
			{
				if ( kvp.Value.ShadowMap is { HasTexture: true } shadowMap )
					total += g_pRenderDevice.ComputeTextureMemorySize( shadowMap.Texture.native );

				if ( kvp.Value.StaticCache is { HasTexture: true } staticCache )
					total += g_pRenderDevice.ComputeTextureMemorySize( staticCache.Texture.native );
			}
			return total;
		}
	}

	struct PooledTexture
	{
		public ShadowMap Texture;
		public float ReturnedAt;
	}

	record struct PoolKey( int Resolution, bool IsCube );

	static readonly Dictionary<PoolKey, Queue<PooledTexture>> TexturePool = new();

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
		if ( FrameStamp == Application.FrameCount )
			return;

		FrameStamp = Application.FrameCount;
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

		return light.Type switch
		{
			ShadowLightType.Spot => FindOrCreateProjectedShadowMap( light, flScreenSize ),
			ShadowLightType.Point => FindOrCreateProjectedCubeShadowMap( light, flScreenSize ),
			_ => InvalidShadowIndex
		};
	}

	internal int DoDirectionalLight( in ShadowLight light )
	{
		GPUDirectionalLightData.Enabled = true;

		if ( !CSMEnabled )
			return 0;

		FindOrCreateDirectionalShadowMaps( light );
		return 0;
	}

	public static void OnSceneObjectTransformCreated() { }
	public static void OnSceneObjectTransformRemoved() { }
	public static void OnSceneObjectTransformChanged() { }
}
