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
	/// The cache entry behind each spot shadow, then each point shadow, of this view - for the shadow map debug view.
	/// </summary>
	readonly List<LightEntry> SpotEntries = new();
	readonly List<LightEntry> PointEntries = new();

	/// <summary>
	/// The cache entries this view drew, which aren't drawn until its frame is submitted (<see cref="Abandon"/>).
	/// </summary>
	readonly List<LightEntry> Drawn = new();

	/// <summary>
	/// This view's cascades.
	/// </summary>
	internal ReadOnlySpan<ShadowMap> Cascades => CascadeShadowMaps.AsSpan( 0, CascadeShadowMapCount );

	/// <summary>
	/// This view's spot light shadows' cache entries, in <see cref="ProjectedShadowData"/> order.
	/// </summary>
	internal List<LightEntry> SpotShadows => SpotEntries;

	/// <summary>
	/// This view's point light shadows' cache entries, in <see cref="ProjectedCubeShadowData"/> order.
	/// </summary>
	internal List<LightEntry> PointShadows => PointEntries;

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
		SpotEntries.Clear();
		PointEntries.Clear();
		Drawn.Clear();
		CascadeShadowMapCount = 0;
		GPUDirectionalLightData.CascadeCount = 0;
		GPUDirectionalLightData.Flags = 0;
		GPUDirectionalLightData.ShadowMaskTextureIndex = 0;
		ShadowsAllocated = 0;

		// Save statistics from last frame, then reset
		ProjectedShadowsRenderedLastFrame = ProjectedShadowsRendered;
		ProjectedShadowsCulledLastFrame = ProjectedShadowsCulled;
		ProjectedShadowsRendered = 0;
		ProjectedShadowsCulled = 0;
	}

	/// <summary>
	/// Whether this view's frame chose its shadow maps and hasn't been submitted yet. A child view rendered meanwhile, from the
	/// middle of its recording, is submitted before it, so it can't use the maps this frame draws (<see cref="DrawnInFlight"/>).
	/// </summary>
	internal bool InFlight;

	/// <summary>
	/// Whether another view's frame, not submitted yet, drew this map this frame: this one draws it again, into the same texture,
	/// at the same resolution, so the other's commands keep theirs.
	/// </summary>
	bool DrawnInFlight( LightEntry entry ) => entry.RenderedFrame == FrameCount && entry.DrawnBy is { InFlight: true } by && by != this;

	/// <summary>
	/// The view's frame was never submitted: the maps it drew, and the static caches it drew into them, were never drawn
	/// after all. They're drawn again the next time they're used, and nothing reuses its cascades.
	/// </summary>
	internal void Abandon()
	{
		InFlight = false;
		foreach ( var entry in Drawn )
		{
			entry.RenderedFrame = 0;
			if ( entry.StaticRenderedFrame != FrameCount ) continue;

			ReleaseTexture( entry.StaticCache, entry.CurrentResolution, entry.IsCube );
			entry.StaticCache = null;
			entry.StaticRenderedFrame = 0;
		}

		Drawn.Clear();
		if ( LastCascades.Mapper == this ) LastCascades = default;
		AbandonFarCascades();
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

		/// <summary>
		/// The frame <see cref="StaticCache"/> rendered in.
		/// </summary>
		public ulong StaticRenderedFrame;

		/// <summary>
		/// The mapper whose view last drew the map.
		/// </summary>
		internal ShadowMapper DrawnBy;

		/// <summary>
		/// The world's <see cref="StaticCasterChanges.Revision"/> <see cref="StaticCache"/> is up to date with.
		/// </summary>
		public long StaticRevision;

		/// <summary>
		/// The tags of the views this map is for: it holds the casters they show.
		/// </summary>
		internal uint[] RenderTags, ExcludeTags;

		/// <summary>
		/// The sub-worlds of the views this map is for, whose casters it holds too.
		/// </summary>
		internal ulong SubWorlds;

		/// <summary>
		/// The same light's map for views with other tags.
		/// </summary>
		internal LightEntry NextFilter;
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
	/// Shadow maps by light: the first of its maps, each for views with different tags (<see cref="LightEntry.NextFilter"/>).
	/// </summary>
	public static ConditionalWeakTable<object, LightEntry> Cache = new();

	/// <summary>
	/// Every entry in <see cref="Cache"/>, to walk each frame without allocating an enumerator.
	/// </summary>
	static readonly List<LightEntry> Entries = new();

	/// <summary>
	/// Get or create the cache entry for a light seen through <see cref="View"/>'s tags, handling resolution changes and
	/// dropping the static cache if the light moved or a static caster it reaches changed.
	/// </summary>
	LightEntry GetOrCreateCacheEntry( in ShadowLight light, int desiredResolution, bool isCube, float flScreenSize )
	{
		// A map holds the casters its view's tags show, so views with other tags keep maps of their own
		var renderTags = View.RenderTags ?? [];
		var excludeTags = View.ExcludeTags ?? [];
		Cache.TryGetValue( light.Key, out var first );
		var entry = first;
		while ( entry is not null && !(SameTags( entry.RenderTags, renderTags ) && SameTags( entry.ExcludeTags, excludeTags ) && entry.SubWorlds == View.SubWorlds) )
			entry = entry.NextFilter;

		if ( entry is null )
		{
			entry = new()
			{
				Light = new WeakReference<object>( light.Key ),
				ShadowMap = AcquireTexture( desiredResolution, isCube ),
				CurrentResolution = desiredResolution,
				IsCube = isCube,
				DebugName = $"{light.Key}_Shadow",
				RenderTags = renderTags.Length == 0 ? [] : (uint[])renderTags.Clone(),
				ExcludeTags = excludeTags.Length == 0 ? [] : (uint[])excludeTags.Clone(),
				SubWorlds = View.SubWorlds,
			};

			if ( first is null )
			{
				Cache.AddOrUpdate( light.Key, entry );
			}
			else
			{
				entry.NextFilter = first.NextFilter;
				first.NextFilter = entry;
			}

			Entries.Add( entry );
		}
		else if ( entry.Type != light.Type || entry.Radius != light.Radius || (light.Type == ShadowLightType.Spot && entry.ConeOuter != light.ConeOuter) )
		{
			// A new kind, radius or cone is a new projection, which neither the map nor its static cache hold yet
			ReleaseTexture( entry.StaticCache, entry.CurrentResolution, entry.IsCube );
			entry.StaticCache = null;
			entry.RenderedFrame = 0;

			if ( entry.IsCube != isCube )
			{
				ReleaseTexture( entry.ShadowMap, entry.CurrentResolution, entry.IsCube );
				entry.ShadowMap = AcquireTexture( entry.CurrentResolution, isCube );
				entry.IsCube = isCube;
			}
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
		// A map another frame in flight drew keeps its size: that frame's commands draw into it
		if ( entry.RenderedFrame == FrameCount && (desiredResolution < entry.CurrentResolution || DrawnInFlight( entry )) )
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

		// The static cache holds the static casters as they were drawn: a change within the light's reach redraws it
		if ( entry.StaticCache is not null && light.StaticChanges is { } changes && entry.StaticRevision != changes.Revision )
		{
			if ( changes.Touches( entry.StaticRevision, light.Position, light.Radius ) )
			{
				ReleaseTexture( entry.StaticCache, entry.CurrentResolution, entry.IsCube );
				entry.StaticCache = null;
				entry.RenderedFrame = 0;
			}

			entry.StaticRevision = changes.Revision;
		}

		return entry;
	}

	/// <summary>
	/// Whether two tag sets hold the same tags, in any order.
	/// </summary>
	static bool SameTags( uint[] a, uint[] b )
	{
		a ??= [];
		b ??= [];
		if ( a == b ) return true;
		if ( a.Length != b.Length ) return false;

		foreach ( var tag in a )
		{
			if ( Array.IndexOf( b, tag ) < 0 ) return false;
		}

		return true;
	}

	/// <summary>
	/// Take an entry out of its light's list in <see cref="Cache"/>.
	/// </summary>
	static void Unlink( object light, LightEntry entry )
	{
		if ( !Cache.TryGetValue( light, out var first ) ) return;

		if ( first == entry )
		{
			if ( entry.NextFilter is null ) Cache.Remove( light );
			else Cache.AddOrUpdate( light, entry.NextFilter );
			return;
		}

		for ( var previous = first; previous is not null; previous = previous.NextFilter )
		{
			if ( previous.NextFilter != entry ) continue;
			previous.NextFilter = entry.NextFilter;
			return;
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
		SettledCasters.Tick( now );
		EvictFarCascades( now );

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
			if ( alive ) Unlink( light, entry );

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
		ResetFarCascades();
		SettledCasters.Reset();
	}

	/// <summary>
	/// Called when a light is removed from the scene. Returns its shadow map to the pool.
	/// </summary>
	public static void OnLightRemoved( object light )
	{
		if ( !Cache.TryGetValue( light, out var first ) )
			return;

		for ( var entry = first; entry is not null; entry = entry.NextFilter )
		{
			ReleaseTexture( entry.ShadowMap, entry.CurrentResolution, entry.IsCube );
			entry.ShadowMap = null;
			ReleaseTexture( entry.StaticCache, entry.CurrentResolution, entry.IsCube );
			entry.StaticCache = null;
			Entries.Remove( entry );
		}

		Cache.Remove( light );
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
