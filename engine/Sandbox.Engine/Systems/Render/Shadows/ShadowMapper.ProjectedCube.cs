using NativeEngine;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Sandbox.Rendering;

internal partial class ShadowMapper
{
	static readonly ImageFormat LocalShadowDepthFormat = ImageFormat.D16;

	static readonly Rotation[] CubeRotations =
	{
		Rotation.LookAt( Vector3.Backward, Vector3.Right ),
		Rotation.LookAt( Vector3.Forward, Vector3.Right ),
		Rotation.LookAt( Vector3.Right, Vector3.Up ),
		Rotation.LookAt( Vector3.Left, Vector3.Down ),
		Rotation.LookAt( Vector3.Down, Vector3.Right ),
		Rotation.LookAt( Vector3.Up, Vector3.Right )
	};


	[StructLayout( LayoutKind.Sequential )]
	struct GPUProjectedCubeShadow
	{
		public Matrix ShadowViewProjectionMatrix0;
		public Matrix ShadowViewProjectionMatrix1;
		public Matrix ShadowViewProjectionMatrix2;
		public Matrix ShadowViewProjectionMatrix3;
		public Matrix ShadowViewProjectionMatrix4;
		public Matrix ShadowViewProjectionMatrix5;
		public Vector3 LightPosition;
		public uint ShadowMapTextureCubeIndex;
		public float InvShadowMapRes;
		public float ShadowHardness;
	}

	/// <summary>
	/// All cube projected shadows (special case)
	/// </summary>
	List<GPUProjectedCubeShadow> GPUProjectedCubeShadows { get; set; } = new();

	GpuBuffer<GPUProjectedCubeShadow> GPUProjectedCubeShadowsBuffer { get; set; }

	internal unsafe uint FindOrCreateProjectedCubeShadowMap( SceneLight light, ISceneView view, float flScreenSize )
	{
		// Don't exceed GPU buffer capacity
		if ( GPUProjectedCubeShadows.Count >= ProjectedCubeShadowBufferSize )
		{
			ProjectedShadowsCulled++;
			return InvalidShadowIndex;
		}

		bool isBakedLight = (light.lightNative.GetLightFlags() & 32) != 0; // LIGHTTYPE_FLAGS_BAKED
		bool isStaticLight = light.GameObject.IsValid() && light.GameObject.IsStatic;

		// How big do we want it, it's okay if our cached is bigger, but not if it's smaller
		var mainViewport = view.GetMainViewport();
		int desiredResolution = GetDesiredResolution( flScreenSize, (int)Math.Max( mainViewport.Rect.Width, mainViewport.Rect.Height ) );

		var cacheEntry = GetOrCreateCacheEntry( light, desiredResolution, isCube: true, flScreenSize );

		GPUProjectedCubeShadow shadow = new();

		float biasScale = ComputeBiasScale( 45f, light.Radius, desiredResolution );

		CFrustum nativeFrustum = CFrustum.Create();
		RenderViewport viewport = new( 0, 0, desiredResolution, desiredResolution );

		// Static lights render their static casters once into a cache that gets copied in
		// each frame, and only dynamic casters are re-rendered on top.
		if ( isStaticLight && !isBakedLight && cacheEntry.StaticCache is null )
		{
			cacheEntry.StaticCache = AcquireTexture( desiredResolution, isCube: true );

			// Render static objects to the static cache, once
			for ( int i = 0; i < 6; i++ )
			{
				nativeFrustum.BuildFrustumFromVectors( light.Position, 1.0f, light.Radius, 90.0f, 1.0f, CubeRotations[i].Forward, CubeRotations[i].Left, CubeRotations[i].Up );

				CSceneSystem.AddShadowView(
					cacheEntry.DebugName + "_StaticCache",
					view, nativeFrustum, viewport, cacheEntry.StaticCache.native, i, SceneObjectFlags.StaticObject, SceneObjectFlags.None, (int)(ShadowDepthBias * biasScale), ShadowSlopeScale * biasScale
				);
			}
		}

		bool useStaticCache = cacheEntry.StaticCache is not null;

		// Baked lights exclude static objects from shadow maps, their static shadows come from lightmaps.
		// Cached lights exclude them too - their static shadows come from the static cache.
		var excludeFlags = isBakedLight || useStaticCache
			? SceneObjectFlags.StaticObject
			: SceneObjectFlags.None;

		for ( int i = 0; i < 6; i++ )
		{
			nativeFrustum.BuildFrustumFromVectors( light.Position, 1.0f, light.Radius, 90.0f, 1.0f, CubeRotations[i].Forward, CubeRotations[i].Left, CubeRotations[i].Up );

			CSceneSystem.AddShadowView(
				cacheEntry.DebugName,
				view, nativeFrustum, viewport, cacheEntry.ShadowMap.native, i, SceneObjectFlags.None, excludeFlags, (int)(ShadowDepthBias * biasScale), ShadowSlopeScale * biasScale,
				// The cached static shadows are copied into the shadow map (once, on the first face), dynamic objects render on top
				cachedShadowTexture: useStaticCache ? cacheEntry.StaticCache.native : default
			);

			// Set our matrix in the GPU struct
			((Matrix*)&shadow)[i] = nativeFrustum.GetReverseZViewProj();
		}

		nativeFrustum.Delete();

		shadow.ShadowMapTextureCubeIndex = (uint)cacheEntry.ShadowMap.Index;
		shadow.LightPosition = light.Position;
		shadow.InvShadowMapRes = 1.0f / desiredResolution;
		shadow.ShadowHardness = 1.0f + light.ShadowHardness * 4.0f;

		cacheEntry.LastFrame = RealTime.Now;

		GPUProjectedCubeShadows.Add( shadow );
		ShadowsAllocated++;

		var index = GPUProjectedCubeShadows.Count - 1;
		cacheEntry.DebugLightIndex = index;
		return (uint)index;
	}

}
