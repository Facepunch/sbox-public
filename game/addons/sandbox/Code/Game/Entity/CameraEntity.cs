/// <summary>Standalone camera view controls and physics. CameraFeed provides the shared video output.</summary>
[Title( "#entity.name.camera" ), Icon( "videocam" )]
public sealed partial class CameraEntity : Component, ICameraModifier, IPhysgunEvent, ICarryEvent
{
	[Property, ClientEditable, Title( "#tool.setting.camera_key" ), Description( "#tool.setting.camera_key.description" ), Group( "#tool.setting.group.inputs" )]
	public ClientInput ViewInput { get; set; } = new();

	[Property, ClientEditable, Range( 1, 150 ), Title( "#tool.setting.camera_fov" ), Group( "#tool.setting.group.behavior" )]
	public float Fov { get; set; } = 75f;

	[Property, ClientEditable, Title( "#tool.setting.camera_static" ), Description( "#tool.setting.camera_static.description" ), Group( "#tool.setting.group.behavior" )]
	public bool Static { get; set; }

	[Property, ClientEditable, Title( "#tool.setting.camera_toggle" ), Description( "#tool.setting.camera_toggle.description" ), Group( "#tool.setting.group.inputs" )]
	public bool ToggleMode { get; set; } = true;

	[RequireComponent, Hide] public CameraFeed Feed { get; set; }
	public Transform ViewTransform => Tracking && _trackingInitialized ? _trackingView : PhysicalView;
	public float ClampedFov => float.IsFinite( Fov ) ? Fov.Clamp( 1f, 150f ) : 75f;
	internal long ViewOrder { get; private set; }
	static long _nextViewOrder;
	bool? _appliedStatic;

	public bool IsLocallyOwned => GetComponent<Ownable>()?.Owner is { } owner && owner == Connection.Local;

	protected override void OnEnabled()
	{
		GameObject.Tags.Add( "camera_entity" );
		ViewOrder = ++_nextViewOrder;
		ResetTracking();
		RefreshPhysics();
		if ( Scene == Game.ActiveScene ) CameraViewSystem.GetOrCreate( Scene );
	}

	protected override void OnUpdate()
	{
		if ( Scene == Game.ActiveScene ) CameraViewSystem.GetOrCreate( Scene );
		var body = GetComponent<Rigidbody>( true );
		var collider = GetComponent<ModelCollider>( true );
		if ( _appliedStatic != Static || Static && (body.IsValid() && (body.Enabled || body.MotionEnabled)
			|| collider.IsValid() && !collider.Static) ) RefreshPhysics();
		UpdateTracking();
		UpdateEffects();
	}

	protected override void OnPreRender()
	{
		if ( !Feed.IsValid() ) return;
		if ( Tracking && _trackingInitialized && !Head.IsValid() )
			Feed.SetWorldView( _trackingView, ViewFov, CameraComponent.Axis.Horizontal, hideViewer: false );
		else
			Feed.UseLensView( ViewFov );
	}

	public void RefreshPhysics()
	{
		var collider = GetComponent<ModelCollider>( true );
		var body = GetComponent<Rigidbody>( true );
		if ( collider.IsValid() ) collider.Static = Static;
		if ( body.IsValid() )
		{
			if ( Static )
			{
				body.MotionEnabled = false;
				body.Enabled = false;
			}
			else if ( _appliedStatic == true || !body.Enabled )
			{
				body.Enabled = true;
				body.MotionEnabled = false;
			}
		}
		_appliedStatic = Static;
	}

	/// <summary>Align the lens, rather than the model origin, with the captured player view.</summary>
	public void AlignView( Transform view )
	{
		ResetTracking();
		var lens = Feed?.Camera;
		var local = lens.IsValid() ? WorldTransform.ToLocal( lens.WorldTransform ) : global::Transform.Zero;
		var rotation = view.Rotation * local.Rotation.Inverse;
		WorldTransform = WorldTransform.WithPosition( view.Position - rotation * (local.Position * WorldScale) ).WithRotation( rotation );
	}

	int ICameraModifier.CameraOrder => 1000;
	void ICameraModifier.ModifyCamera( CameraComponent camera, ref CameraView view )
	{
		if ( Scene != Game.ActiveScene || camera != Scene.Camera ) return;
		CameraViewSystem.Get( Scene )?.ApplyView( this, camera, ref view );
	}

	void IPhysgunEvent.OnPhysgunGrab( IPhysgunEvent.GrabEvent e )
	{
		if ( Static ) e.Cancelled = true;
	}

	void ICarryEvent.OnCarryGrab( PlayerCarryEvent e )
	{
		if ( Static ) e.Cancelled = true;
	}

	protected override void OnDisabled()
	{
		ResetTracking();
		LensEffects.Clear();
		if ( Scene == Game.ActiveScene ) CameraViewSystem.Get( Scene )?.Release( this );
	}

	protected override void OnDestroy()
	{
		LensEffects.Clear();
		if ( Scene == Game.ActiveScene ) CameraViewSystem.Get( Scene )?.Release( this );
	}
}
