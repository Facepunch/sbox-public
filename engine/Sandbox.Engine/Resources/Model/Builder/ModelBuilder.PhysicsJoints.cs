namespace Sandbox;

public sealed partial class ModelBuilder
{
	private readonly List<PhysicsJointBuilder> _joints = [];

	private static void InitJoint( PhysicsJointBuilder builder, int body1, int body2, Transform? frame1, Transform? frame2, bool collision )
	{
		builder.Body1 = body1;
		builder.Body2 = body2;
		builder.Frame1 = frame1 ?? Transform.Zero;
		builder.Frame2 = frame2 ?? Transform.Zero;
		builder.EnableCollision = collision;
	}

	/// <summary>
	/// Adds a hinge joint between two bodies, allowing rotation around a single axis.
	/// </summary>
	/// <param name="body1">The first body index, using the order bodies were added.</param>
	/// <param name="body2">The second body index, using the order bodies were added.</param>
	/// <param name="frame1">The joint frame in the local space of the first body.</param>
	/// <param name="frame2">The joint frame in the local space of the second body.</param>
	/// <param name="collision">Whether the connected bodies can collide. The default is false.</param>
	/// <returns>A builder for the new joint.</returns>
	public HingeJointBuilder AddHingeJoint( int body1, int body2, Transform? frame1 = default, Transform? frame2 = default, bool collision = false )
	{
		var builder = new HingeJointBuilder();
		InitJoint( builder, body1, body2, frame1, frame2, collision );
		_joints.Add( builder );
		return builder;
	}

	/// <summary>
	/// Adds a ball joint between two bodies, allowing free rotation within optional swing/twist limits.
	/// </summary>
	/// <param name="body1">The first body index, using the order bodies were added.</param>
	/// <param name="body2">The second body index, using the order bodies were added.</param>
	/// <param name="frame1">The joint frame in the local space of the first body.</param>
	/// <param name="frame2">The joint frame in the local space of the second body.</param>
	/// <param name="collision">Whether the connected bodies can collide. The default is false.</param>
	/// <returns>A builder for the new joint.</returns>
	public BallJointBuilder AddBallJoint( int body1, int body2, Transform? frame1 = default, Transform? frame2 = default, bool collision = false )
	{
		var builder = new BallJointBuilder();
		InitJoint( builder, body1, body2, frame1, frame2, collision );
		_joints.Add( builder );
		return builder;
	}

	/// <summary>
	/// Adds a fixed joint between two bodies, locking their relative position and orientation.
	/// </summary>
	/// <param name="body1">The first body index, using the order bodies were added.</param>
	/// <param name="body2">The second body index, using the order bodies were added.</param>
	/// <param name="frame1">The joint frame in the local space of the first body.</param>
	/// <param name="frame2">The joint frame in the local space of the second body.</param>
	/// <param name="collision">Whether the connected bodies can collide. The default is false.</param>
	/// <returns>A builder for the new joint.</returns>
	public FixedJointBuilder AddFixedJoint( int body1, int body2, Transform? frame1 = default, Transform? frame2 = default, bool collision = false )
	{
		var builder = new FixedJointBuilder();
		InitJoint( builder, body1, body2, frame1, frame2, collision );
		_joints.Add( builder );
		return builder;
	}

	/// <summary>
	/// Adds a slider joint between two bodies, allowing motion along a single axis.
	/// </summary>
	/// <param name="body1">The first body index, using the order bodies were added.</param>
	/// <param name="body2">The second body index, using the order bodies were added.</param>
	/// <param name="frame1">The joint frame in the local space of the first body.</param>
	/// <param name="frame2">The joint frame in the local space of the second body.</param>
	/// <param name="collision">Whether the connected bodies can collide. The default is false.</param>
	/// <returns>A builder for the new joint.</returns>
	public SliderJointBuilder AddSliderJoint( int body1, int body2, Transform? frame1 = default, Transform? frame2 = default, bool collision = false )
	{
		var builder = new SliderJointBuilder();
		InitJoint( builder, body1, body2, frame1, frame2, collision );
		_joints.Add( builder );
		return builder;
	}
}
