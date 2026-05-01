namespace Sandbox.Physics;

/// <summary>
/// A parallel joint that constrains the Z-axes of two bodies to be parallel using a spring.
/// Useful for keeping a physics body upright relative to another body or a static anchor.
/// </summary>
public partial class UprightJoint : PhysicsJoint
{
	internal UprightJoint( HandleCreationData _ ) { }

	/// <summary>
	/// The spring stiffness in cycles per second (Hertz).
	/// Higher values make the constraint stiffer.
	/// </summary>
	public float Hertz
	{
		get => native.Parallel_GetHertz();
		set => native.Parallel_SetHertz( value );
	}

	/// <summary>
	/// The spring damping ratio (non-dimensional).
	/// A value of 1 is critically damped; values below 1 are under-damped (springy).
	/// </summary>
	public float DampingRatio
	{
		get => native.Parallel_GetDampingRatio();
		set => native.Parallel_SetDampingRatio( value );
	}

	/// <summary>
	/// The maximum torque the joint can apply.
	/// </summary>
	public float MaxTorque
	{
		get => native.Parallel_GetMaxTorque();
		set => native.Parallel_SetMaxTorque( value );
	}
}
