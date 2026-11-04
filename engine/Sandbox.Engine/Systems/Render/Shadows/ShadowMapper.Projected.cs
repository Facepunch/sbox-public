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
	internal struct GPUProjectedShadow
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
	internal unsafe uint FindOrCreateProjectedShadowMap( in ShadowLight light, float flScreenSize )
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

		bool isBakedLight = light.Baked;
		bool isStaticLight = light.Static;

		// How big do we want it, it's okay if our cached is bigger, but not if it's smaller
		int desiredResolution = GetDesiredResolution( flScreenSize, (int)Math.Max( View.ViewportSize.x, View.ViewportSize.y ) );

		var cacheEntry = GetOrCreateCacheEntry( light, desiredResolution, isCube: false, flScreenSize );

		// Already rendered this frame, or not this light's turn. A light filling the view never waits its turn.
		if ( cacheEntry.RenderedFrame == Application.FrameCount || (cacheEntry.RenderedFrame != 0 && !cacheEntry.Scheduled && cacheEntry.ScreenSize < 1f) )
			return AddProjectedShadow( cacheEntry );

		Matrix ScaleBias = Matrix.Identity;
		ScaleBias._numerics[0, 0] = 0.5f;
		ScaleBias._numerics[1, 1] = -0.5f;
		ScaleBias._numerics[0, 3] = 0.5f;
		ScaleBias._numerics[1, 3] = 0.5f;

		float biasScale = ComputeBiasScale( light.ConeOuter, light.Radius, cacheEntry.CurrentResolution );

		var shadowView = new ShadowViewDesc
		{
			Name = cacheEntry.DebugName,
			Position = light.Position,
			Rotation = light.Rotation,
			FieldOfView = 2.0f * light.ConeOuter,
			ZNear = 1.0f,
			ZFar = light.Radius,
			Resolution = cacheEntry.CurrentResolution,
			DepthBias = (int)(ShadowDepthBias * biasScale),
			SlopeScaledDepthBias = ShadowSlopeScale * biasScale,
		};

		// Static lights render their static casters once into a cache that gets copied in
		// each frame, and only dynamic casters are re-rendered on top.
		if ( isStaticLight && !isBakedLight && cacheEntry.StaticCache is null )
		{
			cacheEntry.StaticCache = AcquireTexture( cacheEntry.CurrentResolution, isCube: false );

			// Render static objects to the static cache, once
			var staticView = shadowView;
			staticView.Name = cacheEntry.DebugName + "_StaticCache";
			staticView.Target = cacheEntry.StaticCache;
			staticView.RequiredFlags = SceneObjectFlags.StaticObject;
			Renderer.RenderShadowView( staticView );
		}

		bool useStaticCache = cacheEntry.StaticCache is not null;

		// Baked lights exclude static objects from shadow maps, their static shadows come from lightmaps.
		// Cached lights exclude them too - their static shadows come from the static cache.
		shadowView.Target = cacheEntry.ShadowMap;
		shadowView.ExcludedFlags = isBakedLight || useStaticCache
			? SceneObjectFlags.StaticObject
			: SceneObjectFlags.None;
		shadowView.CachedStatic = useStaticCache ? cacheEntry.StaticCache : null;

		Matrix viewProjection = Renderer.RenderShadowView( shadowView );

		// Render targets don't use texture streaming surely, is this needed?
		if ( cacheEntry.ShadowMap.HasTexture )
			cacheEntry.ShadowMap.Texture.MarkUsed( 2048 );

		GPUProjectedShadow shadow;
		shadow.WorldToShadowMatrix = ScaleBias * viewProjection.Transpose();
		shadow.ShadowMapTextureIndex = 0; // filled in by ResolveTextureIndices
		shadow.ShadowHardness = 1.0f + light.Hardness * 4.0f;
		shadow.InvShadowMapRes = 1.0f / (float)cacheEntry.ShadowMap.Resolution;

		cacheEntry.Projected = shadow;
		cacheEntry.RenderedFrame = Application.FrameCount;
		ProjectedShadowsRendered++;

		return AddProjectedShadow( cacheEntry );
	}

	uint AddProjectedShadow( LightEntry cacheEntry )
	{
		GPUProjectedShadows.Add( cacheEntry.Projected );
		ProjectedShadowMaps.Add( cacheEntry.ShadowMap );
		ShadowsAllocated++;

		cacheEntry.LastFrame = RealTime.Now;
		cacheEntry.UsedFrame = Application.FrameCount;

		// Return the index we just inserted
		var index = GPUProjectedShadows.Count - 1;
		cacheEntry.DebugLightIndex = index;
		return (uint)index;
	}
}
