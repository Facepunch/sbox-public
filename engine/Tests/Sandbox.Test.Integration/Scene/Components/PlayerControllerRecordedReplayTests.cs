using Sandbox.Movement;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SceneTests.Components;

[TestClass]
public sealed class PlayerControllerRecordedReplayTests
{
	const string ReferenceRevision = "be57e3d01ac7d7aa6a2cacbfef187c0b46616db0";
	static string DataDirectory => Path.Combine( AppContext.BaseDirectory, "Data", "MovementReplay" );

	readonly record struct State( int Tick, Vector3 Position, Vector3 Velocity, bool Grounded, bool Ducked );

	[TestMethod]
	[DataRow( "curved-surf-entry" )]
	[DataRow( "grounded-duck-entry" )]
	[DataRow( "grounded-unduck-exit" )]
	[DataRow( "static-ramp-speed" )]
	[DataRow( "stair-ascent-timing" )]
	[DataRow( "stair-descent-limit" )]
	[DataRow( "surf-platform-landing" )]
	[DataRow( "grounded-bank-rim" )]
	public void MatchesRecordedMovement( string name )
		=> Replay( name );

	[TestMethod]
	public void LongCurvedCourseStaysWithinTrajectoryEnvelope()
		=> Replay( "curved-surf-course", allowedGroundMismatches: 3 );

	void Replay( string name, int allowedGroundMismatches = 0 )
	{
		using var document = JsonDocument.Parse( File.ReadAllText( Path.Combine( DataDirectory, "manifest.json" ) ) );
		var root = document.RootElement;
		Assert.AreEqual( 1, root.GetProperty( "version" ).GetInt32() );
		Assert.AreEqual( ReferenceRevision, root.GetProperty( "reference" ).GetProperty( "revision" ).GetString() );
		var fixture = root.GetProperty( "cases" ).EnumerateArray().Single( x => x.GetProperty( "name" ).GetString() == name );
		var settings = root.GetProperty( "settings" );
		var delta = settings.GetProperty( "tickInterval" ).GetSingle();
		var firstTick = fixture.GetProperty( "firstTick" ).GetInt32();
		var lastTick = fixture.GetProperty( "lastTick" ).GetInt32();
		var states = ReadStates( fixture, delta, firstTick, lastTick );
		var positionTolerance = fixture.GetProperty( "positionTolerance" ).GetSingle();
		var velocityTolerance = fixture.GetProperty( "velocityTolerance" ).GetSingle();
		Assert.IsTrue( float.IsFinite( delta ) && delta > 0 && delta <= 0.05f );
		Assert.IsTrue( float.IsFinite( positionTolerance ) && positionTolerance > 0 );
		Assert.IsTrue( float.IsFinite( velocityTolerance ) && velocityTolerance > 0 );

		var scene = new Scene();
		using var scope = scene.Push();
		using var initialTime = Time.Scope( firstTick * (double)delta, 0 );
		try
		{
			scene.PhysicsWorld.Gravity = Vector3.Down * settings.GetProperty( "gravity" ).GetSingle();
			BuildGeometry( scene, fixture.GetProperty( "geometry" ), settings.GetProperty( "groundFriction" ).GetSingle() );
			var player = scene.CreateObject().Components.Create<PlayerController>();
			player.UseInputControls = player.UseLookControls = player.UseCameraControls = player.UseAnimatorControls = false;
			player.EnableFootstepSounds = false;
			// Source specifies a half-width; BodyRadius is the feet-box width.
			player.BodyRadius = settings.GetProperty( "bodyRadius" ).GetSingle() * 2;
			player.BodyHeight = settings.GetProperty( "bodyHeight" ).GetSingle();
			player.DuckedHeight = settings.GetProperty( "duckedHeight" ).GetSingle();
			player.BodyMass = settings.GetProperty( "bodyMass" ).GetSingle();
			player.AirFriction = settings.GetProperty( "airFriction" ).GetSingle();
			player.RunSpeed = settings.GetProperty( "maxSpeed" ).GetSingle();
			player.JumpSpeed = settings.GetProperty( "jumpSpeed" ).GetSingle();
			player.DuckedSpeed = settings.GetProperty( "duckSpeed" ).GetSingle();
			var walk = (MoveModeWalk)player.Mode;
			walk.AirAcceleration = fixture.GetProperty( "airAcceleration" ).GetSingle();

			// Reference state initializes the player once; never correct it mid-replay.
			var initial = states[0];
			player.UpdateDucking( initial.Ducked );
			player.Body.WorldPosition = initial.Position;
			((IScenePhysicsEvents)player.Body).PrePhysicsStep();
			if ( initial.Grounded ) ((IScenePhysicsEvents)player).PostPhysicsStep();
			player.Body.Velocity = initial.Velocity;
			player.UpdateDuckInput( fixture.GetProperty( "previousDuckInput" ).GetBoolean() );
			AssertState( player, initial, name, positionTolerance, velocityTolerance );

			var input = fixture.GetProperty( "input" );
			var initialYaw = input.GetProperty( "yaw" ).GetDouble();
			var yawPerTick = input.GetProperty( "yawPerTick" ).GetDouble();
			var forward = input.GetProperty( "forward" ).GetSingle();
			var side = input.GetProperty( "side" ).GetSingle();
			var duck = input.GetProperty( "duck" ).GetBoolean();
			var jump = input.GetProperty( "jump" ).GetBoolean();
			var movementEndTick = input.TryGetProperty( "movementEndTick", out var endTick ) ? endTick.GetInt32() : int.MaxValue;
			var observation = 1;
			var groundMismatches = 0;
			var oldJump = false;
			for ( int tick = firstTick + 1; tick <= lastTick; ++tick )
			{
				var commandTick = tick - 1;
				using var time = Time.Scope( commandTick * (double)delta, delta );
				var yaw = (float)(initialYaw + Math.Min( commandTick, movementEndTick ) * yawPerTick);
				var eyes = Rotation.FromYaw( yaw );
				player.EyeAngles = new Angles( 0, yaw, 0 );
				player.WishVelocity = (eyes.Forward * forward + eyes.Right * side).ClampLength( player.RunSpeed );
				if ( commandTick >= movementEndTick ) player.WishVelocity = 0;
				if ( player.IsDucking && player.IsOnGround ) player.WishVelocity *= player.DuckedSpeed / player.RunSpeed;
				player.UpdateDuckInput( duck );
				if ( jump && !oldJump && player.IsOnGround ) walk.RequestJump();
				oldJump = jump;
				PlayerControllerTest.SimulateControllerPhysics( scene, player, delta );
				if ( states[observation].Tick != tick ) continue;
				if ( player.IsOnGround != states[observation].Grounded ) groundMismatches++;
				AssertState( player, states[observation++], name, positionTolerance, velocityTolerance, checkGrounded: allowedGroundMismatches == 0 );
			}
			Assert.AreEqual( states.Length, observation );
			Assert.IsTrue( groundMismatches <= allowedGroundMismatches, $"{name}: {groundMismatches} grounded checkpoint mismatches, allowed {allowedGroundMismatches}" );
		}
		finally
		{
			scene.Destroy();
		}
	}

	static State[] ReadStates( JsonElement fixture, float delta, int firstTick, int lastTick )
	{
		var file = fixture.GetProperty( "states" ).GetString();
		Assert.AreEqual( Path.GetFileName( file ), file );
		var lines = File.ReadAllLines( Path.Combine( DataDirectory, file ) );
		var stride = fixture.TryGetProperty( "observationStride", out var strideValue ) ? strideValue.GetInt32() : 1;
		Assert.IsTrue( stride > 0 && lastTick >= firstTick );
		Assert.AreEqual( 0, (lastTick - firstTick) % stride );
		Assert.AreEqual( "tick,time,x,y,z,vx,vy,vz,grounded,ducked", lines[0] );
		Assert.AreEqual( (lastTick - firstTick) / stride + 2, lines.Length, "The recording must contain every declared observation" );
		var states = new State[lines.Length - 1];
		for ( int i = 0; i < states.Length; ++i )
		{
			var fields = lines[i + 1].Split( ',' );
			Assert.AreEqual( 10, fields.Length );
			float F( int column )
			{
				var value = float.Parse( fields[column], CultureInfo.InvariantCulture );
				Assert.IsTrue( float.IsFinite( value ), $"Non-finite observation in {file}, row {i}" );
				return value;
			}
			var tick = int.Parse( fields[0], CultureInfo.InvariantCulture );
			Assert.AreEqual( firstTick + i * stride, tick );
			Assert.AreEqual( tick * (double)delta, double.Parse( fields[1], CultureInfo.InvariantCulture ), 0.00001 );
			Assert.IsTrue( fields[8] is "0" or "1" );
			Assert.IsTrue( fields[9] is "0" or "1" );
			states[i] = new State( tick, new Vector3( F( 2 ), F( 3 ), F( 4 ) ), new Vector3( F( 5 ), F( 6 ), F( 7 ) ), fields[8] == "1", fields[9] == "1" );
		}
		return states;
	}

	static void AssertState( PlayerController player, State expected, string name, float positionTolerance, float velocityTolerance, bool checkGrounded = true )
	{
		var position = player.Body.PhysicsBody.Position;
		var velocity = player.Body.Velocity;
		var context = $"{name}, source tick {expected.Tick}";
		Assert.IsTrue( (position - expected.Position).Length <= positionTolerance, $"{context}: position {position}, recorded {expected.Position}, tolerance {positionTolerance}" );
		Assert.IsTrue( (velocity - expected.Velocity).Length <= velocityTolerance, $"{context}: velocity {velocity}, recorded {expected.Velocity}, tolerance {velocityTolerance}" );
		if ( checkGrounded ) Assert.AreEqual( expected.Grounded, player.IsOnGround, $"{context}: grounded" );
		Assert.AreEqual( expected.Ducked, player.IsDucking, $"{context}: ducked" );
		Assert.AreEqual( PhysicsBodyType.Dynamic, player.Body.PhysicsBody.BodyType, context );
	}

	static Vector3 Vector( JsonElement value ) => new( value[0].GetSingle(), value[1].GetSingle(), value[2].GetSingle() );

	static void BuildGeometry( Scene scene, JsonElement geometry, float friction )
	{
		void AddHull( IEnumerable<Vector3> points )
		{
			var collider = scene.CreateObject().Components.Create<HullCollider>( false );
			collider.Type = HullCollider.PrimitiveType.Points;
			collider.Points = points.ToList();
			collider.Static = true;
			collider.Friction = friction;
			collider.Elasticity = 0;
			collider.Enabled = true;
		}
		foreach ( var shape in geometry.EnumerateArray() )
		{
			switch ( shape.GetProperty( "type" ).GetString() )
			{
				case "box":
					{
						var min = Vector( shape.GetProperty( "mins" ) );
						var max = Vector( shape.GetProperty( "maxs" ) );
						AddHull( [new( min.x, min.y, min.z ), new( max.x, min.y, min.z ), new( max.x, max.y, min.z ), new( min.x, max.y, min.z ),
						new( min.x, min.y, max.z ), new( max.x, min.y, max.z ), new( max.x, max.y, max.z ), new( min.x, max.y, max.z )] );
						break;
					}
				case "hull":
					AddHull( shape.GetProperty( "points" ).EnumerateArray().Select( Vector ) );
					break;
				case "curved-bank":
					{
						var segments = shape.GetProperty( "segments" ).GetInt32();
						var radius = shape.GetProperty( "radius" ).GetDouble();
						var width = shape.GetProperty( "width" ).GetDouble();
						var half = Math.PI / segments;
						var overlap = shape.GetProperty( "overlapDegrees" ).GetDouble() * Math.PI / 180;
						var start = shape.GetProperty( "startDegrees" ).GetDouble() * Math.PI / 180;
						var height = width * Math.Cos( half ) * Math.Sqrt( 3 );
						Vector3 Point( double r, double angle, double z ) => new( (float)(r * Math.Cos( angle )), (float)(r * Math.Sin( angle )), (float)z );
						for ( int i = 0; i < segments; ++i )
						{
							var a = start - half + i * 2 * half - overlap;
							var b = a + 2 * half + 2 * overlap;
							AddHull( [Point( radius + width, a, 0 ), Point( radius + width, b, 0 ), Point( radius, b, 0 ),
							Point( radius, a, 0 ), Point( radius + width, a, height ), Point( radius + width, b, height )] );
						}
						break;
					}
				default:
					throw new InvalidDataException( $"Unknown fixture geometry: {shape}" );
			}
		}
	}
}
