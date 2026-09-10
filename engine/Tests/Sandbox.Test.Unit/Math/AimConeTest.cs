using System;

namespace MathTests;

[TestClass]
public class AimConeTest
{
	/// <summary>
	/// A zero-degree cone never deflects - the direction comes back unchanged regardless of the
	/// random source.
	/// </summary>
	[TestMethod]
	public void ZeroConeIsIdentity()
	{
		var random = new System.Random( 1234 );

		for ( int i = 0; i < 16; i++ )
		{
			var direction = new Vector3( 1, 0.3f, -0.2f ).Normal;
			var deflected = direction.WithAimCone( 0f, 0f, random );

			Assert.AreEqual( 1f, direction.Dot( deflected ), 0.0001f );
		}
	}

	/// <summary>
	/// Deflection stays within the cone - a direction pushed through an N degree cone never deviates
	/// more than N/2 degrees per axis (so at most ~N/√2 total) from the original.
	/// </summary>
	[TestMethod]
	public void DeflectionStaysWithinCone()
	{
		var random = new System.Random( 5678 );
		var direction = Vector3.Forward;

		for ( int i = 0; i < 256; i++ )
		{
			var deflected = direction.WithAimCone( 10f, 10f, random );

			var angle = MathF.Acos( direction.Dot( deflected.Normal ).Clamp( -1f, 1f ) ).RadianToDegree();
			Assert.IsTrue( angle <= 10f, $"Deflected {angle} degrees - outside a 10 degree cone" );
		}
	}

	/// <summary>
	/// The horizontal and vertical cone sizes act independently - a wide flat cone deflects yaw but
	/// never pitch.
	/// </summary>
	[TestMethod]
	public void ConeAxesAreIndependent()
	{
		var random = new System.Random( 9012 );
		var direction = Vector3.Forward;

		for ( int i = 0; i < 64; i++ )
		{
			var deflected = direction.WithAimCone( 20f, 0f, random );
			var angles = Rotation.LookAt( deflected ).Angles();

			Assert.AreEqual( 0f, angles.pitch, 0.001f, "Flat cone should never deflect pitch" );
			Assert.IsTrue( MathF.Abs( angles.yaw ) <= 10.001f, $"Yaw {angles.yaw} outside half-cone" );
		}
	}

	/// <summary>
	/// The same seed produces the same deflection - spread is reproducible when the caller controls
	/// the random source.
	/// </summary>
	[TestMethod]
	public void SeededConeIsDeterministic()
	{
		var a = Vector3.Forward.WithAimCone( 5f, 5f, new System.Random( 42 ) );
		var b = Vector3.Forward.WithAimCone( 5f, 5f, new System.Random( 42 ) );

		Assert.AreEqual( a.x, b.x, 0.000001f );
		Assert.AreEqual( a.y, b.y, 0.000001f );
		Assert.AreEqual( a.z, b.z, 0.000001f );
	}
}
