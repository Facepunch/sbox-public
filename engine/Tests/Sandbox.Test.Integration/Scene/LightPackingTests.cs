using System;

namespace SceneTests;

/// <summary>
/// Lights, cubemaps and probe volumes keep their description and state in managed code, start with what native's
/// <c>CSceneSystem::CreateLight</c> and friends gave them, follow every change the components and maps make, and pack for the
/// light binner (<c>LightPacking</c>, <c>LightBinnerFeature.Pack</c>) as native packed them (<c>RenderTools::PackSceneLight</c>,
/// <c>PackSceneEnvMap</c>): checked against native's values (<see cref="NativeGolden"/>).
/// </summary>
[TestClass]
public class LightPackingTests
{
	static void Same( SceneLight light, string what ) => NativeGolden.Check( $"light/{what}", NativeGolden.Light( light ) );

	static void Same( SceneCubemap cubemap, string what, bool priority = true )
	{
		NativeGolden.Check( $"cubemap/{what}", NativeGolden.Cubemap( cubemap, priority ) );
		if ( priority ) NativeGolden.Check( $"cubemap/{what}/packed", NativeGolden.EnvMapRecord( cubemap ), 1e-3f );
	}

	static void Same( SceneLightProbe probe, string what ) => NativeGolden.Check( $"probe/{what}", NativeGolden.Probe( probe ) );

	[TestMethod]
	public void LightsStartChangeAndPackAsNatives()
	{
		var world = new SceneWorld();
		try
		{
			// Point lights, as a component and a map make them, then every setting the components and maps change
			var point = new ScenePointLight( world, new Vector3( 10, 20, 30 ), 200, Color.Red * 5 );
			Same( point, "new point light" );
			point.Transform = new Transform( new Vector3( -50, 5, 70 ), Rotation.From( 10, 40, 5 ) );
			point.LinearAttenuation = 0.5f;
			point.ConstantAttenuation = 0.25f;
			point.QuadraticAttenuation = 2;
			point.FogLighting = SceneLight.FogLightingMode.Dynamic;
			point.FogStrength = 0.5f;
			point.Shape = SceneLight.LightShape.Capsule;
			point.ShapeSize = new Vector2( 10, 20 );
			point.RenderSpecular = false;
			point.RenderTransmissive = false;
			point.ShadowsEnabled = false;
			point.ShadowTextureResolution = 512;
			point.Radius = 350;
			point.LightColor = new Color( 1, 2, 3 );
			Same( point, "changed point light" );
			point.ApplyLegacy( new Light.LegacyLightData
			{
				DirectLight = 3,
				BakeLightIndex = 7,
				BakeLightIndexScale = 2,
				BakedLightIndexing = true,
				FogContributionStrength = 0.25f,
				RenderDiffuse = true,
				RenderSpecular = true,
				FogLighting = 2,
				ConstantAttenuation = 1,
				LinearAttenuation = 0.1f,
				FallOff = 3,
			} );
			Same( point, "a map's stationary point light" );
			point.SetModulatorRange( 4, 2 );
			Same( point, "modulated point light" );

			var capsule = new ScenePointLight( world, new Vector3( 10, 20, 30 ), 200, Color.Red * 5 );
			capsule.Transform = new Transform( new Vector3( -50, 5, 70 ), Rotation.From( 10, 40, 5 ) );
			capsule.LinearAttenuation = 0.5f;
			capsule.ConstantAttenuation = 0;
			capsule.FogLighting = SceneLight.FogLightingMode.Dynamic;
			capsule.FogStrength = 0.5f;
			capsule.Shape = SceneLight.LightShape.Capsule;
			capsule.ShapeSize = new Vector2( 10, 20 );
			capsule.RenderSpecular = false;
			Same( capsule, "capsule point light without constant attenuation" );

#pragma warning disable CS0618 // the obsolete SceneLight constructor still makes a point light
			Same( new SceneLight( world ), "new obsolete scene light" );
#pragma warning restore CS0618

			var spot = new SceneSpotLight( world, new Vector3( 1, 2, 3 ), Color.White * 10 );
			Same( spot, "new spot light" );
			spot.Transform = new Transform( new Vector3( 100, 0, 50 ), Rotation.From( 45, 90, 0 ) );
			spot.ConeInner = 20;
			spot.ConeOuter = 40;
			spot.FallOff = 2;
			spot.Radius = 500;
			spot.SetBakedLightIndex( 3, true );
			spot.FogLighting = SceneLight.FogLightingMode.DynamicNoShadows;
			spot.Shape = SceneLight.LightShape.Rectangle;
			spot.ShapeSize = new Vector2( 4, 8 );
			Same( spot, "changed spot light" );
			spot.Transform = new Transform( new Vector3( 0, 0, 200 ), Rotation.From( 90, 0, 0 ) );
			spot.Shape = SceneLight.LightShape.Sphere;
			spot.ShapeSize = new Vector2( 3, 0 );
			Same( spot, "spot light pointing straight down" );

			var sun = new SceneDirectionalLight( world, Rotation.From( 60, 30, 0 ), Color.White * 3 );
			Same( sun, "new directional light" );
			sun.Rotation = Rotation.From( 50, 120, 0 );
			sun.SetBakedLightIndex( 1 );
			sun.ShadowCascadeCount = 2;
			sun.ShadowCascadeSplitRatio = 0.8f;
			sun.SetCascadeDistanceScale( 2 );
			sun.SetWorldDirection( Rotation.From( 40, 10, 0 ) );
			Same( sun, "changed directional light" );

			point.RenderingEnabled = false;
			Same( point, "hidden point light" );
		}
		finally
		{
			world.Delete();
		}
	}

	[TestMethod]
	public void CubemapsAndProbeVolumesStartAndChangeAsNatives()
	{
		var world = new SceneWorld();
		try
		{
			// As the envmap component and a sky make them
			var box = new BBox( new Vector3( -100, -200, -50 ), new Vector3( 300, 100, 150 ) );
			var placed = new Transform( new Vector3( 40, 50, 60 ), Rotation.From( 0, 30, 0 ) );
			var cube = new SceneCubemap( world, null, box, placed, Color.White, 0.25f, 1 );
			Same( cube, "new cubemap", priority: false );
			cube.Priority = 3;
			cube.Projection = SceneCubemap.ProjectionMode.Sphere;
			cube.TintColor = new Color( 0.5f, 1, 2 );
			cube.Feathering = 0.1f;
			cube.ProjectionBounds = new BBox( new Vector3( -10 ), new Vector3( 20 ) );
			cube.Transform = new Transform( new Vector3( -5, 5, 10 ), Rotation.From( 0, -45, 0 ) );
			Same( cube, "changed cubemap" );

			// Scaled up, and mirrored as map probes can be: native's inverse divides by the squared scale unless it's 1
			cube.Transform = new Transform( new Vector3( -5, 5, 10 ), Rotation.From( 0, -45, 0 ), 2 );
			Same( cube, "scaled cubemap" );
			// (not on a right angle: a bound landing exactly on a whole unit floors either way with the basis's float noise)
			cube.Transform = new Transform( new Vector3( 7.3f, 0.4f, -3.2f ), Rotation.From( 0, 70, 0 ), -1 );
			Same( cube, "mirrored cubemap" );

			var sky = new SceneCubemap( world );
			sky.Priority = 0;
			Same( sky, "default cubemap" );

			var probeBox = new BBox( new Vector3( -64 ), new Vector3( 128, 64, 32 ) );
			var probe = new SceneLightProbe( world, null, null, null, probeBox, placed, 5, 2 );
			Same( probe, "new probe volume" );
			probe.Transform = new Transform( new Vector3( 1, 2, 3 ) );
			probe.RenderingEnabled = false;
			Same( probe, "moved probe volume" );
		}
		finally
		{
			world.Delete();
		}
	}

	/// <summary>Changes reach the packed light at once.</summary>
	[TestMethod]
	public void ChangesReachThePack()
	{
		var world = new SceneWorld();
		try
		{
			var light = new ScenePointLight( world, Vector3.Zero, 100, Color.White );
			light.LightColor = Color.Green;
			light.FogStrength = 0.75f;
			light.Radius = 250;

			Sandbox.Rendering.LightPacking.Pack( light, out var packed, out _, out _ );
			Assert.AreEqual( (Vector3)Color.Green, packed.Color );
			Assert.AreEqual( 250.0f, packed.Radius );
			Assert.AreEqual( Color.Green, light.LightColor );
			Assert.AreEqual( 0.75f, light.FogStrength );
			Assert.IsTrue( light.native.IsNull, "no native light" );
		}
		finally
		{
			world.Delete();
		}
	}

	[TestMethod]
	public void LightsLeaveTheirWorldWhenDeleted()
	{
		var world = new SceneWorld();
		try
		{
			var light = new ScenePointLight( world );
			var cube = new SceneCubemap( world );
			lock ( world.InternalSceneObjects ) Assert.IsTrue( world.InternalSceneLights.Contains( light ) && world.InternalSceneLights.Contains( cube ) );

			light.Delete();
			cube.Delete();
			Assert.IsFalse( light.IsValid() );
			world.DeletePendingObjects();
			lock ( world.InternalSceneObjects )
			{
				Assert.IsFalse( world.InternalSceneLights.Contains( light ), "light gone from the world's lights" );
				Assert.IsFalse( world.InternalSceneLights.Contains( cube ), "cubemap gone from the world's lights" );
				Assert.IsFalse( world.InternalSceneObjects.Contains( light ) );
			}
		}
		finally
		{
			world.Delete();
		}
	}
}
