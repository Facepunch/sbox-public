using Sandbox.UI;

[Hide]
[Title( "#tool.name.hoverball" )]
[Icon( "🎱" )]
[ClassName( "hoverballtool" )]
[Group( "#tool.group.building" )]
public sealed class HoverballTool : ToolMode
{
	public override IEnumerable<string> TraceIgnoreTags => ["constraint", "collision"];

	[Property, ResourceSelect( Extension = "hdef", AllowPackages = true ), Title( "#tool.name.hoverball" )]
	public string Definition { get; set; } = "entities/hoverball/hoverball.hdef";

	// the same settings as HoverballEntity, put on the placed hoverball
	[Property, Range( 0, 20 ), Title( "#tool.setting.speed" ), Description( "#tool.setting.hoverballentity.speed.description" ), Group( "#tool.setting.group.behavior" ), Order( 0 )]
	public float Speed { get; set; } = 1f;

	[Property, Range( 0, 10 ), Title( "#tool.setting.air_resistance" ), Description( "#tool.setting.hoverballentity.airresistance.description" ), Group( "#tool.setting.group.behavior" ), Order( 0 )]
	public float AirResistance { get; set; } = 0f;

	[Property, Title( "#tool.setting.is_enabled" ), Description( "#tool.setting.hoverballentity.isenabled.description" ), Group( "#tool.setting.group.behavior" ), Order( 0 )]
	public bool IsEnabled { get; set; } = true;

	[Property, Title( "#tool.setting.up" ), Description( "#tool.setting.hoverballentity.up.description" ), Group( "#tool.setting.group.inputs" ), Order( 1 )]
	public ClientInput Up { get; set; }

	[Property, Title( "#tool.setting.down" ), Description( "#tool.setting.hoverballentity.down.description" ), Group( "#tool.setting.group.inputs" ), Order( 1 )]
	public ClientInput Down { get; set; }

	[Property, Title( "#tool.setting.toggle" ), Description( "#tool.setting.hoverballentity.toggle.description" ), Group( "#tool.setting.group.inputs" ), Order( 1 )]
	public ClientInput Toggle { get; set; }

	[Property, Metadata( SoundDefinition.Hoverball ), Title( "#tool.setting.enable_sound" ), Group( "#tool.setting.group.sound" ), Order( 2 )]
	public SoundDefinition EnableSound { get; set; }

	[Property, Metadata( SoundDefinition.Hoverball ), Title( "#tool.setting.disable_sound" ), Group( "#tool.setting.group.sound" ), Order( 2 )]
	public SoundDefinition DisableSound { get; set; }

	public override string Description => "#tool.hint.hoverballtool.description";

	public override bool UseSnapGrid => true;

	protected override void OnStart()
	{
		base.OnStart();

		RegisterAction( ToolInput.Primary, () => "#tool.hint.hoverballtool.place", OnPlace );
	}

	void OnPlace()
	{
		var select = TraceSelect();
		if ( !select.IsValid() ) return;

		var hoverballDef = ResourceLibrary.Get<HoverballDefinition>( Definition );
		if ( hoverballDef == null ) return;

		var pos = select.WorldTransform();
		var placementTrans = new Transform( pos.Position );

		Spawn( select, hoverballDef.Prefab, placementTrans, WriteConfiguration<HoverballEntity>() );
		ShootEffects( select );
	}

	public override void OnControl()
	{
		base.OnControl();

		var select = TraceSelect();
		if ( !select.IsValid() ) return;

		var hoverballDef = ResourceLibrary.Get<HoverballDefinition>( Definition );
		if ( hoverballDef == null ) return;

		var pos = select.WorldTransform();
		var placementTrans = new Transform( pos.Position );

		DebugOverlay.GameObject( hoverballDef.Prefab.GetScene(), transform: placementTrans, castShadows: true, color: Color.White.WithAlpha( 0.9f ) );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	public void Spawn( SelectionPoint point, PrefabFile hoverballPrefab, Transform tx, string configuration )
	{
		if ( !FireToolAction( ToolInput.Primary ) ) return;

		if ( hoverballPrefab == null )
			return;

		var go = hoverballPrefab.GetScene().Clone( global::Transform.Zero, startEnabled: false );
		go.Tags.Add( "removable" );
		go.Tags.Add( "constraint" );
		go.WorldTransform = tx;

		var hoverball = go.GetComponent<HoverballEntity>( true );
		ApplyConfiguration( hoverball, configuration );

		if ( !point.IsWorld )
		{
			var joint = hoverball.AddComponent<FixedJoint>();
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

		var undo = Player.Undo.Create();
		undo.Name = "#tool.name.hoverball";
		undo.Icon = "🎱";
		undo.Add( go );

		CheckContraptionStats( point.GameObject );

		FirePostToolAction( ToolInput.Primary );
	}
}
