using Sandbox.UI;

[Hide, Title( "#tool.name.camera" ), Icon( "📷" ), ClassName( "cameratool" ), Group( "#tool.group.render" )]
public sealed class CameraTool : ToolMode
{
	public const string CameraPrefab = "entities/camera/camera_entity.prefab";

	[Property, Title( "#tool.setting.camera_key" ), Description( "#tool.setting.camera_key.description" ), Group( "#tool.setting.group.inputs" )]
	public ClientInput ViewInput { get; set; } = new() { Action = "View" };

	[Property, Range( 1, 150 ), Title( "#tool.setting.camera_fov" ), Group( "#tool.setting.group.behavior" )]
	public float Fov { get; set; } = 75f;

	[Property, Title( "#tool.setting.camera_static" ), Description( "#tool.setting.camera_static.description" ), Group( "#tool.setting.group.behavior" )]
	public bool Static { get; set; }

	[Property, Title( "#tool.setting.camera_toggle" ), Description( "#tool.setting.camera_toggle.description" ), Group( "#tool.setting.group.inputs" )]
	public bool ToggleMode { get; set; } = true;

	[Property, ToggleGroup( "Tracking", Label = "#entity.group.camera_tracking" )]
	public bool Tracking { get; set; }

	[Property, Title( "#entity.property.camera_tracking_target" ), ToggleGroup( "Tracking", Label = "#entity.group.camera_tracking" )]
	public CameraTrackingTarget TrackingTarget { get; set; } = CameraTrackingTarget.Owner;

	[Property, Range( 0.1f, 20f ), Title( "#entity.property.camera_tracking_speed" ), Description( "#entity.description.camera_tracking_speed" ), ToggleGroup( "Tracking", Label = "#entity.group.camera_tracking" )]
	public float TrackingLerpSpeed { get; set; } = 5f;

	[Property, Range( 0f, 8192f ), Title( "#entity.property.camera_tracking_range" ), Description( "#entity.description.camera_tracking_range" ), ToggleGroup( "Tracking", Label = "#entity.group.camera_tracking" )]
	public float TrackingRange { get; set; } = 2048f;

	[Property, Title( "#entity.property.camera_tracking_return" ), Description( "#entity.description.camera_tracking_return" ), ToggleGroup( "Tracking", Label = "#entity.group.camera_tracking" )]
	public bool ReturnToOriginal { get; set; } = true;

	public override string Description => "#tool.hint.camera.description";

	protected override void OnStart()
	{
		base.OnStart();
		RegisterAction( ToolInput.Primary, () => "#tool.hint.camera.place", Place );
	}

	void Place()
	{
		var view = Scene.Camera.IsValid() ? Scene.Camera.View.Transform : Player.EyeTransform;
		Spawn( view, WriteConfiguration<CameraEntity>() );

		// the usual toolgun shot, without ShootEffects' beam - the camera goes where you're standing, not where you aim
		if ( Toolgun.IsValid() )
		{
			Toolgun.PlayFireAnimation();
			Sound.Play( "weapons/toolgun/sounds/shoot.sound", Toolgun.GetMuzzleTransform().Position );
		}
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	public void Spawn( Transform view, string configuration )
	{
		if ( !Player.IsValid() || !FireToolAction( ToolInput.Primary ) ) return;
		var prefab = ResourceLibrary.Get<PrefabFile>( CameraPrefab );
		if ( prefab?.GetScene() is not Scene scene ) return;
		var go = scene.Clone( new CloneConfig { StartEnabled = false } );
		var entity = go.GetComponent<CameraEntity>( true );
		if ( !entity.IsValid() ) { go.Destroy(); return; }
		ApplyConfiguration( entity, configuration );
		entity.AlignView( view );
		go.Tags.Add( "removable" );
		Ownable.Set( go, Player.Network.Owner );
		var body = go.GetComponent<Rigidbody>( true );
		if ( body.IsValid() ) body.MotionEnabled = false;
		entity.RefreshPhysics();
		go.NetworkSpawn( true, null );
		Track( go );
		var undo = Player.Undo.Create();
		undo.Name = "#tool.name.camera";
		undo.Icon = "📷";
		undo.Add( go );
		FirePostToolAction( ToolInput.Primary );
	}
}
