using NativeEngine;
using System.Runtime.InteropServices;

namespace Sandbox.Rendering;

[StructLayout( LayoutKind.Sequential, Pack = 0 )]
unsafe struct GPUDirectionalLight
{
	public Vector4 Color;
	public Vector4 Direction;

	public Matrix WorldToShadowMatrices0;
	public Matrix WorldToShadowMatrices1;
	public Matrix WorldToShadowMatrices2;
	public Matrix WorldToShadowMatrices3;
	public fixed int ShadowMapIndex[4];
	public uint CascadeCount;
	public float InverseShadowMapSize;
	public Vector2 Padding;
	public fixed float CascadeHardness[4];
	public Vector4 CascadeSphere0;
	public Vector4 CascadeSphere1;
	public Vector4 CascadeSphere2;
	public Vector4 CascadeSphere3;

	public fixed float ShadowBias[4];

	public Span<Matrix> WorldToShadowMatrices => MemoryMarshal.CreateSpan( ref WorldToShadowMatrices0, 4 );
	public Span<Vector4> CascadeSpheres => MemoryMarshal.CreateSpan( ref CascadeSphere0, 4 );
};

internal partial class ShadowMapper
{
	public static int DirectionalShadowMemorySize { get; set; }

	GPUDirectionalLight GPUDirectionalLightData = new();

	struct CascadeDebugInfo
	{
		public Texture DepthTexture;
		public float Near;
		public float Far;
		public float Width;
		public float Height;
	}

	static readonly CascadeDebugInfo[] CascadeDebugInfos = new CascadeDebugInfo[4];
	static int CascadeDebugCount;

	// Near Far frustum corners in clip space
	private static readonly Vector4[] Corners =
	[
		new( -1, -1, 1, 1 ), // tl
		new( -1,  1, 1, 1 ), // bl
		new(  1,  1, 1, 1 ), // br
		new(  1, -1, 1, 1 ), // tr
		new( -1, -1, 0, 1 ), // far tl
		new( -1,  1, 0, 1 ), // far bl
		new(  1,  1, 0, 1 ), // far br
		new(  1, -1, 0, 1 ), // far tr
	];

	private static readonly string[] CascadeNames = ["CSM Cascade 0", "CSM Cascade 1", "CSM Cascade 2", "CSM Cascade 3"];

	/// <summary>
	/// Calculates normalized [0,1] split distances for cascade shadow maps.
	/// Cascade 0 is fixed to firstCascadeSize world units from the near plane.
	/// Cascades 1+ use a logarithmic/uniform blend (PSSM) from firstCascadeSize to far.
	/// </summary>
	public static float[] CalculateSplitDistances( int numCascades, float near, float far, float lambda = 0.91f )
	{
		float[] splits = new float[numCascades];

		float subNear = 1.0f;
		float subRange = far - subNear;
		float subRatio = far / MathF.Max( subNear, 1.0f );

		for ( int i = 0; i < numCascades; i++ )
		{
			float p = (i + 1f) / numCascades;
			float logSplit = subNear * MathF.Pow( subRatio, p );
			float uniformSplit = subNear + subRange * p;
			float d = lambda * (logSplit - uniformSplit) + uniformSplit;
			splits[i] = Math.Clamp( d / far, 0.0f, 1.0f );
		}

		return splits;
	}

	struct Cascade
	{
		public Vector3 Origin;
		public Angles Angles;
		public float Near;
		public float Far;
		public float Width;
		public float Height;
		public Vector3 SphereCenter;
		public float SphereRadius;
	}

	/// <summary>
	/// FOV compensation for cascade shadow maps using sphere-frustum intersection.
	/// Places a unit sphere offset along the view axis based on FOV, then finds
	/// where the frustum diagonal ray intersects it. The intersection distance
	/// becomes the far plane scale factor, keeping cascade sizes stable across FOVs.
	/// 
	/// Reference: Valient, "The Rendering Technology of Killzone 2", GDC 2009
	/// https://www.guerrilla-games.com/media/News/Files/GDC09_Valient_Rendering_Technology_Of_Killzone_2_Extended_Presenter_Notes.pdf
	/// </summary>
	static float CalculateFarPlaneScale( float fov, float diagonalRatio )
	{
		// How much of the shadow range is reserved for fade-out at the far edge
		const float shadowFadeRange = 0.1f;

		// Sphere offset along the view axis, scaled by FOV. Narrow FOV pushes the
		// sphere further forward (larger offset), wide FOV keeps it near the camera.
		float maxOffset = (1.0f - shadowFadeRange) * 0.5f;
		float p = maxOffset * Math.Clamp( 1.0f - fov / 180.0f, 0.0f, 1.0f );
		float r = 1.0f - p;
		float c2 = diagonalRatio * diagonalRatio;

		// Solve ray-sphere intersection: ray from origin along frustum diagonal,
		// sphere centered at (p, 0, 0) with radius r.
		// (x - p)² + (sqrt(c² - 1) * x)² = r²  →  c²x² - 2px + p² - r² = 0
		return (MathF.Sqrt( -c2 * p * p + c2 * r * r + p * p ) + p) / c2;
	}

	/// <summary>
	/// Given a camera view frustum, returns a given number of cascade frustums.
	/// </summary>
	static IEnumerable<Cascade> GetCascades( CFrustum viewFrustum, Rotation rotation, int numCascades, float NearClip, float FarClip, float lambda, int shadowmapSize, Vector3 cameraPosition )
	{
		// Project frustum corners into world space from clip space
		var viewFrustumCorners = new Vector3[8];
		var invViewProj = viewFrustum.GetInvReverseZViewProjTranspose()._numerics;
		for ( int i = 0; i < 8; i++ )
		{
			var corner = System.Numerics.Vector4.Transform( Corners[i], invViewProj );
			viewFrustumCorners[i] = new Vector3( corner.X, corner.Y, corner.Z ) / corner.W;
		}

		// Compute camera forward from frustum geometry (near plane center → far plane center)
		Vector3 nearCenter = (viewFrustumCorners[0] + viewFrustumCorners[1] + viewFrustumCorners[2] + viewFrustumCorners[3]) * 0.25f;
		Vector3 farCenter = (viewFrustumCorners[4] + viewFrustumCorners[5] + viewFrustumCorners[6] + viewFrustumCorners[7]) * 0.25f;
		var fwd = farCenter - nearCenter;
		Vector3 camForward = fwd / fwd.Length;

		// View-space depth of the camera's actual near/far clip planes
		float cameraNearDepth = Vector3.Dot( camForward, nearCenter - cameraPosition );
		float cameraFarDepth = Vector3.Dot( camForward, farCenter - cameraPosition );
		float cameraDepthRange = cameraFarDepth - cameraNearDepth;

		// FOV compensation: scale the effective shadow far distance using sphere-frustum
		// intersection so cascade sizes remain stable across different camera FOVs.
		// See: Valient, "The Rendering Technology of Killzone 2", GDC 2009
		float diagonalRatio = (viewFrustumCorners[4] - cameraPosition).Length / cameraFarDepth;
		float fov = 2.0f * MathF.Atan2( (viewFrustumCorners[0] - viewFrustumCorners[1]).Length * 0.5f, cameraNearDepth ) * (180.0f / MathF.PI);
		float farPlaneScale = CalculateFarPlaneScale( fov, diagonalRatio );
		FarClip *= farPlaneScale;

		var splitDistances = CalculateSplitDistances( numCascades, NearClip, FarClip, lambda );

		// Remap frustum corners to the shadow coverage range [NearClip, FarClip] using
		// depth-proportional interpolation, matching Unreal's GetShadowSplitBoundsDepthRange.
		// This places corners on constant view-space depth planes so cascade boundaries
		// align with the actual frustum geometry at all FOV angles.
		float tNear = (NearClip - cameraNearDepth) / cameraDepthRange;
		float tFar = (FarClip - cameraNearDepth) / cameraDepthRange;

		for ( int i = 0; i < 4; i++ )
		{
			var origNear = viewFrustumCorners[i];
			var origFar = viewFrustumCorners[i + 4];
			viewFrustumCorners[i] = Vector3.Lerp( origNear, origFar, tNear );
			viewFrustumCorners[i + 4] = Vector3.Lerp( origNear, origFar, tFar );
		}

		// Ortho for each cascade
		for ( int cascade = 0; cascade < numCascades; cascade++ )
		{
			var splitNear = cascade == 0 ? 0 : splitDistances[cascade - 1];
			var splitFar = splitDistances[cascade];

			// Lerp our splits along the main view frustum corners
			Vector3[] splitFrustumCorners = new Vector3[8];
			for ( int k = 0; k < 4; k++ )
			{
				splitFrustumCorners[k] = Vector3.Lerp( viewFrustumCorners[k], viewFrustumCorners[k + 4], splitNear );
				splitFrustumCorners[k + 4] = Vector3.Lerp( viewFrustumCorners[k], viewFrustumCorners[k + 4], splitFar );
			}

			Vector3 splitFrustumCenter = Vector3.Zero;
			for ( int l = 0; l < 8; l++ )
				splitFrustumCenter += splitFrustumCorners[l];
			splitFrustumCenter /= 8;

			float frustumRadius = 0.0f;
			for ( int l = 0; l < 8; l++ )
				frustumRadius = Math.Max( frustumRadius, (splitFrustumCorners[l] - splitFrustumCenter).LengthSquared );
			frustumRadius = MathF.Ceiling( MathF.Sqrt( frustumRadius ) * 16.0f ) / 16.0f;

			// Pull the shadow camera back toward the light beyond the bounding sphere to catch
			// off-screen casters (tall buildings, trees behind the camera, etc.)
			const float casterExtension = 4096f;

			Vector3 lightForward = rotation.Forward;

			// Place camera at the light-facing edge of the bounding sphere, pulled back further for casters.
			// Near = 0 (at camera), Far = full sphere diameter + pullback.
			Vector3 cascadeOrigin = splitFrustumCenter - lightForward * (frustumRadius + casterExtension);

			// Snap to nearest texel to prevent view-dependent shadow shimmer
			cascadeOrigin = SnapToTexel( cascadeOrigin, rotation.Right, rotation.Up, lightForward, frustumRadius, shadowmapSize );


			Cascade cascadeData = new()
			{
				Origin = cascadeOrigin,
				Angles = rotation.Angles(),
				Near = 0f,
				Far = MathF.Max( frustumRadius * 2f + casterExtension, FarClip ),
				Width = frustumRadius * 2f,
				Height = frustumRadius * 2f,
				SphereCenter = splitFrustumCenter,
				SphereRadius = frustumRadius
			};

			yield return cascadeData;
		}
	}

	/// <summary>
	/// Snaps a position to the nearest shadowmap texel to prevent view-dependent aliasing.
	/// </summary>
	static Vector3 SnapToTexel( Vector3 position, Vector3 lightRight, Vector3 lightUp, Vector3 lightForward, float frustumRadius, int shadowmapSize )
	{
		// Calculate world units per texel (full ortho width = frustumRadius * 2)
		float worldUnitsPerTexel = (frustumRadius * 2.0f) / shadowmapSize;

		// Fully decompose position into light-space coordinates
		float x = Vector3.Dot( position, lightRight );
		float y = Vector3.Dot( position, lightUp );
		float z = Vector3.Dot( position, lightForward );

		// Snap X and Y to texel boundaries, leave Z unchanged
		float snappedX = MathF.Floor( x / worldUnitsPerTexel ) * worldUnitsPerTexel;
		float snappedY = MathF.Floor( y / worldUnitsPerTexel ) * worldUnitsPerTexel;

		// Fully reconstruct position from light-space coordinates
		// This avoids any accumulation errors from subtracting offsets
		return lightRight * snappedX + lightUp * snappedY + lightForward * z;
	}

	static Matrix GetScaleBiasMatrix( int textureSize, float bias )
	{
		return new(
			0.5f, 0.0f, 0.0f, 0.5f,
			0.0f, -0.5f, 0.0f, 0.5f,
			0.0f, 0.0f, 1.0f, bias,
			0.0f, 0.0f, 0.0f, 1.0f
		);
	}

	/// <summary>
	/// Find or create shadow maps for a directional light (CSM).
	/// Returns an index to the directional shadow buffer.
	/// </summary>
	internal unsafe void FindOrCreateDirectionalShadowMaps( SceneLight light, ISceneView view )
	{
		int numCascades = Math.Min( light.lightNative.GetShadowCascades(), MaxCascades );
		float farClip = CascadeDistance;
		int shadowmapSize = MaxCascadeResolution;
		float splitRatio = light.lightNative.GetShadowCascadeSplitRatio();

		// Baked lights exclude static objects from shadow maps, their static shadows come from lightmaps
		var excludeFlags = (light.lightNative.GetLightFlags() & 32) != 0 // LIGHTTYPE_FLAGS_BAKED
			? SceneObjectFlags.StaticObject
			: SceneObjectFlags.None;

		GPUDirectionalLight gpuShadowData = new();

		// A bit overreach for shadowmapper
		gpuShadowData.Color = new Vector4( light.LightColor, light.FogStrength );
		gpuShadowData.Direction = new Vector4( -light.WorldDirection, 0 );

		DirectionalShadowMemorySize = 0;

		// native stuff does this WorldDirection shit, we can just do light.Rotation if stuff is rotated properly
		var cascades = GetCascades( view.GetFrustum(), (-light.WorldDirection).EulerAngles.ToRotation(), numCascades, 1.0f, farClip, splitRatio, shadowmapSize, view.GetCameraPosition() ).ToArray();
		var frustum = CFrustum.Create();
		float baseHardness = 1.0f + light.ShadowHardness * 4.0f;
		float maxHardnessForFullTexel = ShadowFilter switch
		{
			<= 1 => 1.5f,
			2 => 3.0f,
			_ => 4.5f
		};

		for ( int i = 0; i < cascades.Length; i++ )
		{
			var cascade = cascades[i];
			var rt = RenderTarget.GetTemporary( shadowmapSize, shadowmapSize, ImageFormat.None, ImageFormat.D32 );

			DirectionalShadowMemorySize += (int)g_pRenderDevice.ComputeTextureMemorySize( rt.DepthTarget.native );

			// Create a native ortho frustum
			frustum.InitOrthoCamera( cascade.Origin, cascade.Angles, cascade.Near, cascade.Far, cascade.Width, cascade.Height );

			// Render shadow view
			CSceneSystem.AddShadowView( CascadeNames[i], view, frustum, new( 0, 0, shadowmapSize, shadowmapSize ), rt.DepthTarget.native, 0, SceneObjectFlags.None, excludeFlags, ShadowDepthBias, ShadowSlopeScale );

			// Set our gpu data
			Matrix texScaleBiasMat = GetScaleBiasMatrix( shadowmapSize, 0 );
			gpuShadowData.WorldToShadowMatrices[i] = frustum.GetReverseZViewProjTranspose() * texScaleBiasMat.Transpose();
			gpuShadowData.ShadowMapIndex[i] = rt.DepthTarget.Index;

			// Make cascades share same perceptual sharpness
			gpuShadowData.CascadeHardness[i] = baseHardness * (cascade.Width / cascades[0].Width);

			// Cascade bounding sphere for GPU selection (xyz = center, w = radiusSquared).
			// Shrink non-last cascades by a PCF margin so the selection boundary stays
			// inside the valid shadow map area. Without this, PCF near the sphere edge
			// averages in cleared depth texels (no geometry), causing shadows to fade out.
			float pcfMarginTexels = 4.0f;
			float pcfMarginFraction = pcfMarginTexels * 2.0f / shadowmapSize;
			float selectionRadius = (i < cascades.Length - 1)
				? cascade.SphereRadius * (1.0f - pcfMarginFraction)
				: cascade.SphereRadius;
			gpuShadowData.CascadeSpheres[i] = new Vector4( cascade.SphereCenter, selectionRadius * selectionRadius );

			// Per-cascade depth bias: scale by texel-to-depth ratio relative to cascade 0.
			// Width/Far captures world-space texel size normalized by the cascade's depth range,
			// so the bias in world units stays proportional to texel size across all cascades.
			float biasScale = (cascade.Width * cascades[0].Far) / (cascades[0].Width * cascade.Far);
			gpuShadowData.ShadowBias[i] = light.ShadowBias * biasScale;

			// Guarantee that cascades are softer for at least a full texel
			if ( i > 0 )
				gpuShadowData.CascadeHardness[i] = Math.Min( gpuShadowData.CascadeHardness[i], maxHardnessForFullTexel );

			// Store cascade debug info for HUD rendering
			CascadeDebugInfos[i] = new CascadeDebugInfo
			{
				DepthTexture = rt.DepthTarget,
				Near = cascade.Near,
				Far = cascade.Far,
				Width = cascade.Width,
				Height = cascade.Height
			};
		}

		CascadeDebugCount = cascades.Length;

		frustum.Delete();

		gpuShadowData.CascadeCount = (uint)numCascades;
		gpuShadowData.InverseShadowMapSize = 1.0f / shadowmapSize;
		GPUDirectionalLightData = gpuShadowData;
	}

	static TextRendering.Scope DebugText( string text )
	{
		var scope = new TextRendering.Scope( text, Color.Yellow, 12f, weight: 400 );
		scope.FontName = "Consolas";
		scope.Outline = new() { Color = Color.Black.WithAlpha( 0.7f ), Enabled = true, Size = 3f };
		return scope;
	}
}
