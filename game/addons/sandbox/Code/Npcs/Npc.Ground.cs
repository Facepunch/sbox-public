namespace Sandbox.Npcs;

public partial class Npc : IPhysgunEvent
{
	/// <summary>
	/// Are we on the ground?
	/// </summary>
	public bool IsOnGround => GroundObject.IsValid();

	/// <summary>
	/// The object we're standing on. Null if we're standing on nothing.
	/// </summary>
	public GameObject GroundObject { get; set; }

	/// <summary>
	/// The collider component we're standing on. Null if we're standing nothing
	/// </summary>
	public Component GroundComponent { get; set; }

	/// <summary>
	/// If we're stnding on a surface this is it
	/// </summary>
	public Surface GroundSurface { get; set; }

	/// <summary>
	/// Are we being held by a player?
	/// </summary>
	bool isPickedUp = false;

	void CategorizeGround()
	{
		if ( !_navAgent.IsValid() )
			return;

		if ( isPickedUp )
		{
			// never grounded while being held by a player, even if we are on the ground
			UpdateGroundFromTraceResult( default );
			return;
		}

		var up = WorldRotation.Up;
		var from = WorldPosition + up * 16;
		var to = WorldPosition - up * 8;

		var tr = Scene.Trace.Ray( from, to )
								.IgnoreGameObjectHierarchy( GameObject )
								.WithCollisionRules( Tags )
								.Run();

		if ( !tr.StartedSolid && tr.Hit )
		{
			UpdateGroundFromTraceResult( tr );
		}
		else
		{
			UpdateGroundFromTraceResult( default );
		}
	}

	void IPhysgunEvent.OnPhysgunPickup()
	{
		isPickedUp = true;
		if ( _navAgent.IsValid() ) _navAgent.UpdateRotation = false;
	}

	void IPhysgunEvent.OnPhysgunDrop()
	{
		isPickedUp = false;
		if ( _navAgent.IsValid() ) _navAgent.UpdateRotation = true;
	}

	void UpdateGroundFromTraceResult( SceneTraceResult tr )
	{
		bool wasGrounded = IsOnGround;

		var body = tr.Body;
		GroundObject = body?.GameObject;
		GroundComponent = body?.Component;
		GroundSurface = tr.Surface;

		if ( IsOnGround != wasGrounded )
		{
			OnGroundedChanged();
		}
	}

	private void OnGroundedChanged()
	{
		if ( _rigidbody.IsValid() ) _rigidbody.MotionEnabled = !IsOnGround;
		if ( _navAgent.IsValid() ) _navAgent.Enabled = IsOnGround;
	}
}
