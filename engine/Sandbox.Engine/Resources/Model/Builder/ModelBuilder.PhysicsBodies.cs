using NativeEngine;

namespace Sandbox;

public sealed partial class ModelBuilder
{
	private readonly List<PhysicsBodyBuilder> _bodies = [];

	/// <summary>
	/// Adds a physics body to the model.
	/// Joints refer to bodies by the order they are added.
	/// </summary>
	/// <param name="mass">The mass in kilograms. A value of 0 calculates mass from the body's shapes and surface density.</param>
	/// <param name="surface">The surface properties applied to the body.</param>
	/// <param name="boneName">
	/// Optional name of the bone this body is attached to.
	/// Leave empty for non-skeletal bodies.
	/// </param>
	/// <returns>A builder for the new body.</returns>
	public PhysicsBodyBuilder AddBody( float mass = default, Surface surface = default, string boneName = default )
	{
		var builder = new PhysicsBodyBuilder
		{
			Mass = mass,
			Surface = surface,
			BoneName = boneName,
			BindPose = Transform.Zero
		};

		_bodies.Add( builder );
		return builder;
	}

	private CPhysBodyDescArray CreatePhysicsBodies()
	{
		if ( _bodies.Count == 0 )
			return CreateLegacyPhysicsBodies();

		return CPhysBodyDescArray.Create( _bodies, _joints, _surfaces );
	}
}
