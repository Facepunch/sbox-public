using Sandbox.UI;

[Hide]
[Title( "#tool.name.thruster" )]
[Icon( "🚀" )]
[ClassName( "thrustertool" )]
[Group( "#tool.group.building" )]
public sealed class ThrusterTool : ToolMode
{
	public override bool UseSnapGrid => true;
	public override IEnumerable<string> TraceIgnoreTags => ["constraint", "collision"];

	[Property, ResourceSelect( Extension = "tdef", AllowPackages = true ), Title( "#tool.name.thruster" )]
	public string Definition { get; set; } = "entities/thruster/basic.tdef";

	// the same settings as ThrusterEntity, put on the placed thruster. Power and Invert are left to each thruster's prefab
	[Property, Title( "#tool.setting.hide_effects" ), Group( "#tool.setting.group.behavior" ), Order( 0 )]
	public bool HideEffects { get; set; } = false;

	[Property, Title( "#tool.setting.activate" ), Description( "#tool.setting.thrusterentity.activate.description" ), Group( "#tool.setting.group.inputs" ), Order( 1 )]
	public ClientInput Activate { get; set; }

	[Property, Title( "#tool.setting.reverse" ), Description( "#tool.setting.thrusterentity.reverse.description" ), Group( "#tool.setting.group.inputs" ), Order( 1 )]
	public ClientInput Reverse { get; set; }

	[Property, Metadata( SoundDefinition.Thruster ), Group( "#tool.setting.group.sound" ), Title( "#tool.setting.thruster_sound" ), Description( "#tool.setting.thrusterentity.thrustersound.description" ), Order( 2 )]
	public SoundDefinition ThrusterSound { get; set; }

	public override string Description => "#tool.hint.thrustertool.description";

	Vector3 _axis = Vector3.Up;

	protected override void OnStart()
	{
		base.OnStart();

		RegisterAction( ToolInput.Primary, () => "#tool.hint.thrustertool.place", OnPlace );
		RegisterAction( ToolInput.Secondary, () => "#tool.hint.thrustertool.place_no_weld", OnPlaceNoWeld );
		RegisterAction( ToolInput.Reload, () => "#tool.hint.thrustertool.toggle_axis", OnToggleAxis );
	}

	void OnToggleAxis()
	{
		_axis = _axis == Vector3.Right ? Vector3.Up : Vector3.Right;
	}

	void OnPlace()
	{
		var select = TraceSelect();
		if ( !select.IsValid() ) return;

		var thrusterDef = ResourceLibrary.Get<ThrusterDefinition>( Definition );
		if ( thrusterDef == null ) return;

		var placementTrans = GetPlacementTransform( select );
		Spawn( select, thrusterDef.Prefab, placementTrans, false, WriteConfiguration<ThrusterEntity>() );
		ShootEffects( select );
	}

	void OnPlaceNoWeld()
	{
		var select = TraceSelect();
		if ( !select.IsValid() ) return;

		var thrusterDef = ResourceLibrary.Get<ThrusterDefinition>( Definition );
		if ( thrusterDef == null ) return;

		var placementTrans = GetPlacementTransform( select );
		Spawn( select, thrusterDef.Prefab, placementTrans, true, WriteConfiguration<ThrusterEntity>() );
		ShootEffects( select );
	}

	Transform GetPlacementTransform( SelectionPoint select )
	{
		var pos = select.WorldTransform();
		var axisOffset = _axis == Vector3.Up ? new Angles( 90, 0, 0 ) : new Angles( -90, 0, 0 );

		var placementTrans = new Transform( pos.Position );
		placementTrans.Rotation = pos.Rotation * axisOffset;
		return placementTrans;
	}

	public override void OnControl()
	{
		base.OnControl();

		var select = TraceSelect();
		if ( !select.IsValid() ) return;

		var thrusterDef = ResourceLibrary.Get<ThrusterDefinition>( Definition );
		if ( thrusterDef == null ) return;

		var placementTrans = GetPlacementTransform( select );

		DebugOverlay.GameObject( thrusterDef.Prefab.GetScene(), transform: placementTrans, castShadows: true, color: Color.White.WithAlpha( 0.9f ) );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	public void Spawn( SelectionPoint point, PrefabFile thrusterPrefab, Transform tx, bool noWeld, string configuration )
	{
		if ( !FireToolAction( noWeld ? ToolInput.Secondary : ToolInput.Primary ) ) return;

		if ( thrusterPrefab == null )
			return;

		var go = thrusterPrefab.GetScene().Clone();
		go.Tags.Add( "removable" );
		go.Tags.Add( "constraint" );
		go.WorldTransform = tx;

		var thruster = go.GetComponent<ThrusterEntity>();
		ApplyConfiguration( thruster, configuration );

		if ( !noWeld && !point.GameObject.Tags.Contains( "world" ) )
		{
			// attach it
			var joint = thruster.AddComponent<FixedJoint>();
			joint.Attachment = Joint.AttachmentMode.LocalFrames;
			joint.LocalFrame2 = point.GameObject.WorldTransform.WithScale( 1 ).ToLocal( tx );
			joint.LocalFrame1 = new Transform();
			joint.AngularFrequency = 0;
			joint.LinearFrequency = 0;
			joint.Body = point.GameObject;
			joint.EnableCollision = false;
		}

		ApplyPhysicsProperties( go );

		go.NetworkSpawn( true, null );

		Track( go );

		// undo
		{
			var undo = Player.Undo.Create();
			undo.Name = "#tool.name.thruster";
			undo.Icon = "🚀";
			undo.Add( go );
		}

		CheckContraptionStats( point.GameObject );

		FirePostToolAction( noWeld ? ToolInput.Secondary : ToolInput.Primary );
	}

}
