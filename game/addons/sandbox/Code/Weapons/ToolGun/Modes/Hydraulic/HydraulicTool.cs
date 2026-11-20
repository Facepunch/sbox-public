[Hide]
[Title( "#tool.name.hydraulic" )]
[Icon( "⚙️" )]
[ClassName( "HydraulicTool" )]
[Group( "#tool.group.building" )]
public sealed class HydraulicTool : BaseLengthConstraintTool
{
	public override string Description => Stage == 1 ? "#tool.hint.hydraulictool.stage1" : "#tool.hint.hydraulictool.stage0";
	public override string PrimaryAction => Stage == 1 ? "#tool.hint.hydraulictool.finish" : "#tool.hint.hydraulictool.source";
	public override string ReloadAction => "#tool.hint.hydraulictool.remove";

	// ordered after the base class's length sliders, which are listed after this, to keep the checkboxes together
	[Property, Sync, Title( "#tool.setting.ball_joints" ), Group( "#tool.setting.group.behavior" ), Order( 1 )]
	public bool BallJoints { get; set; } = false;

	protected override string CapModelA => "hydraulics/tool_hydraulic.vmdl";
	protected override string CapModelB => "hydraulics/tool_hydraulic.vmdl";
	protected override string ShaftMaterial => "hydraulics/metal_tile_line.vmat";
	protected override string UndoName => "Hydraulic";
	protected override LengthConstraintEntity.ConstraintKind Kind => LengthConstraintEntity.ConstraintKind.Hydraulic;

	protected override void CreateConstraint( SelectionPoint point1, SelectionPoint point2 )
	{
		if ( point1.GameObject == point2.GameObject )
			return;

		if ( BallJoints )
		{
			CreateBallJointHydraulic( point1, point2 );
			return;
		}

		base.CreateConstraint( point1, point2 );
	}

	private void CreateBallJointHydraulic( SelectionPoint point1, SelectionPoint point2 )
	{
		var p1 = point1.WorldPosition();
		var p2 = point2.WorldPosition();
		var len = p1.Distance( p2 );

		var dir = (p2 - p1).Normal;
		var up = MathF.Abs( Vector3.Dot( dir, Vector3.Up ) ) > 0.99f ? Vector3.Forward : Vector3.Up;
		var axis = Rotation.LookAt( dir );

		var surfaceRotA = point1.WorldTransform().Rotation;
		var surfaceRotB = point2.WorldTransform().Rotation;

		// Visual anchors — identity rotation for LineRenderer
		var goA = new GameObject( false, "bs_hydraulic_a" );
		goA.Parent = point1.GameObject;
		goA.LocalTransform = point1.LocalTransform;
		goA.LocalRotation = Rotation.Identity;
		goA.Tags.Add( "constraint" );

		var goB = new GameObject( false, "bs_hydraulic_b" );
		goB.Parent = point2.GameObject;
		goB.LocalTransform = point2.LocalTransform;
		goB.LocalRotation = Rotation.Identity;
		goB.Tags.Add( "constraint" );

		var cleanup = goA.AddComponent<ConstraintCleanup>();
		cleanup.Attachment = goB;

		// Base mount visuals — surface normal aligned
		var baseVisA = new GameObject( goA, true, "hydraulic_base_a" );
		baseVisA.LocalPosition = Vector3.Zero;
		baseVisA.WorldRotation = surfaceRotA * Rotation.FromPitch( 90f );
		baseVisA.AddComponent<ModelRenderer>().Model = Model.Load( "hydraulics/tool_suspension_base.vmdl" );

		var baseVisB = new GameObject( goB, true, "hydraulic_base_b" );
		baseVisB.LocalPosition = Vector3.Zero;
		baseVisB.WorldRotation = surfaceRotB * Rotation.FromPitch( 90f );
		baseVisB.AddComponent<ModelRenderer>().Model = Model.Load( "hydraulics/tool_suspension_base.vmdl" );

		// Ball joint visuals
		var ballVisA = new GameObject( goA, true, "hydraulic_ball_a" );
		ballVisA.WorldPosition = goA.WorldPosition + surfaceRotA.Forward * 4.644f;
		ballVisA.WorldRotation = Rotation.LookAt( -dir, up ) * Rotation.FromPitch( -90f );
		var skinA = ballVisA.AddComponent<SkinnedModelRenderer>();
		skinA.Model = Model.Load( "hydraulics/tool_balljoint.vmdl" );
		skinA.CreateBoneObjects = true;

		var ballVisB = new GameObject( goB, true, "hydraulic_ball_b" );
		ballVisB.WorldPosition = goB.WorldPosition + surfaceRotB.Forward * 4.644f;
		ballVisB.WorldRotation = Rotation.LookAt( dir, up ) * Rotation.FromPitch( -90f );
		var skinB = ballVisB.AddComponent<SkinnedModelRenderer>();
		skinB.Model = Model.Load( "hydraulics/tool_balljoint.vmdl" );
		skinB.CreateBoneObjects = true;

		// Shaft
		var lineRenderer = goA.AddComponent<LineRenderer>();
		lineRenderer.Points = [ballVisA, ballVisB];
		lineRenderer.Face = SceneLineObject.FaceMode.Cylinder;
		lineRenderer.Texturing = lineRenderer.Texturing with { Material = Material.Load( "hydraulics/metal_tile_line.vmat" ), WorldSpace = true, UnitsPerTexture = 32 };
		lineRenderer.Lighting = true;
		lineRenderer.CastShadows = true;
		lineRenderer.Width = 1.5f;
		lineRenderer.Color = Color.White;

		var aligner = goA.AddComponent<BallSocketPairEntity>();
		aligner.BallModelA = skinA;
		aligner.BallModelB = skinB;
		aligner.ShaftRenderer = lineRenderer;

		// Ball socket constraint
		var ballTarget = new GameObject( point2.GameObject, false, "bs_target" );
		ballTarget.LocalTransform = point2.LocalTransform;

		var ballAnchor = new GameObject( point1.GameObject, false, "bs_anchor" );
		ballAnchor.WorldTransform = ballTarget.WorldTransform;

		var ballJoint = ballAnchor.AddComponent<BallJoint>();
		ballJoint.Body = ballTarget;
		ballJoint.Friction = 0.0f;
		ballJoint.EnableCollision = false;

		// Slider joint
		var sliderA = new GameObject( false, "hydraulic_slider_a" );
		sliderA.Parent = point1.GameObject;
		sliderA.LocalTransform = point1.LocalTransform;
		sliderA.WorldRotation = axis;

		var sliderB = new GameObject( false, "hydraulic_slider_b" );
		sliderB.Parent = point2.GameObject;
		sliderB.LocalTransform = point2.LocalTransform;
		sliderB.WorldRotation = axis;

		var slider = sliderA.AddComponent<SliderJoint>();
		slider.Body = sliderB;
		slider.MinLength = len;
		slider.MaxLength = len;
		slider.EnableCollision = true;

		var entity = sliderA.AddComponent<LengthConstraintEntity>();
		entity.Kind = LengthConstraintEntity.ConstraintKind.Hydraulic;
		ApplyConfiguration( entity, Configuration );
		entity.MinLength = 5.0f;
		entity.MaxLength = len * 2.0f;
		entity.Joint = slider;

		sliderA.AddComponent<CapsuleCollider>();

		// The slider is the only piece with a collider, so it's what the remover tool hits. Tag it
		// removable and chain every piece through ConstraintCleanup (each destroys the next on
		// destroy, and destroys itself when the next is gone) so removing any one removes them all.
		sliderA.Tags.Add( "removable" );
		sliderA.Tags.Add( "constraint" );
		sliderA.AddComponent<ConstraintCleanup>().Attachment = goA;
		goB.AddComponent<ConstraintCleanup>().Attachment = ballAnchor;
		ballAnchor.AddComponent<ConstraintCleanup>().Attachment = ballTarget;
		ballTarget.AddComponent<ConstraintCleanup>().Attachment = sliderB;
		sliderB.AddComponent<ConstraintCleanup>().Attachment = sliderA;

		// TODO: my lord
		goB.NetworkSpawn( true, null );
		goA.NetworkSpawn( true, null );
		ballTarget.NetworkSpawn( true, null );
		ballAnchor.NetworkSpawn( true, null );
		sliderB.NetworkSpawn( true, null );
		sliderA.NetworkSpawn( true, null );

		Track( goA, goB, ballAnchor, ballTarget, sliderA, sliderB );

		var undo = Player.Undo.Create();
		undo.Name = "#tool.undo.hydraulic_ball_joints";
		undo.Add( goA, goB, ballAnchor, ballTarget, sliderA, sliderB );
	}
}

