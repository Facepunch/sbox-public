using Sandbox.UI;

[Hide]
[Title( "#tool.name.wheel" )]
[Icon( "🛞" )]
[ClassName( "wheeltool" )]
[Group( "#tool.group.building" )]
public sealed class WheelTool : ToolMode
{
	public override bool UseSnapGrid => true;
	public override IEnumerable<string> TraceIgnoreTags => ["constraint", "collision"];
	[Property, ResourceSelect( Extension = "wdef", AllowPackages = true ), Title( "#tool.name.wheel" )]
	public string Definition { get; set; } = "entities/wheel/basic.wdef";

	// the same settings as WheelEntity, put on the placed wheel
	[Property, Range( 0, 1 ), Title( "#tool.setting.speed" ), Group( "#tool.setting.group.behavior" ), Order( 0 )]
	public float Speed { get; set; } = 0.5f;

	[Property, Range( 0, 1 ), Title( "#tool.setting.power" ), Group( "#tool.setting.group.behavior" ), Order( 0 )]
	public float Power { get; set; } = 0.5f;

	[Property, Title( "#tool.setting.reversed" ), Group( "#tool.setting.group.behavior" ), Order( 0 )]
	public bool Reversed { get; set; }

	[Property, Title( "#tool.setting.forward" ), Group( "#tool.setting.group.inputs" ), Order( 1 )]
	public ClientInput Forward { get; set; }

	[Property, Title( "#tool.setting.reverse" ), Group( "#tool.setting.group.inputs" ), Order( 1 )]
	public ClientInput Reverse { get; set; }

	[Property, Title( "#tool.setting.brake" ), Group( "#tool.setting.group.inputs" ), Order( 1 )]
	public ClientInput Brake { get; set; }

	[Property, Title( "#tool.setting.turn_left" ), Group( "#tool.setting.group.inputs" ), Order( 1 )]
	public ClientInput TurnLeft { get; set; }

	[Property, Title( "#tool.setting.turn_right" ), Group( "#tool.setting.group.inputs" ), Order( 1 )]
	public ClientInput TurnRight { get; set; }

	public override string Description => "#tool.hint.wheeltool.description";

	Vector3 _axis = Vector3.Right;

	protected override void OnStart()
	{
		base.OnStart();

		RegisterAction( ToolInput.Primary, () => "#tool.hint.wheeltool.place", OnPlace );
		RegisterAction( ToolInput.Secondary, () => "#tool.hint.wheeltool.toggle_axis", OnToggleAxis );
		RegisterAction( ToolInput.Reload, () => "#tool.hint.wheeltool.toggle_direction", OnToggleDirection );
	}

	void OnToggleAxis()
	{
		_axis = _axis == Vector3.Right ? Vector3.Up : Vector3.Right;
	}

	void OnToggleDirection()
	{
		Reversed = !Reversed;
	}

	void OnPlace()
	{
		var select = TraceSelect();
		if ( !select.IsValid() ) return;

		var def = ResourceLibrary.Get<WheelDefinition>( Definition );
		if ( def == null || def.Prefab?.GetScene() is not Scene scene ) return;

		var placementTrans = GetPlacementTransform( select, scene );
		SpawnWheel( select, def, placementTrans, WriteConfiguration<WheelEntity>() );
		ShootEffects( select );
	}

	Transform GetPlacementTransform( SelectionPoint select, Scene scene )
	{
		var pos = select.WorldTransform();
		var modelBounds = scene.GetBounds();
		var surfaceOffset = modelBounds.Size.y * 0.5f;

		var placementTrans = new Transform( pos.Position + pos.Rotation.Forward * surfaceOffset );
		placementTrans.Rotation = Rotation.LookAt( pos.Rotation.Forward, pos.Rotation * _axis ) * new Angles( 0, 90, 0 );
		placementTrans.Scale = scene.LocalScale;
		return placementTrans;
	}

	public override void OnControl()
	{
		base.OnControl();

		var select = TraceSelect();
		if ( !select.IsValid() ) return;

		var def = ResourceLibrary.Get<WheelDefinition>( Definition );
		if ( def == null || def.Prefab?.GetScene() is not Scene scene ) return;

		var placementTrans = GetPlacementTransform( select, scene );
		var modelBounds = scene.GetBounds();

		DebugOverlay.GameObject( scene, transform: placementTrans, castShadows: true, color: Color.White.WithAlpha( 0.9f ) );

		var overlayRadius = MathF.Max( modelBounds.Size.x, modelBounds.Size.z ) * 0.01f * placementTrans.Scale.x;
		WheelOverlay.DrawDirection( placementTrans.Position, placementTrans.Right, select.WorldTransform().Rotation.Up, overlayRadius, Reversed );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	public void SpawnWheel( SelectionPoint point, WheelDefinition def, Transform tx, string configuration )
	{
		if ( !FireToolAction( ToolInput.Primary ) ) return;

		if ( def == null || def.Prefab?.GetScene() is not Scene scene ) return;

		var wheelGo = scene.Clone( new CloneConfig { StartEnabled = false } );
		wheelGo.Name = "wheel";
		wheelGo.Tags.Add( "removable" );
		wheelGo.Tags.Add( "constraint" );
		wheelGo.WorldTransform = tx;

		var we = wheelGo.GetOrAddComponent<WheelEntity>();
		ApplyConfiguration( we, configuration );
		var joint = wheelGo.GetComponentInChildren<WheelJoint>( true );

		if ( joint is null )
		{
			var wheelAnchor = new GameObject( true, "anchor2" );
			wheelAnchor.Parent = wheelGo;
			wheelAnchor.LocalRotation = new Angles( 0, 90, 90 );

			//var joint = jointGo.AddComponent<HingeJoint>();
			joint = wheelAnchor.AddComponent<WheelJoint>();
			joint.Attachment = Joint.AttachmentMode.Auto;
			joint.EnableSuspension = true;
			joint.EnableSuspensionLimit = true;
			joint.SuspensionLimits = new Vector2( -32, 32 );
			joint.EnableCollision = false;
		}

		ApplyPhysicsProperties( wheelGo );

		joint.Body = point.GameObject;

		wheelGo.NetworkSpawn( true, null );

		Track( wheelGo );

		var undo = Player.Undo.Create();
		undo.Name = "#tool.name.wheel";
		undo.Add( wheelGo );

		CheckContraptionStats( point.GameObject );

		FirePostToolAction( ToolInput.Primary );
	}
}
