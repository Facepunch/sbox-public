using NativeEngine;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Sandbox.Rendering;

internal partial class ShadowMapper
{
	[ConVar( "r.shadows.size_cull_threshold", Min = 0.0f, Max = 1.0f, Help = "Threshold of screen size percentage below which projected shadows get culled" )]
	public static float SizeCullThreshold { get; set; } = 0.25f;

	internal static int ProjectedShadowsRendered { get; set; }
	internal static int ProjectedShadowsCulled { get; set; }
	internal static int ProjectedShadowsRenderedLastFrame { get; set; }
	internal static int ProjectedShadowsCulledLastFrame { get; set; }

	[StructLayout( LayoutKind.Sequential )]
	struct GPUProjectedShadow
	{
		public Matrix WorldToShadowMatrix;
		public int ShadowMapTextureIndex;
		public float InvShadowMapRes;
		public float ShadowHardness;
	}

	/// <summary>
	/// All projected shadows for this view
	/// </summary>
	List<GPUProjectedShadow> GPUProjectedShadows { get; set; } = new();

	GpuBuffer<GPUProjectedShadow> GPUProjectedShadowsBuffer { get; set; }

	/// <summary>
	/// Finds a cached shadow map or creates a new one.
	/// This is for a single shadow map like a spot light
	/// </summary>
	internal unsafe uint FindOrCreateProjectedShadowMap( SceneLight light, ISceneView view, float flScreenSize )
	{
		// Cull shadows below the screen size threshold
		if ( flScreenSize < (SizeCullThreshold / 100.0f) )
		{
			ProjectedShadowsCulled++;
			return InvalidShadowIndex;
		}

		// Don't exceed GPU buffer capacity
		if ( GPUProjectedShadows.Count >= ProjectedShadowBufferSize )
		{
			ProjectedShadowsCulled++;
			return InvalidShadowIndex;
		}

		bool isBakedLight = (light.lightNative.GetLightFlags() & 32) != 0; // LIGHTTYPE_FLAGS_BAKED
		bool isStaticLight = light.GameObject.IsValid() && light.GameObject.IsStatic;

		// How big do we want it, it's okay if our cached is bigger, but not if it's smaller
		var mainViewport = view.GetMainViewport();
		int desiredResolution = GetDesiredResolution( flScreenSize, (int)Math.Max( mainViewport.Rect.Width, mainViewport.Rect.Height ) );

		var cacheEntry = GetOrCreateCacheEntry( light, desiredResolution, isCube: false, flScreenSize );

		Matrix ScaleBias = Matrix.Identity;
		ScaleBias._numerics[0, 0] = 0.5f;
		ScaleBias._numerics[1, 1] = -0.5f;
		ScaleBias._numerics[0, 3] = 0.5f;
		ScaleBias._numerics[1, 3] = 0.5f;

		CFrustum nativeFrustum = CFrustum.Create();
		nativeFrustum.BuildFrustumFromVectors( light.Position, 1.0f, light.Radius, 2.0f * light.lightNative.GetPhi(), 1.0f, light.Rotation.Forward, light.Rotation.Left, light.Rotation.Up );

		float biasScale = ComputeBiasScale( light.lightNative.GetPhi(), light.Radius, cacheEntry.CurrentResolution );

		// Static lights render their static casters once into a cache that gets copied in
		// each frame, and only dynamic casters are re-rendered on top.
		if ( isStaticLight && !isBakedLight && cacheEntry.StaticCache is null )
		{
			cacheEntry.StaticCache = AcquireTexture( cacheEntry.CurrentResolution, isCube: false );

			// Render static objects to the static cache, once
			CSceneSystem.AddShadowView( cacheEntry.DebugName + "_StaticCache",
				view,
				nativeFrustum,
				new( 0, 0, cacheEntry.CurrentResolution, cacheEntry.CurrentResolution ),
				cacheEntry.StaticCache.native,
				0,
				requiredFlags: SceneObjectFlags.StaticObject,
				excludedFlags: SceneObjectFlags.None,
				(int)(ShadowDepthBias * biasScale),
				ShadowSlopeScale * biasScale );
		}

		bool useStaticCache = cacheEntry.StaticCache is not null;

		// Baked lights exclude static objects from shadow maps, their static shadows come from lightmaps.
		// Cached lights exclude them too - their static shadows come from the static cache.
		var excludeFlags = isBakedLight || useStaticCache
			? SceneObjectFlags.StaticObject
			: SceneObjectFlags.None;

		RenderViewport viewport = new( 0, 0, cacheEntry.CurrentResolution, cacheEntry.CurrentResolution );

		CSceneSystem.AddShadowView( cacheEntry.DebugName,
							view,
							nativeFrustum,
							viewport,
							cacheEntry.ShadowMap.native,
							0,
							SceneObjectFlags.None,
							excludeFlags,
							(int)(ShadowDepthBias * biasScale),
							ShadowSlopeScale * biasScale,
							cachedShadowTexture: useStaticCache ? cacheEntry.StaticCache.native : default );

		// Render targets don't use texture streaming surely, is this needed?
		cacheEntry.ShadowMap.MarkUsed( 2048 );

		GPUProjectedShadow shadow;
		shadow.WorldToShadowMatrix = ScaleBias * nativeFrustum.GetReverseZViewProj();
		shadow.ShadowMapTextureIndex = cacheEntry.ShadowMap.Index;
		shadow.ShadowHardness = 1.0f + light.ShadowHardness * 4.0f;
		shadow.InvShadowMapRes = 1.0f / (float)cacheEntry.ShadowMap.Width;

		nativeFrustum.Delete();

		GPUProjectedShadows.Add( shadow );
		ProjectedShadowsRendered++;
		ShadowsAllocated++;

		cacheEntry.LastFrame = RealTime.Now;

		// Return the index we just inserted
		var index = GPUProjectedShadows.Count - 1;
		cacheEntry.DebugLightIndex = index;
		return (uint)index;
	}
}
