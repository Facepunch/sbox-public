using Sandbox.SceneRenderer;
using Sandbox.SceneRenderer.Features;
using System;

namespace SceneRendererTests;

/// <summary>
/// Lights packed the way native's <c>CLightBinnerStandard</c> packs them, and spot light bounds.
/// </summary>
[TestClass]
public class LightTests
{
	[TestMethod]
	public void GpuLightMatchesNativeLayout()
	{
		LightBinnerFeature.GpuLight.ValidateLayout();
	}

	/// <summary>
	/// The shader's falloff: saturate( 1 / ( linear * d + quadratic * d^2 ) - bias ).
	/// </summary>
	static float Falloff( in LightBinnerFeature.GpuLight light, float distance )
	{
		var d2 = MathF.Max( distance * distance, 1 );
		var dot = light.LinearFalloff * MathF.Sqrt( d2 ) + light.QuadraticFalloff * d2;
		return Math.Clamp( 1.0f / dot - light.FalloffBias, 0, 1 );
	}

	[TestMethod]
	public void PointLightPacking()
	{
		var light = new LightObject( LightObject.LightKind.Point, new Transform( new Vector3( 10, 20, 30 ), Rotation.FromYaw( 90 ) ) )
		{
			Radius = 200,
			Color = new Color( 2, 3, 4 ),
		};

		var packed = LightBinnerFeature.Pack( light );

		Assert.AreEqual( 1u, packed.Type );
		Assert.AreEqual( 200, packed.Radius );
		Assert.AreEqual( 2, packed.Color.x );

		// Position sits in the last column, which the shader reads as LightToWorld[3]
		Assert.AreEqual( 10, packed.Row0.w );
		Assert.AreEqual( 20, packed.Row1.w );
		Assert.AreEqual( 30, packed.Row2.w );

		// No direction - a sphere point light's rotation would vignette it
		Assert.AreEqual( 0, packed.Row0.x );
		Assert.AreEqual( 0, packed.Row1.x );
		Assert.AreEqual( 0, packed.Row2.x );

		// No cone: every direction passes
		Assert.AreEqual( -1, packed.SpotLightInnerOuterConeCosines.y );

		// Reaches zero exactly at the radius, and is bright close up
		Assert.AreEqual( 0, Falloff( packed, 200 ), 1e-4f );
		Assert.IsTrue( Falloff( packed, 100 ) > 0 );
		Assert.AreEqual( 1, Falloff( packed, 10 ), 1e-4f );
	}

	[TestMethod]
	public void SpotLightPacking()
	{
		var light = new LightObject( LightObject.LightKind.Spot, new Transform( Vector3.Zero, Rotation.LookAt( Vector3.Down ) ) )
		{
			Radius = 500,
			ConeInner = 20,
			ConeOuter = 30,
		};

		var packed = LightBinnerFeature.Pack( light );

		Assert.AreEqual( 3u, packed.Type );

		// Forward is the first column - the shader's LightToWorld[0], which the cone test reads
		Assert.AreEqual( -1, packed.Row2.x, 1e-5f );

		var cones = packed.SpotLightInnerOuterConeCosines;
		Assert.AreEqual( MathF.Cos( 20 * MathF.PI / 180 ), cones.x, 1e-5f );
		Assert.AreEqual( MathF.Cos( 30 * MathF.PI / 180 ), cones.y, 1e-5f );
		Assert.AreEqual( 1 / (cones.x - cones.y), cones.z, 1e-3f );
		Assert.AreEqual( MathF.Tan( 30 * MathF.PI / 180 ), cones.w, 1e-5f );
	}

	[TestMethod]
	public void SpotLightBoundsCoverTheCone()
	{
		var world = new RenderWorld();
		var light = new LightObject( LightObject.LightKind.Spot, new Transform( Vector3.Zero, Rotation.LookAt( Vector3.Down ) ) )
		{
			Radius = 100,
			ConeOuter = 30,
		};
		world.Add( light );

		var center = world.BoundsCenter[light.Index];
		var extents = world.BoundsExtents[light.Index];

		// Points straight down: reaches 100 below, and sin(30) * 100 = 50 to the sides
		Assert.AreEqual( -50, center.z, 1e-3f );
		Assert.AreEqual( 50, extents.z, 1e-3f );
		Assert.AreEqual( 50, extents.x, 1e-3f );
		Assert.AreEqual( 50, extents.y, 1e-3f );

		// A wide cone falls back to the whole sphere
		light.ConeOuter = 60;
		Assert.AreEqual( 100, world.BoundsExtents[light.Index].x, 1e-3f );
		Assert.AreEqual( 0, world.BoundsCenter[light.Index].z, 1e-3f );
	}
}
