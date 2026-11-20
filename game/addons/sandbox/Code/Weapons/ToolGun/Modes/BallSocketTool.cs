
[Icon( "🎱" )]
[Title( "#tool.name.ballsocket" )]
[ClassName( "ballsocket" )]
[Group( "#tool.group.constraints" )]
public sealed class BallSocketTool : BaseConstraintToolMode
{
	[Property, Sync, Title( "#tool.setting.enable_collision" )]
	public bool EnableCollision { get; set; } = false;

	public override string Description => Stage == 1 ? "#tool.hint.ballsocket.stage1" : "#tool.hint.ballsocket.stage0";
	public override string PrimaryAction => Stage == 1 ? "#tool.hint.ballsocket.finish" : "#tool.hint.ballsocket.source";
	public override string ReloadAction => "#tool.hint.ballsocket.remove";

	protected override IEnumerable<GameObject> FindConstraints( GameObject linked, GameObject target )
	{
		foreach ( var joint in linked.GetComponentsInChildren<BallJoint>( true ) )
			if ( linked == target || joint.Body?.Root == target )
				yield return joint.GameObject;
	}

	protected override void CreateConstraint( SelectionPoint point1, SelectionPoint point2 )
	{
		if ( point1.GameObject == point2.GameObject )
			return;

		var go2 = new GameObject( point2.GameObject, false, "ballsocket" );
		go2.LocalTransform = point2.LocalTransform;

		var go1 = new GameObject( point1.GameObject, false, "ballsocket" );
		go1.WorldTransform = go2.WorldTransform;

		var joint = go1.AddComponent<BallJoint>();
		joint.Body = go2;
		joint.Friction = 0.0f;
		joint.EnableCollision = EnableCollision;

		go2.NetworkSpawn();
		go1.NetworkSpawn();

		Track( go1, go2 );

		var undo = Player.Undo.Create();
		undo.Name = "#tool.name.ballsocket";
		undo.Add( go1, go2 );
	}
}
