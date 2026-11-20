using Sandbox.UI;

[Icon( "➖" )]
[Title( "#tool.name.slider" )]
[ClassName( "slider" )]
[Group( "#tool.group.constraints" )]
public sealed class SliderTool : BaseConstraintToolMode
{
	/// <summary>
	/// How hard it resists sliding. Zero slides freely.
	/// </summary>
	[Range( 0, 100 )]
	[Property, Sync, Title( "#tool.setting.friction" ), Description( "#tool.setting.slider_friction.description" )]
	public float Friction { get; set; } = 0f;

	[Range( 0.5f, 5f ), Step( 0.5f )]
	[Property, Sync, Title( "#tool.setting.radius" )]
	public float Radius { get; set; } = 2f;

	[Property, Sync, Title( "#tool.setting.enable_collision" )]
	public bool EnableCollision { get; set; } = true;

	/// <summary>
	/// Keep it between the two points you clicked (or the lengths below). Off, it slides along the whole axis.
	/// </summary>
	[Property, Sync, ToggleGroup( "Limited", Label = "#tool.setting.limited" ), Title( "#tool.setting.limited" ), Order( 1 )]
	public bool Limited { get; set; } = true;

	/// <summary>
	/// How close together the two points can slide, in units.
	/// </summary>
	[Property, Sync, ToggleGroup( "Limited", Label = "#tool.setting.limited" ), Range( 0, 1024 ), Title( "#tool.setting.min_length" ), Order( 1 )]
	public float MinLength { get; set; } = 0f;

	/// <summary>
	/// How far apart they can slide, in units. Zero is as far apart as you clicked them.
	/// </summary>
	[Property, Sync, ToggleGroup( "Limited", Label = "#tool.setting.limited" ), Range( 0, 1024 ), Title( "#tool.setting.max_length" ), Description( "#tool.setting.slider_max_length.description" ), Order( 1 )]
	public float MaxLength { get; set; } = 0f;

	/// <summary>
	/// Multiplies the material's color, white leaves it as it is.
	/// </summary>
	[Property, Sync, Order( 101 ), Title( "#tool.setting.tint" )]
	public Color Tint { get; set; } = Color.White;

	/// <summary>
	/// A local material path or a workshop package ident. Sent with each slider rather than synced.
	/// </summary>
	[Property, HideLabel, Order( 100 ), Title( "#tool.setting.material" )]
	[RopeMaterials( TintProperty = nameof( Tint ) )]
	public string MaterialPath { get; set; } = RopeLook.CableMaterial;

	protected override string WriteConstraintConfiguration() => MaterialPath;

	public override string Description => Stage == 1 ? "#tool.hint.slider.stage1" : "#tool.hint.slider.stage0";
	public override string PrimaryAction => Stage == 1 ? "#tool.hint.slider.finish" : "#tool.hint.slider.source";
	public override string SecondaryAction => Stage == 1 ? "#tool.hint.slider.secondary.stage1" : "#tool.hint.slider.secondary";
	public override string ReloadAction => "#tool.hint.slider.remove";

	protected override IEnumerable<GameObject> FindConstraints( GameObject linked, GameObject target )
	{
		foreach ( var joint in linked.GetComponentsInChildren<SliderJoint>( true ) )
			if ( linked == target || joint.Body?.Root == target )
				yield return joint.GameObject;
	}

	protected override SelectionPoint? GetSecondaryPoint( SelectionPoint select )
	{
		return TraceFromRay( select.WorldTransform().ForwardRay, 4096, select.GameObject );
	}

	protected override void CreateConstraint( SelectionPoint point1, SelectionPoint point2 )
	{
		if ( point1.GameObject == point2.GameObject )
			return;

		var axis = Rotation.LookAt( Vector3.Direction( point1.WorldPosition(), point2.WorldPosition() ) );

		var go1 = new GameObject( false, "slider" );
		go1.Parent = point1.GameObject;
		go1.LocalTransform = point1.LocalTransform;
		go1.WorldRotation = axis;

		var go2 = new GameObject( false, "slider" );
		go2.Parent = point2.GameObject;
		go2.LocalTransform = point2.LocalTransform;
		go2.WorldRotation = axis;

		var cleanup = go1.AddComponent<ConstraintCleanup>();
		cleanup.Attachment = go2;

		var len = point1.WorldPosition().Distance( point2.WorldPosition() );

		var joint = go1.AddComponent<SliderJoint>();
		joint.Body = go2;
		joint.EnableCollision = EnableCollision;
		joint.Friction = float.IsFinite( Friction ) ? Friction.Clamp( 0f, 100f ) : 0f;

		if ( Limited )
		{
			// zero max is as far apart as you clicked them
			var max = MaxLength > 0f && float.IsFinite( MaxLength ) ? MaxLength : len;
			var min = float.IsFinite( MinLength ) ? MinLength.Clamp( 0f, max ) : 0f;
			joint.MinLength = min;
			joint.MaxLength = max;
		}
		else
		{
			joint.LimitEnabled = false;
		}

		// a straight line, the owner's material came with the create RPC
		RopeLook.AddLine( go1, go2, Configuration, Radius, Tint, splineInterpolation: 0 );

		go2.NetworkSpawn();
		go1.NetworkSpawn();

		Track( go1, go2 );

		var undo = Player.Undo.Create();
		undo.Name = "#tool.name.slider";
		undo.Add( go1, go2 );
	}
}
