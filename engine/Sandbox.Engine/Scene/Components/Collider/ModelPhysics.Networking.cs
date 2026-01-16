
namespace Sandbox;

partial class ModelPhysics
{
	/// <summary>
	/// Represents a <see cref="Rigidbody"/> attached to a specific bone with a local transform.
	/// </summary>
	public readonly record struct Body( Rigidbody Component, int Bone, Transform LocalTransform );

	/// <summary>
	/// Represents a <see cref="Sandbox.Joint"/> between two bodies with local frames for each.
	/// </summary>
	public readonly record struct Joint( Sandbox.Joint Component, Body Body1, Body Body2, Transform LocalFrame1, Transform LocalFrame2 );

	/// <summary>
	/// Networked list of bodies.
	/// </summary>
	[Sync, Property, Hide] public List<Body> Bodies { get; set; } = [];

	/// <summary>
	/// Networked list of joints.
	/// </summary>
	[Sync, Property, Hide] public List<Joint> Joints { get; set; } = [];

	/// <summary>
	/// Networked <see cref="Rigidbody"/> transforms.
	/// </summary>
	[Sync] private NetworkTransforms BodyTransforms { get; set; } = new();

	/// <summary>
	/// Sync visual transforms to physics transforms.
	/// </summary>
	void UpdateProxyTransforms()
	{
		if ( !Renderer.IsValid() )
			return;

		var so = Renderer.SceneModel;
		if ( !so.IsValid() )
			return;

		var world = WorldTransform;

		foreach ( var (component, boneIndex, _) in Bodies )
		{
			if ( !component.IsValid() )
				continue;

			if ( !component.GameObject.Flags.Contains( GameObjectFlags.PhysicsBone ) )
				continue;

			var body = component.PhysicsBody;
			if ( !body.IsValid() )
				continue;

			// Set transform to lerped physics transform.
			var bodyTransform = body.GetLerpedTransform( Time.Now ).WithScale( component.WorldScale );
			component.WorldTransform = bodyTransform;

			// Bone overrides are in modelspace, strip off our world transform from body world transform.
			var boneTransform = world.ToLocal( bodyTransform );
			so.SetBoneOverride( boneIndex, boneTransform );
		}
	}

	/// <summary>
	/// Send body transforms.
	/// </summary>
	void SetBodyTransforms()
	{
		for ( var i = 0; i < Bodies.Count; i++ )
		{
			var component = Bodies[i].Component;
			if ( !component.IsValid() )
				continue;

			// If someone enabled networking on this body gameobject then it doesn't need to be sent.
			if ( component.GameObject.NetworkMode == NetworkMode.Object )
				continue;

			var body = component.PhysicsBody;
			if ( !body.IsValid() )
				continue;

			// Physics has gone to sleep, so it's not moving.
			if ( body.Sleeping )
				continue;

			// Mark transform as changed so it gets networked over.
			BodyTransforms.Set( i, body.Transform.WithScale( component.WorldScale ) );
		}
	}

	/// <summary>
	/// Move proxy bodies to networked body transforms.
	/// </summary>
	void MoveProxyBodies()
	{
		var transforms = BodyTransforms.Entries;

		foreach ( var (bodyIndex, transform) in transforms )
		{
			var component = Bodies[bodyIndex].Component;
			if ( !component.IsValid() )
				continue;

			component.WorldScale = transform.Scale;

			var body = component.PhysicsBody;
			if ( !body.IsValid() )
				continue;

			// Take a bit longer to reach target to smooth it out a bit.
			body.Move( transform, Time.Delta * 2.0f );
		}
	}

	protected override void OnRefresh()
	{
		if ( !IsProxy )
			return;

		var transforms = BodyTransforms.Entries;

		foreach ( var (bodyIndex, transform) in transforms )
		{
			var component = Bodies[bodyIndex].Component;
			if ( !component.IsValid() )
				continue;

			// Set transform to initial networked transform so physics get created there.
			component.GameObject.Flags = GameObjectFlags.Absolute;
			component.WorldTransform = transform;
		}
	}
}
