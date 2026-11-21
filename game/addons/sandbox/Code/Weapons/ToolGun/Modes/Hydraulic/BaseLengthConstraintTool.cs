
/// <summary>
/// Shared geometry for two-point length-changing constraints (Hydraulic, Muscle, Winch):
/// a shaft between two capped anchors, driven by a <see cref="SliderJoint"/>.
/// </summary>
public abstract class BaseLengthConstraintTool : BaseConstraintToolMode
{
	protected abstract string CapModelA { get; }
	protected abstract string CapModelB { get; }
	protected abstract string ShaftMaterial { get; }
	protected abstract string UndoName { get; }

	protected abstract LengthConstraintEntity.ConstraintKind Kind { get; }

	protected virtual float ShaftWidth => 2f;
	protected virtual float MinLength => 5.0f;
	protected virtual float MaxLengthMultiplier => 2.0f;

	// the same settings as LengthConstraintEntity, put on the placed constraint. Its min and max length come from where it's placed,
	// tools set their own defaults in their constructors
	[Property, Range( 0, 1 ), Title( "#tool.setting.length" ), Group( "#tool.setting.group.behavior" ), Order( 0 )]
	public float Length { get; set; } = 0.5f;

	[Property, Range( 0, 1 ), Title( "#tool.setting.speed" ), Group( "#tool.setting.group.behavior" ), Order( 0 )]
	public float Speed { get; set; } = 0.25f;

	[Property, Title( "#tool.setting.push" ), Group( "#tool.setting.group.inputs" ), Order( 1 )]
	public ClientInput Push { get; set; }

	[Property, Range( 0, 1 ), Title( "#tool.setting.push_speed" ), Group( "#tool.setting.group.behavior" ), Order( 0 )]
	public float PushSpeed { get; set; } = 0.25f;

	[Property, Title( "#tool.setting.pull" ), Group( "#tool.setting.group.inputs" ), Order( 1 )]
	public ClientInput Pull { get; set; }

	[Property, Range( 0, 1 ), Title( "#tool.setting.pull_speed" ), Group( "#tool.setting.group.behavior" ), Order( 0 )]
	public float PullSpeed { get; set; } = 0.25f;

	[Property, Title( "#tool.setting.toggle" ), Group( "#tool.setting.group.inputs" ), Order( 1 )]
	public ClientInput Toggle { get; set; }

	[Property, Title( "#tool.setting.activate" ), Description( "#tool.setting.lengthconstraintentity.activate.description" ), Group( "#tool.setting.group.inputs" ), Order( 1 )]
	public ClientInput Activate { get; set; }

	[Property, ToggleGroup( "Animated", Label = "#tool.setting.animated" ), Title( "#tool.setting.animated" ), Order( 2 )]
	public bool Animated { get; set; }

	[Property, ToggleGroup( "Animated", Label = "#tool.setting.animated" ), Range( 0, 10 ), Title( "#tool.setting.animation_speed" ), Order( 2 )]
	public float AnimationSpeed { get; set; } = 1.0f;

	[Property, ToggleGroup( "Animated", Label = "#tool.setting.animated" ), Title( "#tool.setting.ease_in" ), Order( 2 )]
	public LengthConstraintEntity.EaseType EaseIn { get; set; } = LengthConstraintEntity.EaseType.Linear;

	[Property, ToggleGroup( "Animated", Label = "#tool.setting.animated" ), Title( "#tool.setting.ease_out" ), Order( 2 )]
	public LengthConstraintEntity.EaseType EaseOut { get; set; } = LengthConstraintEntity.EaseType.Linear;

	protected override string WriteConstraintConfiguration() => WriteConfiguration<LengthConstraintEntity>();

	protected override IEnumerable<GameObject> FindConstraints( GameObject linked, GameObject target )
	{
		foreach ( var cleanup in linked.GetComponentsInChildren<ConstraintCleanup>( true ) )
		{
			if ( linked != target && cleanup.Attachment?.Root != target ) continue;
			if ( cleanup.GameObject.GetComponentInChildren<LengthConstraintEntity>() is not null )
				yield return cleanup.GameObject;
		}
	}

	protected override void CreateConstraint( SelectionPoint point1, SelectionPoint point2 )
	{
		if ( point1.GameObject == point2.GameObject )
			return;

		var line = point1.WorldPosition() - point2.WorldPosition();
		var baseName = UndoName.ToLowerInvariant();

		var go1 = new GameObject( false, $"{baseName}_a" );
		go1.Parent = point1.GameObject;
		go1.LocalTransform = point1.LocalTransform;
		go1.WorldRotation = Rotation.LookAt( -line );
		go1.Tags.Add( "constraint" );

		var go2 = new GameObject( false, $"{baseName}_b" );
		go2.Parent = point2.GameObject;
		go2.LocalTransform = point2.LocalTransform;
		go2.WorldRotation = Rotation.LookAt( -line );
		go2.Tags.Add( "constraint" );

		var cleanup = go1.AddComponent<ConstraintCleanup>();
		cleanup.Attachment = go2;

		var len = (point1.WorldPosition() - point2.WorldPosition()).Length;

		// End caps
		var capA = new GameObject( go1, true, $"{baseName}_cap_a" );
		capA.LocalPosition = Vector3.Zero;
		capA.WorldRotation = Rotation.LookAt( line ) * Rotation.FromPitch( -90f );
		capA.AddComponent<ModelRenderer>().Model = Model.Load( CapModelA );

		var capB = new GameObject( go2, true, $"{baseName}_cap_b" );
		capB.LocalPosition = Vector3.Zero;
		capB.WorldRotation = Rotation.LookAt( -line ) * Rotation.FromPitch( -90f );
		capB.AddComponent<ModelRenderer>().Model = Model.Load( CapModelB );

		// Shaft, using line renderer
		var lineRenderer = go1.AddComponent<LineRenderer>();
		lineRenderer.Points = [go1, go2];
		lineRenderer.Face = SceneLineObject.FaceMode.Cylinder;
		lineRenderer.Texturing = lineRenderer.Texturing with { Material = Material.Load( ShaftMaterial ), WorldSpace = true, UnitsPerTexture = 32 };
		lineRenderer.Lighting = true;
		lineRenderer.CastShadows = true;
		lineRenderer.Width = ShaftWidth;
		lineRenderer.Color = Color.White;

		SliderJoint joint;

		var jointGo = new GameObject( go1, true, baseName );

		// Joint
		{
			joint = jointGo.AddComponent<SliderJoint>();
			joint.Attachment = Sandbox.Joint.AttachmentMode.Auto;
			joint.Body = go2;
			joint.MinLength = len;
			joint.MaxLength = len;
			joint.EnableCollision = true;
		}

		var entity = jointGo.AddComponent<LengthConstraintEntity>();
		entity.Kind = Kind;
		ApplyConfiguration( entity, Configuration );
		entity.MinLength = MinLength;
		entity.MaxLength = len * MaxLengthMultiplier;
		entity.Joint = joint;

		jointGo.AddComponent<CapsuleCollider>();

		// The joint is the part with a collider, so it's what the remover tool hits. Tag it removable
		// and tie it to the anchors, so removing the shaft takes the caps and anchors with it (and the
		// joint goes when its anchor does).
		jointGo.Tags.Add( "removable" );
		jointGo.Tags.Add( "constraint" );
		jointGo.AddComponent<ConstraintCleanup>().Attachment = go1;

		go2.NetworkSpawn( true, null );
		go1.NetworkSpawn( true, null );
		jointGo.NetworkSpawn( true, null );

		Track( go1, go2, jointGo );

		var undo = Player.Undo.Create();
		undo.Name = TypeDescription.Title;
		undo.Add( go1, go2, jointGo );
	}

}
