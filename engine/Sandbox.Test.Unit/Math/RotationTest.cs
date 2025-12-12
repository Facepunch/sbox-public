using System;
using System.Collections.Generic;

namespace MathTest;

[TestClass]
public class RotationTest
{
	private static bool AlmostEqual( float a, float b, float precision = 0.0001f )
	{
		return a.AlmostEqual( b, 0.001f );
	}

	private static bool AlmostEqual( Rotation a, Rotation b )
	{
		var forward = a.Forward.Dot( b.Forward );
		var right = a.Right.Dot( b.Right );
		var up = a.Up.Dot( b.Up );

		return AlmostEqual( forward, 1f ) && AlmostEqual( right, 1f ) && AlmostEqual( up, 1f );
	}

	private static bool AlmostEqual( Angles a, Angles b )
	{
		return AlmostEqual( a.yaw, b.yaw ) && AlmostEqual( a.pitch, b.pitch ) && AlmostEqual( a.roll, b.roll );
	}

	/// <summary>
	/// Generate a bunch of rotations, and make sure they return trip from euler angles.
	/// </summary>
	[TestMethod]
	public void AnglesRandom()
	{
		SandboxSystem.SetRandomSeed( 123456 );

		for ( var i = 0; i < 10_000; ++i )
		{
			var srcRotation = Rotation.Random;
			var angles = srcRotation.Angles();
			var dstRotation = angles.ToRotation();

			Assert.IsTrue( AlmostEqual( srcRotation, dstRotation ) );
		}
	}

	[TestMethod]
	public void ParseAngles()
	{
		{
			Rotation r = Rotation.Parse( "5,3,2" );
			var angles = new Angles( 5, 3, 2 );

			Assert.IsTrue( AlmostEqual( r.Angles(), angles ) );
		}

		{
			Rotation r = Rotation.Parse( "5, 3, 2" );
			var angles = new Angles( 5, 3, 2 );

			Assert.IsTrue( AlmostEqual( r.Angles(), angles ) );
		}

		{
			Rotation r = Rotation.Parse( "[5,3,2]" );
			var angles = new Angles( 5, 3, 2 );

			Assert.IsTrue( AlmostEqual( r.Angles(), angles ) );
		}

		{
			Rotation r = Rotation.Parse( "[	5,	3,	2]	" );
			var angles = new Angles( 5, 3, 2 );

			Assert.IsTrue( AlmostEqual( r.Angles(), angles ) );
		}

		{
			Rotation r = Rotation.Parse( "[	5,\n\r   3,\n\r   2,\n\r		]	" );
			var angles = new Angles( 5, 3, 2 );

			Assert.IsTrue( AlmostEqual( r.Angles(), angles ) );
		}

	}

	/// <summary>
	/// Test converting to and from angles near +-90 pitch.
	/// </summary>
	[TestMethod]
	public void AnglesPoles()
	{
		for ( var pitch = 87f; pitch <= 100f; pitch += 1f / 512f )
		{
			for ( var yaw = -180; yaw < 180; yaw += 1 )
			{
				for ( var sign = -1; sign <= 1; sign += 2 )
				{
					var srcRotation = new Angles( pitch * sign, yaw, 0f ).ToRotation();
					var angles = srcRotation.Angles();
					var dstRotation = angles.ToRotation();

					Assert.IsTrue( AlmostEqual( srcRotation, dstRotation ) );
				}
			}
		}
	}

	/// <summary>
	/// Test converting to and from tricky angles against other engines
	/// </summary>
	[TestMethod]
	public void AnglesParity()
	{
		// Unity poles but not normalized
		var list = new List<(Angles, Angles)>()
		{
			new( new( 90, 112, 19 ), new( 90, 93, 0 ) ),
			new( new( 90, 12, 180 ), new( 90, -168, 0 ) ),
			new( new( -90, 90, -60 ), new( -90, 30, 0 ) ),
		};

		foreach ( var a in list )
		{
			Console.WriteLine( $"{Rotation.From( a.Item1 ).Angles()}\t{a.Item2}" );
			Assert.IsTrue( AlmostEqual( Rotation.From( a.Item1 ).Angles(), a.Item2 ) );
		}
	}

	[TestMethod]
	public void Parse()
	{
		{
			Rotation r = Rotation.Parse( "1.1,2.1,3.1,4.1" );

			Assert.AreEqual( r.x, 1.1f );
			Assert.AreEqual( r.y, 2.1f );
			Assert.AreEqual( r.z, 3.1f );
			Assert.AreEqual( r.w, 4.1f );
		}

		{
			Rotation r = Rotation.Parse( "1.1, 2.1, 3.1, 4.1" );

			Assert.AreEqual( r.x, 1.1f );
			Assert.AreEqual( r.y, 2.1f );
			Assert.AreEqual( r.z, 3.1f );
			Assert.AreEqual( r.w, 4.1f );
		}

		{
			Rotation r = Rotation.Parse( "[1.1,2.1,3.1,4.1]" );

			Assert.AreEqual( r.x, 1.1f );
			Assert.AreEqual( r.y, 2.1f );
			Assert.AreEqual( r.z, 3.1f );
			Assert.AreEqual( r.w, 4.1f );
		}

		{
			Rotation r = Rotation.Parse( "[	1.1,	2.1,	3.1,	4.1]	" );

			Assert.AreEqual( r.x, 1.1f );
			Assert.AreEqual( r.y, 2.1f );
			Assert.AreEqual( r.z, 3.1f );
			Assert.AreEqual( r.w, 4.1f );
		}

		{
			Rotation r = Rotation.Parse( "[	1.1,\n\r   2.1,\n\r   3.1,\n\r		4.1\n\r		]	" );

			Assert.AreEqual( r.x, 1.1f );
			Assert.AreEqual( r.y, 2.1f );
			Assert.AreEqual( r.z, 3.1f );
			Assert.AreEqual( r.w, 4.1f );
		}
	}

	[TestMethod]
	public void TryParse()
	{
		{
			Assert.IsTrue( Rotation.TryParse( "1.1,2.1,3.1,4.1", out var r ) );

			Assert.AreEqual( r.x, 1.1f );
			Assert.AreEqual( r.y, 2.1f );
			Assert.AreEqual( r.z, 3.1f );
			Assert.AreEqual( r.w, 4.1f );
		}

		{
			Assert.IsTrue( Rotation.TryParse( "1.1, 2.1, 3.1, 4.1", out var r ) );

			Assert.AreEqual( r.x, 1.1f );
			Assert.AreEqual( r.y, 2.1f );
			Assert.AreEqual( r.z, 3.1f );
			Assert.AreEqual( r.w, 4.1f );
		}

		{
			Assert.IsTrue( Rotation.TryParse( "[1.1,2.1,3.1,4.1]", out var r ) );

			Assert.AreEqual( r.x, 1.1f );
			Assert.AreEqual( r.y, 2.1f );
			Assert.AreEqual( r.z, 3.1f );
			Assert.AreEqual( r.w, 4.1f );
		}

		{
			Assert.IsTrue( Rotation.TryParse( "[	1.1,	2.1,	3.1,	4.1]	", out var r ) );

			Assert.AreEqual( r.x, 1.1f );
			Assert.AreEqual( r.y, 2.1f );
			Assert.AreEqual( r.z, 3.1f );
			Assert.AreEqual( r.w, 4.1f );
		}

		{
			Assert.IsTrue( Rotation.TryParse( "[	1.1,\n\r   2.1,\n\r   3.1,\n\r		4.1\n\r		]	", out var r ) );

			Assert.AreEqual( r.x, 1.1f );
			Assert.AreEqual( r.y, 2.1f );
			Assert.AreEqual( r.z, 3.1f );
			Assert.AreEqual( r.w, 4.1f );
		}

		{
			Assert.IsFalse( Rotation.TryParse( "1", out _ ) );
			Assert.IsFalse( Rotation.TryParse( "abcdef", out _ ) );
			Assert.IsFalse( Rotation.TryParse( "1.1, 2.2", out _ ) );
		}
	}
}
