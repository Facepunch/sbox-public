
[Hide]
[Title( "#tool.name.motor" )]
[Icon( "🔄" )]
[ClassName( "MotorTool" )]
[Group( "#tool.group.constraints" )]
public sealed class MotorTool : BaseConstraintToolMode
{
	public override string Description => Stage == 1 ? "#tool.hint.motortool.stage1" : "#tool.hint.motortool.stage0";
	public override string PrimaryAction => Stage == 1 ? "#tool.hint.motortool.finish" : "#tool.hint.motortool.source";
	public override string ReloadAction => "#tool.hint.motortool.remove";

	// the same settings as MotorEntity, put on the placed motor
	[Property, Range( 0, 1 ), Title( "#tool.setting.speed" ), Group( "#tool.setting.group.behavior" ), Order( 0 )]
	public float Speed { get; set; } = 0.5f;

	[Property, Range( 0, 1 ), Title( "#tool.setting.torque" ), Group( "#tool.setting.group.behavior" ), Order( 0 )]
	public float Torque { get; set; } = 0.5f;

	/// <summary>
	/// The spin axis is the surface normal where you make the first selection - this flips it
	/// 180 degrees, which is only useful in combination with <see cref="Reversed"/> and a limit range.
	/// </summary>
	[Property, Sync, Title( "#tool.setting.flip_axis" ), Group( "#tool.setting.group.behavior" ), Order( 0 )]
	public bool FlipAxis { get; set; } = false;

	[Property, Sync, Title( "#tool.setting.reversed" ), Group( "#tool.setting.group.behavior" ), Order( 0 )]
	public bool Reversed { get; set; } = false;

	[Property, Title( "#tool.setting.start_active" ), Description( "#tool.setting.motorentity.startactive.description" ), Group( "#tool.setting.group.behavior" ), Order( 0 )]
	public bool StartActive { get; set; } = true;

	[Property, ToggleGroup( "Limited", Label = "#tool.setting.limited" ), Title( "#tool.setting.limited" ), Order( 2 )]
	public bool Limited { get; set; }

	[Property, ToggleGroup( "Limited", Label = "#tool.setting.limited" ), Range( -180, 180 ), Title( "#tool.setting.min_angle" ), Order( 2 )]
	public float MinAngle { get; set; } = -90f;

	[Property, ToggleGroup( "Limited", Label = "#tool.setting.limited" ), Range( -180, 180 ), Title( "#tool.setting.max_angle" ), Order( 2 )]
	public float MaxAngle { get; set; } = 90f;

	[Property, Title( "#tool.setting.forward" ), Description( "#tool.setting.motorentity.forward.description" ), Group( "#tool.setting.group.inputs" ), Order( 1 )]
	public ClientInput Forward { get; set; }

	[Property, Title( "#tool.setting.reverse" ), Description( "#tool.setting.motorentity.reverse.description" ), Group( "#tool.setting.group.inputs" ), Order( 1 )]
	public ClientInput Reverse { get; set; }

	[Property, Title( "#tool.setting.toggle" ), Group( "#tool.setting.group.inputs" ), Order( 1 )]
	public ClientInput Toggle { get; set; }

	protected override string WriteConstraintConfiguration() => WriteConfiguration<MotorEntity>();

	protected override IEnumerable<GameObject> FindConstraints( GameObject linked, GameObject target )
	{
		foreach ( var joint in linked.GetComponentsInChildren<HingeJoint>( true ) )
			if ( joint.GetComponent<MotorEntity>() is not null && (linked == target || joint.Body?.Root == target) )
				yield return joint.GameObject;
	}

	protected override void CreateConstraint( SelectionPoint point1, SelectionPoint point2 )
	{
		if ( point1.GameObject == point2.GameObject )
			return;

		// The hinge spins around its anchor's local Up - build a rotation whose Up matches the
		// normal at the first click (i.e. point at the surface you want to spin around).
		var normal = point1.WorldTransform().Rotation.Forward * (FlipAxis ? -1f : 1f);
		var reference = MathF.Abs( normal.Dot( Vector3.Up ) ) > 0.99f ? Vector3.Forward : Vector3.Up;
		var axisForward = Vector3.Cross( reference, normal ).Normal;
		var axisRotation = Rotation.LookAt( axisForward, normal );

		var go1 = new GameObject( false, "motor_a" );
		go1.Parent = point1.GameObject;
		go1.WorldPosition = point1.WorldPosition();
		go1.WorldRotation = axisRotation;
		go1.Tags.Add( "constraint" );

		var go2 = new GameObject( false, "motor_b" );
		go2.Parent = point2.GameObject;
		go2.WorldTransform = go1.WorldTransform;
		go2.Tags.Add( "constraint" );

		var cleanup = go1.AddComponent<ConstraintCleanup>();
		cleanup.Attachment = go2;

		var joint = go1.AddComponent<HingeJoint>();
		joint.Attachment = Sandbox.Joint.AttachmentMode.Auto;
		joint.Body = go2;
		joint.EnableCollision = false;

		var entity = go1.AddComponent<MotorEntity>();
		ApplyConfiguration( entity, Configuration );
		entity.Joint = joint;

		go2.NetworkSpawn( true, null );
		go1.NetworkSpawn( true, null );

		Track( go1, go2 );

		var undo = Player.Undo.Create();
		undo.Name = "#tool.name.motor";
		undo.Add( go1, go2 );

		CheckContraptionStats( point1.GameObject );
	}
}
