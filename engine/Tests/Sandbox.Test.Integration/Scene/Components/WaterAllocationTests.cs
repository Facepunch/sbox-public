using System;
using System.Reflection;

namespace SceneTests.Components;

/// <summary>
/// Water's steady update paths must not create garbage each frame or physics step.
/// </summary>
[TestClass]
public class WaterAllocationTests
{
	/// <summary>
	/// Adjacent waters touching different colliders of one body must resolve ownership without boxing contacts.
	/// </summary>
	[TestMethod, TestCategory( Allocations.Category )]
	public void CompoundBodyContactQueriesDoNotAllocate()
	{
		if ( !Graphics.IsAvailable )
			Assert.Inconclusive( "Requires SBOX_TEST_GRAPHICS=1." );

		var scene = new Scene();
		using var scope = scene.Push();
		try
		{
			var left = scene.CreateObject().Components.Create<WaterBody>();
			left.Size = new Vector2( 64, 128 );
			left.WorldPosition = new Vector3( -32, 0, 0 );
			left.Effects = false;
			var right = scene.CreateObject().Components.Create<WaterBody>();
			right.Size = left.Size;
			right.WorldPosition = new Vector3( 32, 0, 0 );
			right.Effects = false;

			var obj = scene.CreateObject();
			obj.WorldPosition = Vector3.Down * 100;
			var body = obj.Components.Create<Rigidbody>();
			body.MassOverride = 10;
			var first = new GameObject( obj ).Components.Create<BoxCollider>();
			first.LocalPosition = new Vector3( -32, 0, 0 );
			first.Scale = new Vector3( 16 );
			var second = new GameObject( obj ).Components.Create<BoxCollider>();
			second.LocalPosition = new Vector3( 32, 0, 0 );
			second.Scale = new Vector3( 16 );
			for ( int i = 0; i < 10; i++ )
				scene.GameTick( 1.0 / 50 );

			var volume = left.GetComponentsInChildren<BoxCollider>().Single();
			Assert.IsTrue( volume.Touching.Contains( first ) );
			Assert.IsFalse( volume.Touching.Contains( second ) );
			var touches = typeof( WaterBody ).GetMethod( "TouchesBody", BindingFlags.Instance | BindingFlags.NonPublic )
				.CreateDelegate<Func<Rigidbody, Collider, bool>>( left );
			Assert.IsTrue( touches( body, second ) );
			Allocations.AssertSettles( "Compound body contact query", () => touches( body, second ) );
		}
		finally
		{
			scene.Destroy();
		}
	}

	/// <summary>
	/// Recording the refraction capture each frame keeps its setup in the command list instead of boxing it.
	/// </summary>
	[TestMethod, TestCategory( Allocations.Category )]
	public void RefractionCaptureRecordsWithoutAllocating()
	{
		if ( !Graphics.IsAvailable )
			Assert.Inconclusive( "Requires SBOX_TEST_GRAPHICS=1." );

		var scene = new Scene();
		using var scope = scene.Push();
		try
		{
			var camera = scene.CreateObject().Components.Create<CameraComponent>();
			var commands = new Sandbox.Rendering.CommandList( "Water captures" );
			var exclusions = new TagSet( ["water"] );
			Allocations.AssertSettles( "Refraction capture recording", () =>
			{
				var target = commands.GetRenderTarget( "Water refraction", 2, ImageFormat.RGBA16161616F, ImageFormat.None );
				commands.DrawRefraction( camera, new Plane( Vector3.Zero, Vector3.Down ), target, new Sandbox.Rendering.RefractionSetup
				{
					RenderBehind = true,
					FallbackColor = Color.Transparent,
					ViewSetup = new Sandbox.Rendering.ViewSetup { EnablePostprocessing = false, ClearColor = Color.Blue, ExcludeTags = exclusions },
				} );
			}, beforePass: commands.Reset );
		}
		finally
		{
			scene.Destroy();
		}
	}
}
