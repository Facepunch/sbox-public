using Sandbox.UI;

[Hide]
[Title( "#tool.name.button" )]
[Icon( "🔘" )]
[ClassName( "buttontool" )]
[Group( "#tool.group.building" )]
public sealed class ButtonTool : ToolMode
{
	public override bool UseSnapGrid => true;
	public override IEnumerable<string> TraceIgnoreTags => ["constraint", "collision"];

	[Property, ResourceSelect( Extension = "btndef", AllowPackages = true ), Title( "#tool.name.button" )]
	public string Definition { get; set; } = "entities/button/basic.btndef";

	// the same settings as ButtonEntity, put on the placed button
	[Property, Group( "#tool.setting.group.behavior" ), Title( "#tool.setting.mode" ), Order( 0 )]
	public ButtonMode Mode { get; set; } = ButtonMode.Hold;

	public override string Description => "#tool.hint.buttontool.description";

	protected override void OnStart()
	{
		base.OnStart();
		RegisterAction( ToolInput.Primary, () => "#tool.hint.buttontool.place", OnPlace );
		RegisterAction( ToolInput.Secondary, () => "#tool.hint.buttontool.place_no_weld", OnPlaceNoWeld );
	}

	void OnPlace() => Place( noWeld: false );
	void OnPlaceNoWeld() => Place( noWeld: true );

	void Place( bool noWeld )
	{
		var select = TraceSelect();
		if ( !select.IsValid() ) return;

		var definition = ResourceLibrary.Get<ButtonDefinition>( Definition );
		if ( definition?.Prefab is null ) return;

		var transform = GetPlacementTransform( select );
		Spawn( select, definition.Prefab, transform, noWeld, WriteConfiguration<ButtonEntity>() );
		ShootEffects( select );
	}

	Transform GetPlacementTransform( SelectionPoint select )
	{
		var surface = select.WorldTransform();
		return new Transform( surface.Position, surface.Rotation );
	}

	public override void OnControl()
	{
		base.OnControl();

		var select = TraceSelect();
		if ( !select.IsValid() ) return;

		var definition = ResourceLibrary.Get<ButtonDefinition>( Definition );
		if ( definition?.Prefab?.GetScene() is not Scene scene ) return;

		DebugOverlay.GameObject( scene, transform: GetPlacementTransform( select ), castShadows: true, color: Color.White.WithAlpha( 0.9f ) );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	public void Spawn( SelectionPoint point, PrefabFile buttonPrefab, Transform transform, bool noWeld, string configuration )
	{
		if ( !FireToolAction( noWeld ? ToolInput.Secondary : ToolInput.Primary ) ) return;

		if ( buttonPrefab?.GetScene() is not Scene scene ) return;

		var button = scene.Clone( new CloneConfig { StartEnabled = false } );
		button.Tags.Add( "removable" );
		button.Tags.Add( "constraint" );
		button.WorldTransform = transform;

		ApplyConfiguration( button.GetComponentInChildren<ButtonEntity>( true ), configuration );

		if ( !noWeld )
		{
			var joint = button.AddComponent<FixedJoint>();
			joint.Attachment = Joint.AttachmentMode.LocalFrames;
			joint.LocalFrame2 = point.GameObject.WorldTransform.WithScale( 1 ).ToLocal( transform );
			joint.LocalFrame1 = new Transform();
			joint.AngularFrequency = 0;
			joint.LinearFrequency = 0;
			joint.Body = point.GameObject;
			joint.EnableCollision = false;
		}

		ApplyPhysicsProperties( button );
		button.NetworkSpawn( true, null );
		Track( button );

		var undo = Player.Undo.Create();
		undo.Name = "#tool.name.button";
		undo.Icon = "🔘";
		undo.Add( button );

		CheckContraptionStats( point.GameObject );

		FirePostToolAction( noWeld ? ToolInput.Secondary : ToolInput.Primary );
	}
}
