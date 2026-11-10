using Sandbox.Movement;
using System;
using System.IO;
using System.Text.Json;

namespace Sandbox.Test;

[TestClass]
public class SourceMovementTests
{
	[TestMethod]
	[DataRow( -500f )]
	[DataRow( 0f )]
	[DataRow( 100f )]
	public void ZeroGravityScalePreservesVelocity( float velocity )
	{
		Assert.AreEqual( velocity, MovementMath.StartGravity( velocity, 800, 0, 0.02f ) );
		Assert.AreEqual( velocity, MovementMath.FinishGravity( velocity, 800, 0, 0.02f ) );
	}

	[TestMethod]
	public void MatchesRecordedSdkResults()
	{
		using var document = JsonDocument.Parse( File.ReadAllText( Path.Combine( AppContext.BaseDirectory, "Data", "SourceMovement", "reference.json" ) ) );
		Assert.AreEqual( "be57e3d01ac7d7aa6a2cacbfef187c0b46616db0", document.RootElement.GetProperty( "revision" ).GetString() );
		var count = 0;
		foreach ( var test in document.RootElement.GetProperty( "cases" ).EnumerateArray() )
		{
			var operation = test.GetProperty( "operation" ).GetString();
			// Full-tick cases run in the integration tests.
			if ( operation.EndsWith( "_tick", StringComparison.Ordinal ) ) continue;
			float F( string name ) => test.GetProperty( name ).GetSingle();
			// Source records zero as default gravity; translate it to s&box's effective scale.
			// Actual zero-g behavior is covered separately, not by Source's sentinel value.
			float GravityScale()
			{
				var scale = F( "scale" );
				return scale == 0 ? 1 : scale;
			}
			Vector3 V( string name )
			{
				var value = test.GetProperty( name );
				return new Vector3( value[0].GetSingle(), value[1].GetSingle(), value[2].GetSingle() );
			}

			var velocity = V( "velocity" );
			var actual = operation switch
			{
				"friction" => MovementMath.Friction( velocity, F( "acceleration" ), F( "stop" ), F( "friction" ), F( "delta" ) ),
				"accelerate" => MovementMath.Accelerate( velocity, V( "direction" ), F( "speed" ), F( "acceleration" ), F( "friction" ), F( "delta" ) ),
				"air" => MovementMath.AirAccelerate( velocity, V( "direction" ), F( "speed" ), F( "acceleration" ), F( "cap" ), F( "friction" ), F( "delta" ) ),
				"start_gravity" => velocity.WithZ( MovementMath.StartGravity( velocity.z, F( "gravity" ), GravityScale(), F( "delta" ) ) ),
				"finish_gravity" => velocity.WithZ( MovementMath.FinishGravity( velocity.z, F( "gravity" ), GravityScale(), F( "delta" ) ) ),
				"jump" => velocity.WithZ( MovementMath.FinishGravity( MovementMath.Jump( velocity.z, MovementMath.JumpSpeed, F( "ducked" ) != 0 ), F( "gravity" ), GravityScale(), F( "delta" ) ) ),
				"check_velocity" => MovementMath.CheckVelocity( velocity, 3500 ),
				_ => throw new InvalidOperationException( operation )
			};
			var expected = V( "expected" );
			for ( int axis = 0; axis < 3; axis++ )
			{
				// A few float ULPs accommodate C++ scalar vs .NET vector arithmetic.
				var tolerance = MathF.Max( 0.00002f, MathF.Abs( expected[axis] ) * 0.000001f );
				Assert.AreEqual( expected[axis], actual[axis], tolerance, $"SDK case {count}: {operation}, axis {axis}: {test}" );
			}
			count++;
		}
		Assert.AreEqual( 1179, count, "The complete primitive reference corpus must be present" );
	}
}
