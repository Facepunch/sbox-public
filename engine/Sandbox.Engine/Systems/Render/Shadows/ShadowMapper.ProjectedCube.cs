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

		// Static lights copy their whole static cache into the map, so they render every face
		var faces = isStaticLight && !isBakedLight ? (byte)0b111111 : VisibleFaces( light );

		// Already rendered this frame, or not this light's turn. A light filling the view never waits its turn.
		var reusable = cacheEntry.RenderedFrame == Application.FrameCount || (cacheEntry.RenderedFrame != 0 && !cacheEntry.Scheduled && cacheEntry.ScreenSize < 1f);

		// The cached map only holds the faces earlier views needed.
		var hasNeededFaces = (faces & ~cacheEntry.ValidFaces) == 0;

		if ( reusable && hasNeededFaces )
			return AddProjectedCubeShadow( cacheEntry );

		// The entry may keep a bigger map than this view asked for
		desiredResolution = cacheEntry.CurrentResolution;

		// Get cached matrices so culled faces use their last ones
		GPUProjectedCubeShadow shadow = cacheEntry.Cube;

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
			// Culled: nothing this face shadows is in the view, so don't render it
			var culled = (faces & (1 << i)) == 0;
			if ( culled )
				continue;

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
		cacheEntry.ValidFaces |= faces;
		cacheEntry.RenderedFrame = Application.FrameCount;
		ProjectedShadowsRendered++;

		return AddProjectedCubeShadow( cacheEntry );
	}

	/// <summary>
	/// The cube faces this light can shadow in the current view, a bit per face.
	/// </summary>
	byte VisibleFaces( in ShadowLight light )
	{
		// Slightly wider than the face, so filtering at its edges stays covered
		const float EdgeMargin = 1.1f;
		Span<System.Numerics.Vector4> pyramid = stackalloc System.Numerics.Vector4[5];
		byte faces = 0;

		for ( int i = 0; i < 6; i++ )
		{
			var face = CubeRotations[i];
			var forward = light.Position + face.Forward * light.Radius;
			var right = face.Right * light.Radius * EdgeMargin;
			var up = face.Up * light.Radius * EdgeMargin;
			pyramid[0] = Clip( light.Position );
			pyramid[1] = Clip( forward + right + up );
			pyramid[2] = Clip( forward + right - up );
			pyramid[3] = Clip( forward - right + up );
			pyramid[4] = Clip( forward - right - up );

			if ( !OutsideOneSide( pyramid ) ) faces |= (byte)(1 << i);
		}

		return faces;

		System.Numerics.Vector4 Clip( Vector3 world ) => System.Numerics.Vector4.Transform( new System.Numerics.Vector4( world, 1 ), View.WorldToProjection );
	}

	/// <summary>
	/// Whether all the points are past the same edge of the view (left, right, top, bottom, or in front of the near plane), so nothing between them can be on screen.
	/// </summary>
	static bool OutsideOneSide( ReadOnlySpan<System.Numerics.Vector4> points )
	{
		bool left = true, right = true, bottom = true, top = true, near = true;
		foreach ( var p in points )
		{
			left &= p.X < -p.W;
			right &= p.X > p.W;
			bottom &= p.Y < -p.W;
			top &= p.Y > p.W;
			near &= p.Z > p.W;
		}

		return left || right || bottom || top || near;
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
