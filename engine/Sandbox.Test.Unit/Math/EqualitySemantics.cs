namespace MathTest;

/// <summary>
/// Formalizes the equality contract for math types that use approximate == and exact Equals:
///   - operator ==  ->  AlmostEqual (approximate, for gameplay comparisons)
///   - Equals()     ->  exact bitwise (for serialization, hashing, dictionary keys)
///
/// This ensures we don't accidentally regress either direction:
///   - Making == exact would break gameplay code that relies on tolerance
///   - Making Equals approximate would break serialization round-trip checks
/// </summary>
[TestClass]
public class EqualitySemantics
{
	[TestMethod]
	public void Vector3_OperatorEquals_IsApproximate()
	{
		var a = new Vector3( 1, 2, 3 );
		var b = new Vector3( 1, 2, 3 + 5e-5f );

		Assert.IsTrue( a == b, "operator == should use AlmostEqual and treat tiny differences as equal" );
		Assert.IsFalse( a != b );
	}

	[TestMethod]
	public void Vector3_Equals_IsExact()
	{
		var a = new Vector3( 1, 2, 3 );
		var b = new Vector3( 1, 2, 3 + 5e-5f );

		Assert.IsFalse( a.Equals( b ), "Equals should be exact bitwise and reject any difference" );
	}

	[TestMethod]
	public void Vector3_Equals_IdenticalValues()
	{
		var a = new Vector3( 1, 2, 3 );
		var b = new Vector3( 1, 2, 3 );

		Assert.IsTrue( a == b );
		Assert.IsTrue( a.Equals( b ) );
	}

	[TestMethod]
	public void Rotation_OperatorEquals_IsApproximate()
	{
		var a = Rotation.Identity;
		var b = Rotation.Identity;

		// Nudge one quaternion component by a tiny amount within the almost equals tolerance
		b._quat.X += 5e-6f;

		Assert.IsTrue( a == b, "operator == should use AlmostEqual and treat tiny differences as equal" );
		Assert.IsFalse( a != b );
	}

	[TestMethod]
	public void Rotation_Equals_IsExact()
	{
		var a = Rotation.Identity;
		var b = Rotation.Identity;

		b._quat.X += 1e-5f;

		Assert.IsFalse( a.Equals( b ), "Equals should be exact bitwise and reject any difference" );
	}

	[TestMethod]
	public void Rotation_Equals_IdenticalValues()
	{
		var a = Rotation.FromAxis( Vector3.Up, 45 );
		var b = Rotation.FromAxis( Vector3.Up, 45 );

		Assert.IsTrue( a == b );
		Assert.IsTrue( a.Equals( b ) );
	}

	[TestMethod]
	public void Transform_OperatorEquals_IsApproximate()
	{
		var a = new Transform( new Vector3( 100, 200, 300 ), Rotation.Identity, 1 );
		var b = new Transform( new Vector3( 100, 200, 300 + 5e-5f ), Rotation.Identity, 1 );

		Assert.IsTrue( a == b, "operator == should use AlmostEqual and treat tiny differences as equal" );
		Assert.IsFalse( a != b );
	}

	[TestMethod]
	public void Transform_Equals_IsExact()
	{
		var a = new Transform( new Vector3( 100, 200, 300 ), Rotation.Identity, 1 );
		var b = new Transform( new Vector3( 100, 200, 300 + 5e-5f ), Rotation.Identity, 1 );

		Assert.IsFalse( a.Equals( b ), "Equals should be exact bitwise and reject any difference" );
	}

	[TestMethod]
	public void Transform_Equals_IdenticalValues()
	{
		var a = new Transform( new Vector3( 100, 200, 300 ), Rotation.FromAxis( Vector3.Up, 90 ), 2 );
		var b = new Transform( new Vector3( 100, 200, 300 ), Rotation.FromAxis( Vector3.Up, 90 ), 2 );

		Assert.IsTrue( a == b );
		Assert.IsTrue( a.Equals( b ) );
	}

	/// <summary>
	/// The exact scenario that caused phantom prefab overrides: a value below
	/// the 0.0001 AlmostEqual tolerance must be distinguishable via Equals.
	/// </summary>
	[TestMethod]
	public void Transform_Equals_DetectsSubToleranceDrift()
	{
		var prefab = new Transform( new Vector3( 226, -4446, -7.247925E-05f ), Rotation.Identity, 1 );
		var instance = new Transform( new Vector3( 226, -4446, 0 ), Rotation.Identity, 1 );

		Assert.IsTrue( prefab == instance, "operator == should consider these approximately equal" );
		Assert.IsFalse( prefab.Equals( instance ), "Equals must detect the sub-tolerance difference" );
	}

	/// <summary>
	/// q and -q represent the same orientation; AlmostEqual uses |Dot| to handle this.
	/// </summary>
	[TestMethod]
	public void Rotation_AlmostEqual_TreatsAntipodalQuaternionsAsEqual()
	{
		var q = Rotation.FromAxis( Vector3.Up, 45 );
		var negQ = new Rotation( -q._quat.X, -q._quat.Y, -q._quat.Z, -q._quat.W );

		Assert.IsTrue( q.AlmostEqual( negQ ) );
		Assert.IsTrue( q == negQ );
	}

	/// <summary>
	/// Equals is bitwise, so q and -q are distinct even though they're the same orientation.
	/// </summary>
	[TestMethod]
	public void Rotation_Equals_DistinguishesAntipodalQuaternions()
	{
		var q = Rotation.FromAxis( Vector3.Up, 45 );
		var negQ = new Rotation( -q._quat.X, -q._quat.Y, -q._quat.Z, -q._quat.W );

		Assert.IsFalse( q.Equals( negQ ) );
	}

	/// <summary>
	/// Default delta = 1e-5 ≈ 0.0012° angular tolerance.
	/// </summary>
	[TestMethod]
	public void Rotation_AlmostEqual_DefaultToleranceIsAbout0Point0012Degrees()
	{
		var identity = Rotation.Identity;

		// 0.001° is within ~0.0012° tolerance
		var inside = Rotation.FromAxis( Vector3.Up, 0.001f );
		Assert.IsTrue( identity.AlmostEqual( inside ) );

		// 0.01° is outside ~0.0012° tolerance
		var outside = Rotation.FromAxis( Vector3.Up, 0.01f );
		Assert.IsFalse( identity.AlmostEqual( outside ) );
	}

	/// <summary>
	/// Two zero-length (default) quaternions must compare equal.
	/// </summary>
	[TestMethod]
	public void Rotation_AlmostEqual_ZeroQuaternionsAreEqual()
	{
		Rotation a = default;
		Rotation b = default;
		Assert.IsTrue( a.AlmostEqual( b ), "Two zero quaternions should be considered equal" );
		Assert.IsTrue( a == b, "operator == on zero quaternions should return true" );
	}

	/// <summary>
	/// Zero vs non-zero quaternion must not be equal.
	/// </summary>
	[TestMethod]
	public void Rotation_AlmostEqual_ZeroVsNonZeroNotEqual()
	{
		Rotation zero = default;
		Assert.IsFalse( zero.AlmostEqual( Rotation.Identity ) );
		Assert.IsFalse( zero == Rotation.Identity );
	}

	/// <summary>
	/// default(Transform) == default(Transform) must be true.
	/// </summary>
	[TestMethod]
	public void Transform_Default_EqualsDefault()
	{
		Transform a = default;
		Transform b = default;
		Assert.IsTrue( a == b, "default(Transform) == default(Transform) must be true" );
	}
}
