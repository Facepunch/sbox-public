namespace Sandbox.Physics;

/// <summary>
/// A generic "rope" type constraint.
/// </summary>
public partial class FixedJoint : PhysicsJoint
{
	internal FixedJoint( PhysicsJointInternal joint ) : base( joint ) { }

	/// <summary>
	/// How springy and tight the joint will be in its movement.
	/// </summary>
	public PhysicsSpring SpringLinear
	{
		get => _joint?.SpringLinear ?? default;
		set => _joint?.SpringLinear = value;
	}

	/// <summary>
	/// How springy and tight the joint will be in its rotation.
	/// </summary>
	public PhysicsSpring SpringAngular
	{
		get => _joint?.SpringAngular ?? default;
		set => _joint?.SpringAngular = value;
	}
}
