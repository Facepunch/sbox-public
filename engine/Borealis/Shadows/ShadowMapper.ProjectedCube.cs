using System.Numerics;
using System.Runtime.InteropServices;

namespace Borealis.Shadows;

internal partial class ShadowMapper
{
	static readonly TextureFormat LocalShadowDepthFormat = TextureFormat.D16;

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

		// How big do we want it, it's okay if our cached is bigger, but not if it's smaller
		int desiredResolution = GetDesiredResolution( flScreenSize, (int)Math.Max( View.ViewportSize.x, View.ViewportSize.y ) );

		var cacheEntry = GetOrCreateCacheEntry( light, desiredResolution, isCube: true, flScreenSize );

		// Already rendered this frame, or not this light's turn. A light filling the view never waits its turn. A frame in flight
		// drew it this frame: it's drawn again for this one, submitted first
		var inFlight = DrawnInFlight( cacheEntry );
		if ( !inFlight && (cacheEntry.RenderedFrame == FrameCount || (cacheEntry.RenderedFrame != 0 && !cacheEntry.Scheduled && cacheEntry.ScreenSize < 1f)) )
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

		// Static lights, and ones that have kept still, render their static casters once into a cache that gets copied in
		// each frame, and only dynamic casters are re-rendered on top.
		if ( CachesStatic( light, cacheEntry ) && !isBakedLight && (cacheEntry.StaticCache is null || (inFlight && cacheEntry.StaticRenderedFrame == FrameCount)) )
		{
			cacheEntry.StaticCache ??= AcquireTexture( desiredResolution, isCube: true );

			// Render static objects to the static cache, once
			var staticView = shadowView;
			staticView.Name = cacheEntry.DebugName + "_StaticCache";
			staticView.Target = cacheEntry.StaticCache;
			staticView.StaticFilter = StaticFilter.OnlyStatic;

			for ( int i = 0; i < 6; i++ )
			{
				staticView.Rotation = CubeRotations[i];
				staticView.Slice = i;
				Renderer.RenderShadowView( staticView );
			}

			cacheEntry.StaticRenderedFrame = FrameCount;
			cacheEntry.StaticRevision = light.StaticChanges?.Revision ?? 0;
		}

		// Not once the light is baked: the lightmap holds its static casters (a cache made before it was baked would shadow them twice)
		bool useStaticCache = cacheEntry.StaticCache is not null && !isBakedLight;

		// Baked lights exclude static objects from shadow maps, their static shadows come from lightmaps.
		// Cached lights exclude them too - their static shadows come from the static cache.
		shadowView.Target = cacheEntry.ShadowMap;
		shadowView.StaticFilter = isBakedLight || useStaticCache ? StaticFilter.NoStatic : StaticFilter.Any;

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
		cacheEntry.RenderedFrame = FrameCount;
		cacheEntry.DrawnBy = this;
		Drawn.Add( cacheEntry );
		ProjectedShadowsRendered++;

		return AddProjectedCubeShadow( cacheEntry );
	}

	uint AddProjectedCubeShadow( LightEntry cacheEntry )
	{
		GPUProjectedCubeShadows.Add( cacheEntry.Cube );
		ProjectedCubeShadowMaps.Add( cacheEntry.ShadowMap );
		PointEntries.Add( cacheEntry );
		ShadowsAllocated++;

		cacheEntry.LastFrame = RealTime.Now;
		cacheEntry.UsedFrame = FrameCount;

		var index = GPUProjectedCubeShadows.Count - 1;
		cacheEntry.DebugLightIndex = index;
		return (uint)index;
	}
}
