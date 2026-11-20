/// <summary>
/// A handheld camera. CameraFeed provides its video output; this component handles weapon controls.
/// </summary>
[Title( "#weapon.name.camera" ), Icon( "videocam" )]
public sealed class CameraWeapon : BaseSandboxWeapon
{
	public CameraWeapon()
	{
		// The camera doesn't use ammo (the engine default is on, which would show an ammo indicator).
		UsesAmmo = false;
	}

	float fov = 50;
	float roll = 0;

	DepthOfField dof;
	bool focusing;
	Vector3 focusPoint;

	[RequireComponent, Hide] public CameraFeed Feed { get; set; }
	/// <summary>Upper-body hold graph, connected to the player graph's "hold_pose" slot.</summary>
	[Property, Feature( "WorldModel" )] public Sandbox.Animation.Graph HoldGraph { get; set; }

	SkinnedModelRenderer _holdBody;

	public override bool WantsHideHud => true;

	public override HudElement HiddenHudElements => HudElement.All & ~HudElement.Inventory;

	protected override void OnDisabled()
	{
		ReleaseHold();
		base.OnDisabled();

		DestroyDepthOfField();
	}

	protected override void OnDestroy()
	{
		ReleaseHold();
		DestroyDepthOfField();
	}

	protected override void OnPreRender()
	{
		if ( !Feed.IsValid() ) return;

		if ( HasOwner && Scene.Camera.IsValid() )
		{
			// When held, mirror the player's camera so the TV shows their POV.
			// TODO: network some props to the TV so they show up in the RT camera when held by a player other than the host.
			Feed.SetWorldView( Scene.Camera.View.Transform, Scene.Camera.View.FieldOfView, Scene.Camera.FovAxis, hideViewer: true );
		}
		else
		{
			Feed.UseLensView( 40f );
		}
	}

	/// <summary>
	/// We want to control the camera fov when held by a player.
	/// </summary>
	public override void OnCameraSetup( Player player, Sandbox.CameraComponent camera )
	{
		if ( !player.Network.IsOwner || !Network.IsOwner ) return;

		camera.FieldOfView = fov;
		camera.WorldRotation = camera.WorldRotation * new Angles( 0, 0, roll );
	}

	public override void OnCameraMove( Player player, ref Angles angles )
	{
		if ( Input.Down( "attack2" ) )
		{
			angles = default;
		}

		float sensitivity = fov.Remap( 1, 70, 0.01f, 1 );
		angles *= sensitivity;
	}

	protected override void OnControl()
	{
		if ( Input.Pressed( "reload" ) )
		{
			fov = 50;
			roll = 0;
		}

		if ( Input.Down( "attack2" ) )
		{
			fov += Input.AnalogLook.pitch;
			fov = fov.Clamp( 1, 150 );
			roll -= Input.AnalogLook.yaw;
		}

		EnsureDepthOfField();

		if ( dof.IsValid() )
		{
			UpdateDepthOfField( dof );
		}

		if ( focusing && Input.Released( "attack1" ) )
		{
			Game.TakeScreenshot();
			Sandbox.Services.Stats.Increment( "photos", 1 );

			PlayAttackSound();
		}

		focusing = Input.Down( "attack1" );
	}

	private void EnsureDepthOfField()
	{
		if ( dof.IsValid() ) return;

		dof = Scene.Camera.GetOrAddComponent<DepthOfField>();
		dof.Flags |= ComponentFlags.NotNetworked;
		focusing = false;
	}

	private void DestroyDepthOfField()
	{
		dof?.Destroy();
		dof = default;
	}

	private void UpdateDepthOfField( DepthOfField dof )
	{
		if ( !focusing )
		{
			dof.BlurSize = MathF.Pow( Scene.Camera.FieldOfView.Remap( 1, 55, 1, 0 ), 4 ) * 16;
			dof.FocusRange = 512;
			dof.FrontBlur = false;

			var tr = Scene.Trace.Ray( Scene.Camera.Transform.World.ForwardRay, 5000 )
								.Radius( 4 )
								.IgnoreGameObjectHierarchy( GameObject.Root )
								.Run();

			focusPoint = tr.EndPosition;
		}

		var target = Scene.Camera.WorldPosition.Distance( focusPoint ) + 64;

		dof.FocalDistance = dof.FocalDistance.LerpTo( target, Time.Delta * 2.0f );
	}

	/// <summary>
	/// Keeps the camera view clear of weapon HUD elements.
	/// </summary>
	public override void DrawHud( Painter painter, Vector2 crosshair )
	{
		// nothing!
	}

	protected override void OnHolstered()
	{
		ReleaseHold();
		base.OnHolstered();
	}

	protected override void UpdateBodyAnimations( SkinnedModelRenderer body )
	{
		base.UpdateBodyAnimations( body );

		// Connect only while the slot is empty, so this doesn't restart the pose every frame.
		var slots = body.Animation.ExternalGraphs;
		if ( !IsActive || HoldGraph is null || !slots.Contains( "hold_pose" ) || slots.IsConnected( "hold_pose" ) ) return;
		slots.Connect( "hold_pose", HoldGraph );
		_holdBody = body;
	}

	void ReleaseHold()
	{
		if ( _holdBody.IsValid() && _holdBody.Animation.ExternalGraphs.IsConnected( "hold_pose" ) )
			_holdBody.Animation.ExternalGraphs.Disconnect( "hold_pose" );
		_holdBody = null;
	}
}
