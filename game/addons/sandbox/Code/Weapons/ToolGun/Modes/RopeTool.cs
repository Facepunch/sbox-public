using Sandbox.UI;

[Icon( "🐍" )]
[Title( "#tool.name.rope" )]
[ClassName( "rope" )]
[Group( "#tool.group.constraints" )]
public sealed class RopeTool : BaseConstraintToolMode
{
	[Range( -500, 500 )]
	[Property, Sync, Title( "#tool.setting.slack" )]
	public float Slack { get; set; } = 0.0f;

	[Range( 0.5f, 5f ), Step( 0.5f )]
	[Property, Sync, Title( "#tool.setting.radius" )]
	public float Radius { get; set; } = 1f;

	[Property, Sync, Title( "#tool.setting.rigid" )]
	public bool Rigid { get; set; } = false;

	/// <summary>
	/// Multiplies the material's color, white leaves it as it is.
	/// </summary>
	[Property, Sync, Order( 101 ), Title( "#tool.setting.tint" )]
	public Color Tint { get; set; } = Color.White;

	/// <summary>
	/// A local material path or a workshop package ident. Sent with each rope rather than synced.
	/// </summary>
	[Property, HideLabel, Order( 100 ), Title( "#tool.setting.material" )]
	[RopeMaterials( TintProperty = nameof( Tint ) )]
	public string MaterialPath { get; set; } = RopeLook.RopeMaterial;

	public override string Description => Stage == 1 ? "#tool.hint.rope.stage1" : "#tool.hint.rope.stage0";
	public override string PrimaryAction => Stage == 1 ? "#tool.hint.rope.finish" : "#tool.hint.rope.source";
	public override string ReloadAction => "#tool.hint.rope.remove";

	public override bool CanConstraintToSelf => true;

	protected override string WriteConstraintConfiguration() => MaterialPath;

	protected override IEnumerable<GameObject> FindConstraints( GameObject linked, GameObject target )
	{
		foreach ( var cleanup in linked.GetComponentsInChildren<ConstraintCleanup>( true ) )
		{
			if ( linked != target && cleanup.Attachment?.Root != target ) continue;
			var go = cleanup.GameObject;
			if ( go.GetComponent<SpringJoint>() is not null || go.GetComponent<VerletRope>() is not null )
				yield return go;
		}
	}

	protected override void CreateConstraint( SelectionPoint point1, SelectionPoint point2 )
	{
		var go1 = new GameObject( false, "rope" );
		go1.Parent = point1.GameObject;
		go1.LocalTransform = point1.LocalTransform;
		go1.LocalRotation = Rotation.Identity;

		var go2 = new GameObject( false, "rope" );
		go2.Parent = point2.GameObject;
		go2.LocalTransform = point2.LocalTransform;
		go2.LocalRotation = Rotation.Identity;

		var len = point1.WorldPosition().Distance( point2.WorldPosition() );
		len = MathF.Max( 1.0f, len + Slack );

		var cleanup = go1.AddComponent<ConstraintCleanup>();
		cleanup.Attachment = go2;

		//
		// If it's ourself - we want to create the rope, but no joint between
		//
		if ( point1.GameObject != point2.GameObject )
		{
			var joint = go1.AddComponent<SpringJoint>();
			joint.Body = go2;
			joint.MinLength = Rigid ? len : 0;
			joint.MaxLength = len;
			joint.RestLength = len;
			joint.Frequency = 0;
			joint.Damping = 0;
			joint.EnableCollision = true;
		}

		var splineInterpolation = 0;
		if ( !Rigid )
		{
			var vertletRope = go1.AddComponent<VerletRope>();
			vertletRope.Attachment = go2;

			const int maxSegmentCount = 48;
			// Maximum segment count, so long ropes don't exceed computation limits
			int segmentCount = Math.Min( maxSegmentCount, MathX.CeilToInt( len / 16f ) );

			vertletRope.SegmentCount = segmentCount;
			vertletRope.Radius = Radius;
			splineInterpolation = segmentCount > maxSegmentCount ? 8 : 4;
		}

		// the owner's pick came with the create RPC
		RopeLook.AddLine( go1, go2, Configuration, Radius, Tint, splineInterpolation );

		go2.NetworkSpawn( true, null );
		go1.NetworkSpawn( true, null );

		Track( go1, go2 );

		var undo = Player.Undo.Create();
		undo.Name = "#tool.name.rope";
		undo.Add( go1, go2 );
	}
}
