using Borealis;
using Borealis.Features;
using Borealis.Gpu;
using System;

namespace BorealisTests;

/// <summary>
/// Environment map probes and fog: packed the way native's light binner and rendering pipeline pack them.
/// </summary>
// Frames go through Borealis's ShadowMapper, whose cache and frame counter are static: one test's frame
// can't run in the middle of another's
[TestClass]
[DoNotParallelize]
public class EnvironmentTests
{
	[TestMethod]
	public void EnvMapLayout()
	{
		LightBinnerFeature.GpuEnvMap.ValidateLayout();
	}

	static Vector3 ToLocal( in LightBinnerFeature.GpuEnvMap environmentMap, Vector3 point )
	{
		var position = new Vector4( point, 1 );
		return new( Vector4.Dot( environmentMap.Row0, position ), Vector4.Dot( environmentMap.Row1, position ), Vector4.Dot( environmentMap.Row2, position ) );
	}

	/// <summary>
	/// World to probe space undoes the probe's rotation and position, and multiplies by its scale rather than dividing it out,
	/// as native's <c>InverseTR</c> of the probe's matrix does - it transposes it as a rotation. Priority is offset and flipped so
	/// higher sorts first.
	/// </summary>
	[TestMethod]
	public void EnvMapPacking()
	{
		var bounds = new BBox( new Vector3( -10, -20, -30 ), new Vector3( 10, 20, 30 ) );
		var probe = new EnvMapObject( null, bounds, new Transform( new Vector3( 100, 0, 0 ), Rotation.FromYaw( 90 ), 5 ) )
		{
			Tint = new Color( 0.5f, 0.25f, 1 ),
			Feathering = 3,
			Priority = 5,
		};

		var packed = LightBinnerFeature.Pack( probe );

		// Yawed 90 degrees, the probe's forward is world +y, and its scale of 5 divides (native's MatrixInvert: the inverse
		// for a uniform scale)
		var local = ToLocal( packed, new Vector3( 100, 50, 0 ) );
		Assert.AreEqual( 10, local.x, 1e-3, "along its forward" );
		Assert.AreEqual( 0, local.y, 1e-3 );
		Assert.AreEqual( 0, local.z, 1e-3 );
		Assert.AreEqual( 0, ToLocal( packed, new Vector3( 100, 0, 0 ) ).Length, 1e-3, "its position is its origin" );
		Assert.AreEqual( 2, ToLocal( packed, new Vector3( 100, 10, 0 ) ).Length, 1e-3, "and its scale divides" );

		// A map's probes have a scale of -1, which turns their local space inside out, as native's does
		var mapProbe = new EnvMapObject( null, bounds, new Transform( new Vector3( 100, 0, 0 ), Rotation.Identity, -1 ) );
		var inverted = ToLocal( LightBinnerFeature.Pack( mapProbe ), new Vector3( 110, 20, -30 ) );
		Assert.AreEqual( new Vector3( -10, -20, 30 ), inverted );

		Assert.AreEqual( new Vector4( -10, -20, -30, 0 ), packed.BoxMins );
		Assert.AreEqual( new Vector4( 10, 20, 30, 0 ), packed.BoxMaxs );
		Assert.AreEqual( new Vector4( 0.5f, 0.25f, 1, 3 ), packed.Color, "tint, and feathering in w" );
		Assert.AreEqual( 19995u, packed.Priority );
	}

	/// <summary>
	/// Probes blend in native's order: highest priority first, then smallest. Lights sharing the binner
	/// aren't disturbed.
	/// </summary>
	[TestMethod]
	public void EnvMapOrder()
	{
		var world = new RenderWorld();
		var big = new EnvMapObject( null, new BBox( new Vector3( -500 ), new Vector3( 500 ) ), new Transform( new Vector3( 100, 0, 0 ) ) );
		var small = new EnvMapObject( null, new BBox( new Vector3( -50 ), new Vector3( 50 ) ), new Transform( new Vector3( 100, 0, 0 ) ) );
		var important = new EnvMapObject( null, new BBox( new Vector3( -500 ), new Vector3( 500 ) ), new Transform( new Vector3( 100, 0, 0 ) ) ) { Priority = 1 };
		world.Add( big );
		world.Add( new LightObject( LightObject.LightKind.Point, new Transform( new Vector3( 100, 0, 0 ) ) ) { Radius = 50, CastShadows = false } );
		world.Add( small );
		world.Add( important );

		var view = new RenderView { FieldOfView = 90, ZNear = 1, ZFar = 1000, Viewport = new Rect( 0, 0, 1000, 1000 ) };
		view.Update();

		using var system = new RenderSystem();
		var stats = new RenderStats();
		system.Collect( world, view, ref stats );
		system.Prepare( world, view );

		var binner = system.GetFeature<LightBinnerFeature>();
		CollectionAssert.AreEqual( new[] { important, small, big }, binner.PackedEnvMaps.ToArray() );
		Assert.AreEqual( 1, binner.Count, "the light is still binned" );
	}

	/// <summary>
	/// Native's <c>SetupGradientFog</c>: a normalised distance and height ramp, culled before the start
	/// distance and above the end height. Off, its culling params reject every pixel.
	/// </summary>
	[TestMethod]
	public void GradientFogConstants()
	{
		var vr = new ViewEnvironment.ViewConstantsVRData();
		ViewEnvironment.SetupGradientFog( ref vr, new GradientFogSettings
		{
			Enabled = true,
			StartDistance = 100,
			EndDistance = 1100,
			StartHeight = 0,
			EndHeight = 200,
			MaximumOpacity = 0.75f,
			DistanceFalloffExponent = 2,
			VerticalFalloffExponent = 3,
			Color = new Color( 1, 0.5f, 0.25f, 0.5f ),
		} );

		Assert.AreEqual( 1, vr.GradientFogEnabled );
		Assert.AreEqual( new Vector4( -0.1f, 1, 0.001f, -0.005f ), vr.GradientFogBiasAndScale );
		Assert.AreEqual( new Vector4( 2, 3, 0, 0 ), vr.GradientFogExponent );
		Assert.AreEqual( new Vector4( 10000, 200, 0, 0 ), vr.GradientFogCullingParams );
		Assert.AreEqual( new Vector4( 0.5f, 0.25f, 0.125f, 0.75f ), vr.GradientFogColorOpacity, "color scaled by its alpha, opacity in w" );

		vr = new ViewEnvironment.ViewConstantsVRData();
		ViewEnvironment.SetupGradientFog( ref vr, default );
		Assert.AreEqual( 0, vr.GradientFogEnabled );
		Assert.AreEqual( new Vector4( float.PositiveInfinity, float.NegativeInfinity, 0, 0 ), vr.GradientFogCullingParams );
	}

	/// <summary>
	/// The host's volumetric fog setting (<c>r_enable_volume_fog</c>): off, a scene with fog volumes runs none of the fog's
	/// passes, and turned back on, the same render system runs them again.
	/// </summary>
	[TestMethod]
	public void VolumetricFogSettingRemovesItsPasses()
	{
		var world = new RenderWorld();
		world.FogVolumes.Add( new FogVolume { Bounds = new BBox( new Vector3( -4000 ), new Vector3( 4000 ) ), Strength = 0.15f, Exponent = 1 } );
		world.Lighting.VolumetricFog.Enabled = true;
		world.Lighting.VolumetricFog.Scattering = 1;
		using var target = ViewTarget.Create( new Vector2Int( 64, 64 ) );
		using var system = new RenderSystem();

		int FogDispatches()
		{
			using var frame = HeadlessRenderHost.Headless.Capture();
			RenderHost.Current.NextFrame();
			system.Render( world, TestScenes.LookingDownX(), target );
			return frame.Commands.Count( c => c.Op == Headless.GpuOp.Dispatch && c.In( "VolumetricFog" ) );
		}

		try
		{
			Assert.IsTrue( FogDispatches() > 0, "on, the fog's passes run" );
			Assert.IsTrue( system.Fog.Active );

			HeadlessRenderHost.VolumetricFogOn = false;
			Assert.AreEqual( 0, FogDispatches(), "off, none of them run" );
			Assert.IsFalse( system.Fog.Active, "and views bind no fog volume" );
			Assert.AreSame( VolumetricFog.OpaqueBlack, system.Fog.FogVolumeTexture );

			HeadlessRenderHost.VolumetricFogOn = true;
			Assert.IsTrue( FogDispatches() > 0, "back on, they run again" );
		}
		finally
		{
			HeadlessRenderHost.VolumetricFogOn = true;
		}
	}

	/// <summary>
	/// The fog's froxels follow the host's (the fog quality's <c>volume_fog_*</c>): native's defaults, then High's, then Low's
	/// with its width rounded down to whole thread groups, each made at that size (a frame to start scattering again, as
	/// when the fog's volumes are first made) and running the fog.
	/// </summary>
	[TestMethod]
	public void VolumetricFogTakesTheHostsFroxels()
	{
		var world = new RenderWorld();
		world.FogVolumes.Add( new FogVolume { Bounds = new BBox( new Vector3( -4000 ), new Vector3( 4000 ) ), Strength = 0.15f, Exponent = 1 } );
		world.Lighting.VolumetricFog.Enabled = true;
		world.Lighting.VolumetricFog.Scattering = 1;
		using var target = ViewTarget.Create( new Vector2Int( 64, 64 ) );
		using var system = new RenderSystem();

		int FogDispatches()
		{
			using var frame = HeadlessRenderHost.Headless.Capture();
			RenderHost.Current.NextFrame();
			system.Render( world, TestScenes.LookingDownX(), target );
			return frame.Commands.Count( c => c.Op == Headless.GpuOp.Dispatch && c.In( "VolumetricFog" ) );
		}

		try
		{
			FogDispatches();
			Assert.IsTrue( FogDispatches() > 0 );
			Assert.AreEqual( new Vector3Int( 240, 160, 64 ), system.Fog.Size, "native's defaults" );
			Assert.AreEqual( new Vector2Int( 240, 160 ), system.Fog.FogVolumeTexture.Size );

			HeadlessRenderHost.FogSize = new Vector3Int( 192, 128, 64 );
			FogDispatches();
			Assert.IsTrue( FogDispatches() > 0 );
			Assert.AreEqual( new Vector3Int( 192, 128, 64 ), system.Fog.Size, "High's" );
			Assert.AreEqual( new Vector2Int( 192, 128 ), system.Fog.FogVolumeTexture.Size, "its volumes made again at that size" );

			HeadlessRenderHost.FogSize = new Vector3Int( 60, 40, 32 );
			FogDispatches();
			Assert.IsTrue( FogDispatches() > 0 );
			Assert.AreEqual( new Vector3Int( 56, 40, 32 ), system.Fog.Size, "Low's, its width a whole number of 8 wide groups" );
			Assert.AreEqual( new Vector2Int( 56, 40 ), system.Fog.FogVolumeTexture.Size );
		}
		finally
		{
			HeadlessRenderHost.FogSize = VolumetricFog.DefaultSize;
		}
	}
}
