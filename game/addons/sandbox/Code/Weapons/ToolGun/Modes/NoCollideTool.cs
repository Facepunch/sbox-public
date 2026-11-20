
[Icon( "⛔" )]
[Title( "#tool.name.nocollide" )]
[ClassName( "nocollide" )]
[Group( "#tool.group.tools" )]
public sealed class NoCollideTool : BaseConstraintToolMode
{
	public override string Description => Stage == 1 ? "#tool.hint.nocollide.stage1" : "#tool.hint.nocollide.stage0";
	public override string PrimaryAction => Stage == 1 ? "#tool.hint.nocollide.finish" : "#tool.hint.nocollide.source";
	public override string ReloadAction => "#tool.hint.nocollide.remove";

	protected override IEnumerable<GameObject> FindConstraints( GameObject linked, GameObject target )
	{
		foreach ( var filter in linked.GetComponentsInChildren<PhysicsFilter>( true ) )
			if ( linked == target || filter.Body?.Root == target )
				yield return filter.GameObject;
	}

	protected override void CreateConstraint( SelectionPoint point1, SelectionPoint point2 )
	{
		var go = new GameObject( point1.GameObject, false, "no collide" );
		var joint = go.AddComponent<PhysicsFilter>();
		joint.Body = point2.GameObject;

		go.NetworkSpawn();

		Track( go );

		var undo = Player.Undo.Create();
		undo.Name = "#tool.name.nocollide";
		undo.Add( go );
	}
}
