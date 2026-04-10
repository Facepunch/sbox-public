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

		// How big do we want it, it's okay if our cached is bigger, but not if it's smaller
		var mainViewport = view.GetMainViewport();
		int desiredResolution = GetDesiredResolution( flScreenSize, (int)Math.Max( mainViewport.Rect.Width, mainViewport.Rect.Height ) );

		if ( !Cache.TryGetValue( light, out var cacheEntry ) )
		{
			cacheEntry = new()
			{
				ShadowMap = AcquireTexture( desiredResolution, isCube: true ),
				CurrentResolution = desiredResolution,
				IsCube = true
			};
			Cache.AddOrUpdate( light, cacheEntry );
		}

		// Keep track of how big we actually want it, if we run low on budget we can downgrade these out of scope
		cacheEntry.DesiredResolution = desiredResolution;
		cacheEntry.ScreenSize = flScreenSize;

		// Do we want a bigger resolution for this shadow map now?
		if ( cacheEntry.CurrentResolution < desiredResolution )
		{
			ReleaseTexture( cacheEntry.ShadowMap, cacheEntry.CurrentResolution, cacheEntry.IsCube );
			cacheEntry.ShadowMap = AcquireTexture( desiredResolution, isCube: true );
			cacheEntry.CurrentResolution = desiredResolution;
		}

		GPUProjectedCubeShadow shadow = new();

		float biasScale = ComputeBiasScale( 45f, light.Radius, desiredResolution );

		// Baked lights exclude static objects from shadow maps, their static shadows come from lightmaps
		var excludeFlags = (light.lightNative.GetLightFlags() & 32) != 0 // LIGHTTYPE_FLAGS_BAKED
			? SceneObjectFlags.StaticObject
			: SceneObjectFlags.None;

		for ( int i = 0; i < 6; i++ )
		{
			CFrustum nativeFrustum = CFrustum.Create();
			nativeFrustum.BuildFrustumFromVectors( light.Position, 1.0f, light.Radius, 90.0f, 1.0f, CubeRotations[i].Forward, CubeRotations[i].Left, CubeRotations[i].Up );

			RenderViewport viewport = new( 0, 0, desiredResolution, desiredResolution );
			CSceneSystem.AddShadowView(
				$"{light}_Shadow",
				view, nativeFrustum, viewport, cacheEntry.ShadowMap.native, i, SceneObjectFlags.None, excludeFlags, (int)(ShadowDepthBias * biasScale), ShadowSlopeScale * biasScale
			);

			// Might not need to construct this like this, GetReverseZViewProj is fine if don't need the scale?
			var matrix = nativeFrustum.GetReverseZProj() * (nativeFrustum.GetView() * Matrix.CreateScale( new Vector3( 1, 1, 1 ) ));

			// Set our matrix in the GPU struct
			((Matrix*)&shadow)[i] = matrix;

			nativeFrustum.Delete();

		}

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
