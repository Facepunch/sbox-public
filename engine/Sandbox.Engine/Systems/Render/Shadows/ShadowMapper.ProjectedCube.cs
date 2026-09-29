using NativeEngine;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Sandbox.Rendering;

internal partial class ShadowMapper
{
	static readonly ImageFormat LocalShadowDepthFormat = ImageFormat.D16;

	/// <summary>
	/// The cube faces, in the order the shader picks them (GetCubemapFace in ProjectedShadowCube.hlsl).
	/// </summary>
	internal static readonly Rotation[] CubeRotations =
	{
		Rotation.LookAt( Vector3.Backward, Vector3.Right ),
		Rotation.LookAt( Vector3.Forward, Vector3.Right ),
		Rotation.LookAt( Vector3.Right, Vector3.Up ),
		Rotation.LookAt( Vector3.Left, Vector3.Down ),
		Rotation.LookAt( Vector3.Down, Vector3.Right ),
		Rotation.LookAt( Vector3.Up, Vector3.Right )
	};


	[StructLayout( LayoutKind.Sequential )]
	internal struct GPUProjectedCubeShadow
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

	static readonly string[] CubeFaceNames = ["Cube face 0", "Cube face 1", "Cube face 2", "Cube face 3", "Cube face 4", "Cube face 5"];

	internal unsafe uint FindOrCreateProjectedCubeShadowMap( in ShadowLight light, float flScreenSize )
	{
		// Don't exceed GPU buffer capacity
		if ( GPUProjectedCubeShadows.Count >= ProjectedCubeShadowBufferSize )
		{
			ProjectedShadowsCulled++;
			return InvalidShadowIndex;
		}

		bool isBakedLight = light.Baked;
		bool isStaticLight = light.Static;

		// How big do we want it, it's okay if our cached is bigger, but not if it's smaller
		int desiredResolution = GetDesiredResolution( flScreenSize, (int)Math.Max( View.ViewportSize.x, View.ViewportSize.y ) );

		var cacheEntry = GetOrCreateCacheEntry( light, desiredResolution, isCube: true, flScreenSize );

		// Already rendered this frame, or not this light's turn. A light filling the view never waits its turn.
		if ( cacheEntry.RenderedFrame == Application.FrameCount || (cacheEntry.RenderedFrame != 0 && !cacheEntry.Scheduled && cacheEntry.ScreenSize < 1f) )
			return AddProjectedCubeShadow( cacheEntry );

		// The entry may keep a bigger map than this view asked for
		desiredResolution = cacheEntry.CurrentResolution;

		GPUProjectedCubeShadow shadow = new();

		float biasScale = ComputeBiasScale( 45f, light.Radius, desiredResolution );

		var shadowView = new ShadowViewDesc
		{
			Name = cacheEntry.DebugName,
			Position = light.Position,
			FieldOfView = 90.0f,
			ZNear = 1.0f,
			ZFar = light.Radius,
			Resolution = desiredResolution,
			DepthBias = (int)(ShadowDepthBias * biasScale),
			SlopeScaledDepthBias = ShadowSlopeScale * biasScale,
		};

		// Static lights render their static casters once into a cache that gets copied in
		// each frame, and only dynamic casters are re-rendered on top.
		if ( isStaticLight && !isBakedLight && cacheEntry.StaticCache is null )
		{
			cacheEntry.StaticCache = AcquireTexture( desiredResolution, isCube: true );

			// Render static objects to the static cache, once
			var staticView = shadowView;
			staticView.Name = cacheEntry.DebugName + "_StaticCache";
			staticView.Target = cacheEntry.StaticCache;
			staticView.RequiredFlags = SceneObjectFlags.StaticObject;

			for ( int i = 0; i < 6; i++ )
			{
				staticView.Rotation = CubeRotations[i];
				staticView.Slice = i;
				Renderer.RenderShadowView( staticView );
			}
		}

		bool useStaticCache = cacheEntry.StaticCache is not null;

		// Baked lights exclude static objects from shadow maps, their static shadows come from lightmaps.
		// Cached lights exclude them too - their static shadows come from the static cache.
		shadowView.Target = cacheEntry.ShadowMap;
		shadowView.ExcludedFlags = isBakedLight || useStaticCache
			? SceneObjectFlags.StaticObject
			: SceneObjectFlags.None;

		// The cached static shadows are copied into the shadow map (once, on the first face), dynamic objects render on top
		shadowView.CachedStatic = useStaticCache ? cacheEntry.StaticCache : null;

		for ( int i = 0; i < 6; i++ )
		{
			shadowView.Rotation = CubeRotations[i];
			shadowView.Slice = i;
			Matrix viewProjection = Renderer.RenderShadowView( shadowView );

			// Set our matrix in the GPU struct
			((Matrix*)&shadow)[i] = viewProjection.Transpose();
		}

		shadow.ShadowMapTextureCubeIndex = 0; // filled in by ResolveTextureIndices
		shadow.LightPosition = light.Position;
		shadow.InvShadowMapRes = 1.0f / desiredResolution;
		shadow.ShadowHardness = 1.0f + light.Hardness * 4.0f;

		cacheEntry.Cube = shadow;
		cacheEntry.RenderedFrame = Application.FrameCount;
		ProjectedShadowsRendered++;

		return AddProjectedCubeShadow( cacheEntry );
	}

	uint AddProjectedCubeShadow( LightEntry cacheEntry )
	{
		GPUProjectedCubeShadows.Add( cacheEntry.Cube );
		ProjectedCubeShadowMaps.Add( cacheEntry.ShadowMap );
		ShadowsAllocated++;

		cacheEntry.LastFrame = RealTime.Now;
		cacheEntry.UsedFrame = Application.FrameCount;

		var index = GPUProjectedCubeShadows.Count - 1;
		cacheEntry.DebugLightIndex = index;
		return (uint)index;
	}

}
