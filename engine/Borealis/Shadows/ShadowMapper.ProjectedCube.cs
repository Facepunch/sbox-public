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

	internal unsafe uint FindOrCreateProjectedCubeShadowMap( in ShadowLight light, float flScreenSize )
	{
		// Don't exceed GPU buffer capacity
		if ( GPUProjectedCubeShadows.Count >= ProjectedCubeShadowBufferSize )
			return InvalidShadowIndex;

		var cacheEntry = GetOrCreateCacheEntry( light, isCube: true, flScreenSize );

		if ( ReusesLocalMap( cacheEntry, out var inFlight ) )
			return AddProjectedCubeShadow( cacheEntry );

		// The entry may keep a bigger map than this view asked for
		int resolution = cacheEntry.CurrentResolution;

		GPUProjectedCubeShadow shadow = new();

		var shadowView = LocalView( light, cacheEntry, 45.0f );

		// Static lights, and ones that have kept still, draw their static and settled casters once into a cache that's copied in
		// each update, and only the casters that move are drawn on top
		if ( DrawsCache( light, cacheEntry, inFlight ) )
		{
			var staticView = shadowView;
			ToCache( light, cacheEntry, ref staticView );
			for ( int i = 0; i < 6; i++ )
			{
				staticView.Rotation = CubeRotations[i];
				staticView.Slice = i;
				Renderer.RenderShadowView( staticView );
			}
		}

		// The cache is copied into the map once, on the first face
		ToMap( light, cacheEntry, ref shadowView );

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
		shadow.InvShadowMapRes = 1.0f / resolution;
		shadow.ShadowHardness = 1.0f + light.Hardness * 4.0f;

		cacheEntry.Cube = shadow;
		MarkLocalMapDrawn( cacheEntry );

		return AddProjectedCubeShadow( cacheEntry );
	}

	uint AddProjectedCubeShadow( LightEntry cacheEntry )
	{
		GPUProjectedCubeShadows.Add( cacheEntry.Cube );
		ProjectedCubeShadowMaps.Add( cacheEntry.ShadowMap );
		PointEntries.Add( cacheEntry );
		UseLocalMap( cacheEntry );
		return (uint)(GPUProjectedCubeShadows.Count - 1);
	}
}
