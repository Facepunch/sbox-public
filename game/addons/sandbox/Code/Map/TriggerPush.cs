[Category( "Gameplay" ), Icon( "compare_arrows" ), EditorHandle( Icon = "🌠" )]
public sealed class TriggerPush : Component, Component.ITriggerListener
{
	[Property] Vector3 Direction { get; set; } = Vector3.Up;
	[Property] float Power { get; set; } = 20.0f;

	[Property]
	List<GameObject> Objects = new();

	protected override void DrawGizmos()
	{
		Gizmo.Draw.Arrow( 0, Direction * 50 );
	}

	protected override void OnFixedUpdate()
	{
		if ( Objects is null )
			return;

		foreach ( var obj in Objects )
		{
			var plycomp = obj.Components.Get<Player>();
			var cc = obj.Components.Get<Rigidbody>( FindMode.EverythingInSelfAndParent );

			if(plycomp.IsValid())
				plycomp.Controller.PreventGrounding( 0.1f );

			if ( !cc.IsValid() )
			{
				Objects.Remove( obj );
				return;
			}

			cc.Velocity += Direction * Power;
		}
	}

	void ITriggerListener.OnTriggerEnter( Collider other )
	{
		if ( other.GameObject.Components.Get<Rigidbody>(FindMode.EverythingInSelfAndParent).IsValid())
		{
			var obj = other.GameObject.Root;
			Objects.Add( obj );
		}
	}

	void ITriggerListener.OnTriggerExit( Collider other )
	{
		if ( other.GameObject.Components.Get<Rigidbody>( FindMode.EverythingInSelfAndParent ).IsValid() )
		{
			var obj = other.GameObject.Root;
			Objects.Remove( obj );
		}
	}
}
