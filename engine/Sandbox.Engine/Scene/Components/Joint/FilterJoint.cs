namespace Sandbox;

[Expose]
[Title( "Physics Filter" )]
[Category( "Physics" )]
[Icon( "do_not_touch" )]
public sealed class PhysicsFilter : Component
{
	PhysicsJoint _joint;
	bool _started;

	/// <summary>
	/// The other body to ignore collisions with.
	/// </summary>
	[Property]
	public GameObject Body
	{
		get => field;
		set
		{
			if ( value == field )
				return;

			field = value;

			CreateJoint();
		}
	}

	protected override void OnStart()
	{
		_started = true;
		CreateJoint();
	}

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
		_started = false;
		DestroyJoint();
	}

	void DestroyJoint()
	{
		if ( !_joint.IsValid() )
			return;

		var body = _joint.Body1;

		_joint.Remove();
		_joint = null;

		if ( body.IsValid() )
			body.ResetProxy();
	}

	void CreateJoint()
	{
		if ( !_started || !Active )
			return;

		DestroyJoint();

		var body1 = Joint.FindPhysicsBody( GameObject, GameObject );
		if ( !body1.IsValid() )
			return;

		var body2 = Joint.FindPhysicsBody( Body, Body );
		if ( !body2.IsValid() )
			body2 = Scene?.PhysicsWorld?.Body;

		if ( !body2.IsValid() )
			return;

		_joint = PhysicsJoint.CreateFilter( body1, body2 );

		if ( _joint.IsValid() )
		{
			body1.ResetProxy();
		}
	}
}
