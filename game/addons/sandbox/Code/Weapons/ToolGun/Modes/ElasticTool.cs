using Sandbox.UI;

[Icon( "🌀" )]
[Title( "#tool.name.elastic" )]
[ClassName( "elastic" )]
[Group( "#tool.group.constraints" )]
public sealed class ElasticTool : BaseConstraintToolMode
{
	[Range( 0, 15 )]
	[Property, Sync, Title( "#tool.setting.frequency" )]
	public float Frequency { get; set; } = 2.0f;

	[Range( 0, 4 )]
	[Property, Sync, Title( "#tool.setting.damping" )]
	public float Damping { get; set; } = 0.1f;

	[Range( 0.5f, 5f ), Step( 0.5f )]
	[Property, Sync, Title( "#tool.setting.radius" )]
	public float Radius { get; set; } = 0.5f;

	[Property, Sync, Title( "#tool.setting.stretch_only" )]
	public bool StretchOnly { get; set; } = false;

	/// <summary>
	/// Multiplies the material's color, white leaves it as it is. Orange by default, the elastic's colour.
	/// </summary>
	[Property, Sync, Order( 101 ), Title( "#tool.setting.tint" )]
	public Color Tint { get; set; } = Color.Orange;

	/// <summary>
	/// A local material path or a workshop package ident. Sent with each elastic rather than synced.
	/// </summary>
	[Property, HideLabel, Order( 100 ), Title( "#tool.setting.material" )]
	[RopeMaterials( TintProperty = nameof( Tint ) )]
	public string MaterialPath { get; set; } = RopeLook.CableMaterial;

	protected override string WriteConstraintConfiguration() => MaterialPath;

	public override string Description => Stage == 1 ? "#tool.hint.elastic.stage1" : "#tool.hint.elastic.stage0";
	public override string PrimaryAction => Stage == 1 ? "#tool.hint.elastic.finish" : "#tool.hint.elastic.source";
	public override string ReloadAction => "#tool.hint.elastic.remove";

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
		var go1 = new GameObject( false, "elastic" );
		go1.Parent = point1.GameObject;
		go1.LocalTransform = point1.LocalTransform;
		go1.LocalRotation = Rotation.Identity;

		var go2 = new GameObject( false, "elastic" );
		go2.Parent = point2.GameObject;
		go2.LocalTransform = point2.LocalTransform;
		go2.LocalRotation = Rotation.Identity;

		var cleanup = go1.AddComponent<ConstraintCleanup>();
		cleanup.Attachment = go2;

		var len = point1.WorldPosition().Distance( point2.WorldPosition() );

		if ( point1.GameObject != point2.GameObject )
		{
			var joint = go1.AddComponent<SpringJoint>();
			joint.Body = go2;
			joint.MinLength = 0;
			joint.MaxLength = float.MaxValue;
			joint.RestLength = len;
			joint.Frequency = Frequency;
			joint.Damping = Damping;
			joint.EnableCollision = true;
			joint.ForceMode = StretchOnly ? SpringJoint.SpringForceMode.Pull : SpringJoint.SpringForceMode.Both;
		}

		var vertletRope = go1.AddComponent<VerletRope>();
		vertletRope.Attachment = go2;
		vertletRope.SegmentCount = MathX.CeilToInt( len / 16.0f );
		vertletRope.Radius = Radius;

		// the owner's material came with the create RPC
		RopeLook.AddLine( go1, go2, Configuration, Radius, Tint, splineInterpolation: 4 );

		go2.NetworkSpawn();
		go1.NetworkSpawn();

		Track( go1, go2 );

		var undo = Player.Undo.Create();
		undo.Name = "#tool.name.elastic";
		undo.Add( go1, go2 );
	}
}
