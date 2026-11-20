public sealed class TriggerTeleport : Component, Component.ITriggerListener
{
	/// <summary>
	/// If not empty, the target must have one of these tags
	/// </summary>
	[Property, Group( "Target" )] public TagSet Include { get; set; } = new();

	/// <summary>
	/// If not empty, the target must not have one of these tags
	/// </summary>
	[Property, Group( "Target" )] public TagSet Exclude { get; set; } = new();

	[Property] public GameObject Target { get; set; }
	[Property] public Action<GameObject> OnTeleported { get; set; }

	protected override void DrawGizmos()
	{
		if ( !Target.IsValid() )
			return;

		Gizmo.Draw.Arrow( 0, WorldTransform.PointToLocal( Target.WorldPosition ) );
	}

	void ITriggerListener.OnTriggerEnter( Collider other )
	{
		var go = other.GameObject;

		if ( !IsValidTarget( ref go ) ) return;

		go.WorldPosition = Target.WorldPosition;
		go.Transform.ClearInterpolation();

		DoTeleportedEvent( go );
	}

	bool IsValidTarget( ref GameObject go )
	{
		go = go.Root;
		if ( go.IsProxy ) return false;

		if ( !Exclude.IsEmpty && go.Tags.HasAny( Exclude ) )
			return false;

		if ( !Include.IsEmpty && !go.Tags.HasAny( Include ) )
			return false;

		return true;
	}

	[Rpc.Broadcast]
	void DoTeleportedEvent( GameObject obj )
	{
		OnTeleported?.Invoke( obj );
	}
}
