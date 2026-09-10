using System;

namespace MathTests;

[TestClass]
public class CapsuleTest
{
	static readonly Capsule UprightCapsule = new( new Vector3( 0, 0, 10 ), new Vector3( 0, 0, 90 ), 10 );

	/// <summary>
	/// Contains should be true on the axis, inside the end caps, and false
	/// beyond the radius or past the cap spheres.
	/// </summary>
	[TestMethod]
	public void Contains()
	{
		Assert.IsTrue( UprightCapsule.Contains( new Vector3( 0, 0, 50 ) ) );
		Assert.IsTrue( UprightCapsule.Contains( new Vector3( 5, 0, 50 ) ) );
		Assert.IsTrue( UprightCapsule.Contains( new Vector3( 0, 0, 95 ) ) );

		Assert.IsFalse( UprightCapsule.Contains( new Vector3( 20, 0, 50 ) ) );
		Assert.IsFalse( UprightCapsule.Contains( new Vector3( 0, 0, 150 ) ) );
	}

	/// <summary>
	/// Bounds should fully enclose both cap spheres.
	/// </summary>
	[TestMethod]
	public void Bounds()
	{
		var bounds = UprightCapsule.Bounds;

		Assert.IsTrue( bounds.Mins.z <= 0 );
		Assert.IsTrue( bounds.Maxs.z >= 100 );
		Assert.IsTrue( bounds.Mins.x <= -10 );
		Assert.IsTrue( bounds.Maxs.x >= 10 );
	}

	/// <summary>
	/// The analytic volume should match cylinder + sphere for a simple capsule.
	/// </summary>
	[TestMethod]
	public void Volume()
	{
		// 80 tall cylinder of radius 10 plus a complete sphere of radius 10
		var expected = MathF.PI * 100f * 80f + (4f / 3f) * MathF.PI * 1000f;

		Assert.AreEqual( expected, UprightCapsule.Volume, expected * 0.001f );
	}

	/// <summary>
	/// Edge distance should grow as the query point moves away from the surface.
	/// </summary>
	[TestMethod]
	public void EdgeDistanceOrdering()
	{
		var near = UprightCapsule.GetEdgeDistance( new Vector3( 15, 0, 50 ) );
		var far = UprightCapsule.GetEdgeDistance( new Vector3( 50, 0, 50 ) );

		Assert.IsTrue( near < far );
	}

	/// <summary>
	/// Sampled random points must always satisfy Contains.
	/// </summary>
	[TestMethod]
	public void RandomPointInsideIsContained()
	{
		for ( int i = 0; i < 100; i++ )
		{
			var p = UprightCapsule.RandomPointInside;
			Assert.IsTrue( UprightCapsule.Contains( p ), $"{p} not inside capsule" );
		}
	}

	/// <summary>
	/// Capsules with identical centers and radius should be equal.
	/// </summary>
	[TestMethod]
	public void Equality()
	{
		var a = new Capsule( Vector3.Zero, Vector3.Up, 5 );
		var b = new Capsule( Vector3.Zero, Vector3.Up, 5 );
		var c = new Capsule( Vector3.Zero, Vector3.Up, 6 );

		Assert.IsTrue( a == b );
		Assert.IsTrue( a != c );
	}
}
