namespace Sandbox;

[Expose]
[Title( "Physics Filter" )]
[Category( "Physics" )]
[Icon( "do_not_touch" )]
public sealed class PhysicsFilter : Component
{
	/// <summary>
	/// The other body to ignore collisions with.
	/// </summary>
	[Property]
	public GameObject Body
	{
		get => field;
		set
		{
			if ( value == field ) return;

			field = value;

			CreateJoint();
		}
	}

	PhysicsJoint _joint;

	protected override void OnEnabled()
	{
		CreateJoint();
	}

	protected override void OnDisabled()
	{
		DestroyJoint();
	}

	protected override void OnDestroy()
	{
		DestroyJoint();
	}

	void DestroyJoint()
	{
		if ( !_joint.IsValid() ) return;

		var body = _joint.Body1;

		_joint.Remove();
		_joint = null;

		if ( !body.IsValid() ) return;

		body.ResetProxy();
	}

	void CreateJoint()
	{
		if ( !Active ) return;

		var body1 = Joint.FindPhysicsBody( GameObject, GameObject );
		if ( !body1.IsValid() )
			return;

		var body2 = Joint.FindPhysicsBody( Body, Body );
		if ( !body2.IsValid() )
			body2 = Scene?.PhysicsWorld?.Body;

		_joint?.Remove();
		_joint = PhysicsJoint.CreateFilter( body1, body2 );

		body1.ResetProxy();
	}
}
