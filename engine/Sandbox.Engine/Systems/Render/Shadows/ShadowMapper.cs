using NativeEngine;
using System.Numerics;
using System.Runtime.CompilerServices;
namespace Sandbox.Rendering;

/// <summary>
/// ShadowMapper 
/// It is owned by a CLightBinnerStandard in c++ which are pooled and reinitialized with CLightBinnerStandard::InitForView
/// We then also get called by CLightBinnerStandard::UploadLightingToGPU
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

	[ConVar( "r.shadows.debug", Help = "Show shadow debug overlay with memory allocation and budget info." )]
	public static bool DebugEnabled { get; set; } = false;

	[ConVar( "r.shadows.csm.enabled", Help = "Enable directional light (CSM) shadows." )]
	public static bool CSMEnabled { get; set; } = true;

	[ConVar( "r.shadows.local.enabled", Help = "Enable local light (spot/point) shadows." )]
	public static bool LocalShadowsEnabled { get; set; } = true;

	[ConVar( "r.shadows.depthbias", Min = -256, Max = 0, Help = "Rasterizer constant depth bias applied during shadow map rendering. More negative = stronger bias." )]
	public static int ShadowDepthBias { get; set; } = -1;

	[ConVar( "r.shadows.slopescale", Min = -16.0f, Max = 0.0f, Help = "Rasterizer slope-scaled depth bias applied during shadow map rendering. More negative = stronger bias on angled surfaces." )]
	public static float ShadowSlopeScale { get; set; } = -1.5f;

	const uint InvalidShadowIndex = 0xFFFFFFFF;

	// Absolute upper limits, no harm in increasing these
	const int ProjectedShadowBufferSize = 512;
	const int ProjectedCubeShadowBufferSize = 256;

	int ShadowsAllocated { get; set; }


	public ShadowMapper()
	{
		// This ties our buffers to a specific lightbinner.
		// They're all the same size at the end of the day, we could pool them normally.
		GPUProjectedShadowsBuffer = new( ProjectedShadowBufferSize );
		GPUProjectedCubeShadowsBuffer = new( ProjectedCubeShadowBufferSize );
	}

	ISceneView SceneView { get; set; }

	internal void InitForView( ISceneView sceneView )
	{
		SceneView = sceneView;

		// Evict stale shadow maps and clean the texture pool
		Update();

		// Reset all our lists
		GPUProjectedShadows.Clear();
		GPUProjectedCubeShadows.Clear();
		GPUDirectionalLightData.CascadeCount = 0;
		GPUDirectionalLightData.Enabled = false;
		ShadowsAllocated = 0;

		// Save statistics from last frame, then reset
		ProjectedShadowsRenderedLastFrame = ProjectedShadowsRendered;
		ProjectedShadowsCulledLastFrame = ProjectedShadowsCulled;
		ProjectedShadowsRendered = 0;
		ProjectedShadowsCulled = 0;
	}

	internal void SetShaderAttributes( RenderAttributes attributes )
	{
		if ( attributes is null )
			return;

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
		GPUProjectedShadowsBuffer.SetData( GPUProjectedShadows );
		GPUProjectedCubeShadowsBuffer.SetData( GPUProjectedCubeShadows );
	}

	public class LightEntry
	{
		public float LastFrame;
		public Texture StaticCache;
		public Texture ShadowMap;
		public float ScreenSize;
		public int CurrentResolution;
		public int DesiredResolution;
		public int DebugLightIndex;
		public bool IsCube;
	}

	public static ConditionalWeakTable<SceneLight, LightEntry> Cache = new();

	public static long MemorySize => Cache.Where( x => x.Value.ShadowMap is not null ).Sum( x => g_pRenderDevice.ComputeTextureMemorySize( x.Value.ShadowMap.native ) );

	struct PooledTexture
	{
		public Texture Texture;
		public float ReturnedAt;
	}

	record struct PoolKey( int Resolution, bool IsCube );

	static readonly Dictionary<PoolKey, Stack<PooledTexture>> TexturePool = new();

	/// <summary>
	/// How long a texture sits unused in the cache before being evicted and returned to the pool.
	/// </summary>
	const float CacheEvictionTime = 2.0f;

	/// <summary>
	/// How long a texture sits unused in the pool before being disposed.
	/// </summary>
	const float PoolDisposeTime = 10.0f;

	static Texture AcquireTexture( int resolution, bool isCube )
	{
		var key = new PoolKey( resolution, isCube );
		if ( TexturePool.TryGetValue( key, out var stack ) && stack.Count > 0 )
			return stack.Pop().Texture;

		if ( isCube )
			return Texture.CreateCube( resolution, resolution, LocalShadowDepthFormat ).AsRenderTarget().Finish();

		return Texture.CreateRenderTarget( $"ShadowPool_{resolution}", LocalShadowDepthFormat, new Vector2( resolution ) );
	}

	static void ReleaseTexture( Texture texture, int resolution, bool isCube )
	{
		if ( texture is null )
			return;

		var key = new PoolKey( resolution, isCube );
		if ( !TexturePool.TryGetValue( key, out var stack ) )
		{
			stack = new Stack<PooledTexture>();
			TexturePool[key] = stack;
		}

		stack.Push( new PooledTexture { Texture = texture, ReturnedAt = RealTime.Now } );
	}

	/// <summary>
	/// Evict shadow maps from lights that haven't been rendered recently,
	/// and dispose pooled textures that have sat idle for too long.
	/// </summary>
	public static void Update()
	{
		float now = RealTime.Now;

		// Evict stale cache entries
		List<SceneLight> toEvict = null;
		foreach ( var kvp in Cache )
		{
			if ( now - kvp.Value.LastFrame < CacheEvictionTime )
				continue;

			toEvict ??= new();
			toEvict.Add( kvp.Key );
		}

		if ( toEvict is not null )
		{
			foreach ( var light in toEvict )
			{
				if ( Cache.TryGetValue( light, out var entry ) )
				{
					ReleaseTexture( entry.ShadowMap, entry.CurrentResolution, entry.IsCube );
					entry.ShadowMap = null;
					Cache.Remove( light );
				}
			}
		}

		// Dispose pooled textures that have been idle for too long
		foreach ( var kvp in TexturePool )
		{
			var stack = kvp.Value;
			while ( stack.Count > 0 && now - stack.Peek().ReturnedAt > PoolDisposeTime )
			{
				stack.Pop().Texture?.Dispose();
			}
		}
	}

	/// <summary>
	/// Called when a light is removed from the scene. Returns its shadow map to the pool.
	/// </summary>
	public static void OnLightRemoved( SceneLight light )
	{
		if ( !Cache.TryGetValue( light, out var entry ) )
			return;

		ReleaseTexture( entry.ShadowMap, entry.CurrentResolution, entry.IsCube );
		entry.ShadowMap = null;
		Cache.Remove( light );
	}

	/// <summary>
	/// Computes a per-light bias scale factor based on the shadow frustum's texel size.
	/// Wider cones and larger ranges produce bigger shadow map texels in world space,
	/// requiring proportionally more bias to prevent acne. Matches Unity URP's approach
	/// of scaling bias by <c>frustumSize / resolution</c>.
	/// </summary>
	static float ComputeBiasScale( float halfAngleDegrees, float range, int resolution )
	{
		float frustumSize = MathF.Tan( halfAngleDegrees * MathF.PI / 180f ) * range;
		float texelSize = frustumSize / resolution;

		// Normalize against a reference texel size so that a typical mid-range spotlight
		// (e.g. 45° half-angle, 200 range, 1024 res) gets a scale of ~1.0.
		const float ReferenceTexelSize = 0.2f;
		return MathF.Max( 1f, texelSize / ReferenceTexelSize );
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
	/// Find a cached shadow map or create a new one for the light and view.
	/// Returns an index to the shadow maps structured buffer
	/// </summary>
	internal unsafe uint FindOrCreateShadowMaps( SceneLight sceneObject, ISceneView view, float flScreenSize )
	{
		if ( !LocalShadowsEnabled )
			return InvalidShadowIndex;

		// Unified shadow budget — reject if we've hit the limit
		if ( ShadowsAllocated >= MaxShadows )
		{
			ProjectedShadowsCulled++;
			return InvalidShadowIndex;
		}

		return sceneObject.lightNative.GetLightType() switch
		{
			3 => FindOrCreateProjectedShadowMap( sceneObject, view, flScreenSize ),
			1 => FindOrCreateProjectedCubeShadowMap( sceneObject, view, flScreenSize ),
			_ => InvalidShadowIndex
		};
	}

	internal int DoDirectionalLight( SceneLight sceneObject, ISceneView view )
	{
		GPUDirectionalLightData.Enabled = true;

		if ( !CSMEnabled )
			return 0;

		FindOrCreateDirectionalShadowMaps( sceneObject, view );
		return 0;
	}

	public static void OnSceneObjectTransformCreated() { }
	public static void OnSceneObjectTransformRemoved() { }
	public static void OnSceneObjectTransformChanged() { }
}
