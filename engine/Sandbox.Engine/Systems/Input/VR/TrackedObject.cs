namespace Sandbox.VR;

/// <summary>
/// Represents a physically tracked VR object with a transform
/// </summary>
public record TrackedObject
{
	/// <summary>
	/// Whether or not this object is currently accessible (if false, then the transform will not update).
	/// </summary>
	public bool Active => _trackedDevice.IsActive;

	/// <summary>
	/// Local velocity of this object.
	/// </summary>
	public Vector3 Velocity { get; private set; }

	/// <summary>
	/// Local angular velocity of this object (degrees/s)
	/// </summary>
	public Angles AngularVelocity { get; private set; }

	/// <summary>
	/// The grip pose transform of this tracked object in world space (centered on palm/grip).
	/// This is the default transform used for hand positioning.
	/// </summary>
	public virtual Transform Transform => Input.VR.Anchor.ToWorld( _trackedDevice.Transform );

	/// <summary>
	/// The aim pose transform of this tracked object in world space (pointing forward).
	/// Use this for aiming, pointing, or ray casting.
	/// </summary>
	public virtual Transform AimTransform => Input.VR.Anchor.ToWorld( _trackedDevice.AimTransform );

	/// <summary>
	/// Which part of the body this tracked object represents - waist, left shoulder, etc.
	/// </summary>
	public TrackedDeviceRole Role => _trackedDevice.DeviceRole;

	/// <summary>
	/// What type of object this is - tracker, controller, etc.
	/// </summary>
	public TrackedDeviceType Type => _trackedDevice.DeviceType;

	private Transform _previousTransform;
	internal TrackedDevice _trackedDevice;

	internal TrackedObject( TrackedDevice trackedDevice )
	{
		_trackedDevice = trackedDevice;
	}

	internal virtual void Update()
	{
		_trackedDevice.Update();

		// Calculate velocities
		if ( _previousTransform.IsValid )
		{
			var delta = Transform.Position - _previousTransform.Position;
			Velocity = delta / Time.Delta;

			var deltaRot = Rotation.Difference( _previousTransform.Rotation, Transform.Rotation );
			AngularVelocity = deltaRot.Angles() / Time.Delta;
		}

		_previousTransform = Transform;
	}
}
