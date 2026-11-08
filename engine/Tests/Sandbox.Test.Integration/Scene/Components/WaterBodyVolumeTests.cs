using Sandbox.Movement;

namespace SceneTests.Components;

/// <summary>
/// Verifies water volume queries, native contacts and the existing swimming integration.
/// </summary>
[TestClass]
public class WaterBodyVolumeTests
{
	/// <summary>
	/// Containment follows the authored footprint, rotation and depth without moving the surface.
	/// Mirrored scale still puts the fluid below the surface normal.
	/// </summary>
	[TestMethod]
	public void ContainmentUsesSurfaceCoordinatesAndDepth()
	{
		if ( !Graphics.IsAvailable )
			Assert.Inconclusive( "Requires SBOX_TEST_GRAPHICS=1." );

		var scene = Scene.CreateEditorScene();
		using var scope = scene.Push();
		try
		{
			var water = scene.CreateObject().Components.Create<WaterBody>();
			water.Size = new Vector2( 100, 50 );
			water.Depth = 20;
			Assert.IsTrue( water.Contains( Vector3.Zero ) );
			Assert.IsTrue( water.Contains( new Vector3( 50, 25, -20 ) ) );
			Assert.IsFalse( water.Contains( new Vector3( 51, 0, -10 ) ) );
			Assert.IsFalse( water.Contains( Vector3.Down * 21 ) );
			Assert.IsFalse( water.Contains( Vector3.Up ) );
			water.Enabled = false;
			Assert.IsFalse( water.Contains( Vector3.Down * 10 ) );
			water.Enabled = true;

			water.WorldTransform = new Transform( new Vector3( 30, 40, 50 ), Rotation.From( 20, 30, 10 ), new Vector3( -2, 3, -4 ) );
			var local = new Vector3( 80, 60, -40 );
			var point = water.FromSurface( local );
			Assert.IsTrue( water.ToSurface( point ).AlmostEqual( local, 0.001f ) );
			Assert.IsTrue( water.Contains( point ) );
			Assert.IsFalse( water.Contains( water.FromSurface( new Vector3( 101, 0, -40 ) ) ) );
			Assert.IsFalse( water.Contains( water.FromSurface( new Vector3( 0, 0, -81 ) ) ) );
			water.Ocean = true;
			Assert.IsTrue( water.Contains( water.FromSurface( new Vector3( 100000, 0, -40 ) ) ) );
			water.Enabled = false;
			Assert.IsFalse( water.Contains( point ) );
		}
		finally
		{
			scene.Destroy();
		}
	}

	/// <summary>
	/// Existing player swimming detects the generated trigger.
	/// </summary>
	[TestMethod]
	public void GeneratedTriggerEnablesPlayerSwimming()
	{
		if ( !Graphics.IsAvailable )
			Assert.Inconclusive( "Requires SBOX_TEST_GRAPHICS=1." );

		var scene = new Scene();
		using var scope = scene.Push();
		try
		{
			var water = scene.CreateObject().Components.Create<WaterBody>();
			water.Effects = false;
			var playerObject = scene.CreateObject();
			playerObject.WorldPosition = Vector3.Down * 64;
			var player = playerObject.Components.Create<PlayerController>();
			player.UseInputControls = false;
			player.EnableFootstepSounds = false;
			var swim = playerObject.Components.Create<MoveModeSwim>();
			for ( int i = 0; i < 10; i++ )
				scene.GameTick( 1.0 / 50 );
			Assert.IsTrue( player.Body.Touching.Any( x => x.Tags.Contains( "water" ) ) );
			Assert.IsTrue( swim.WaterLevel > swim.SwimLevel );
			Assert.IsTrue( player.IsSwimming );
		}
		finally
		{
			scene.Destroy();
		}
	}
}
