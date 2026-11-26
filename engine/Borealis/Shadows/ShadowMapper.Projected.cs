using System.Numerics;
using System.Runtime.InteropServices;

namespace Borealis.Shadows;

internal partial class ShadowMapper
{
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

	/// <summary>
	/// Finds a cached shadow map or creates a new one.
	/// This is for a single shadow map like a spot light
	/// </summary>
	internal unsafe uint FindOrCreateProjectedShadowMap( in ShadowLight light, float flScreenSize )
	{
		// Cull shadows below the screen size threshold
		if ( flScreenSize < (SizeCullThreshold / 100.0f) )
			return InvalidShadowIndex;

		// Don't exceed GPU buffer capacity
		if ( GPUProjectedShadows.Count >= ProjectedShadowBufferSize )
			return InvalidShadowIndex;

		var cacheEntry = GetOrCreateCacheEntry( light, isCube: false, flScreenSize );

		if ( ReusesLocalMap( cacheEntry, out var inFlight ) )
			return AddProjectedShadow( cacheEntry );

		Matrix ScaleBias = Matrix.Identity;
		ScaleBias._numerics[0, 0] = 0.5f;
		ScaleBias._numerics[1, 1] = -0.5f;
		ScaleBias._numerics[0, 3] = 0.5f;
		ScaleBias._numerics[1, 3] = 0.5f;

		var shadowView = LocalView( light, cacheEntry, light.ConeOuter );

		// Static lights, and ones that have kept still, draw their static and settled casters once into a cache that's copied in
		// each update, and only the casters that move are drawn on top
		if ( DrawsCache( light, cacheEntry, inFlight ) )
		{
			var staticView = shadowView;
			ToCache( light, cacheEntry, ref staticView );
			Renderer.RenderShadowView( staticView );
		}

		ToMap( light, cacheEntry, ref shadowView );

		Matrix viewProjection = Renderer.RenderShadowView( shadowView );

		GPUProjectedShadow shadow;
		shadow.WorldToShadowMatrix = ScaleBias * viewProjection.Transpose();
		shadow.ShadowMapTextureIndex = 0; // filled in by ResolveTextureIndices
		shadow.ShadowHardness = 1.0f + light.Hardness * 4.0f;
		shadow.InvShadowMapRes = 1.0f / (float)cacheEntry.ShadowMap.Resolution;

		cacheEntry.Projected = shadow;
		MarkLocalMapDrawn( cacheEntry );

		return AddProjectedShadow( cacheEntry );
	}

	uint AddProjectedShadow( LightEntry cacheEntry )
	{
		GPUProjectedShadows.Add( cacheEntry.Projected );
		ProjectedShadowMaps.Add( cacheEntry.ShadowMap );
		SpotEntries.Add( cacheEntry );
		UseLocalMap( cacheEntry );
		return (uint)(GPUProjectedShadows.Count - 1);
	}
}
