using Sandbox.Movement;
using System;
using System.IO;
using System.Text.Json;

namespace SceneTests.Components;

public partial class PlayerControllerTest
{
	[TestMethod]
	public void PhysicalTicksMatchRecordedFullWalkMove()
	{
		using var document = JsonDocument.Parse( File.ReadAllText( Path.Combine( AppContext.BaseDirectory, "Data", "SourceMovement", "reference.json" ) ) );
		Assert.AreEqual( "be57e3d01ac7d7aa6a2cacbfef187c0b46616db0", document.RootElement.GetProperty( "revision" ).GetString() );
		int count = 0;
		foreach ( var test in document.RootElement.GetProperty( "cases" ).EnumerateArray() )
		{
			var operation = test.GetProperty( "operation" ).GetString();
			if ( !operation.EndsWith( "_tick", StringComparison.Ordinal ) ) continue;
			Vector3 V( string name )
			{
				var value = test.GetProperty( name );
				return new Vector3( value[0].GetSingle(), value[1].GetSingle(), value[2].GetSingle() );
			}
			var scene = new Scene();
			using var sceneScope = scene.Push();
			scene.PhysicsWorld.Gravity = Vector3.Down * 800;
			var airborne = operation == "air_tick";
			if ( !airborne ) CreateFloor( scene );
			var pc = CreatePlayer( scene, Vector3.Up * (airborne ? 1000 : 1) );
			if ( !airborne )
			{
				TickUntilGrounded( scene, pc );
				Assert.IsTrue( pc.IsOnGround );
				pc.GroundFriction = 0.8f;
			}
			var start = pc.Body.PhysicsBody.Position;
			var delta = test.GetProperty( "delta" ).GetSingle();
			using var timeScope = Time.Scope( 1, delta );
			pc.Body.Velocity = V( "velocity" );
			pc.WishVelocity = V( "direction" ) * test.GetProperty( "speed" ).GetSingle();
			if ( operation == "jump_tick" ) ((MoveModeWalk)pc.Mode).RequestJump();
			SimulateControllerPhysics( scene, pc, delta );
			var displacement = pc.Body.PhysicsBody.Position - start;
			Assert.IsTrue( (pc.Body.Velocity - V( "expected" )).Length < 0.005f, $"case {count}: velocity {pc.Body.Velocity}, SDK {V( "expected" )}: {test}" );
			Assert.IsTrue( (displacement - V( "displacement" )).Length < 0.005f, $"case {count}: displacement {displacement}, SDK {V( "displacement" )}: {test}" );
			count++;
		}
		Assert.AreEqual( 108, count );
	}
}
