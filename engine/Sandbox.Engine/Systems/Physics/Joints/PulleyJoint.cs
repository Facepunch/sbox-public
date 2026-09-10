namespace Sandbox.Physics;

/// <summary>
/// A pulley constraint. Consists of 2 ropes which share same length, and the ratio changes via physics interactions.
/// </summary>
public partial class PulleyJoint : PhysicsJoint
{
	internal PulleyJoint( PhysicsJointInternal joint ) : base( joint ) { }
}
