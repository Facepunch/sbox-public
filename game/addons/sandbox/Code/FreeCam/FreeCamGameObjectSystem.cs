using Sandbox.Utility;
using static Sandbox.Component;

internal sealed class FreeCamGameObjectSystem : GameObjectSystem<FreeCamGameObjectSystem>, ISceneStage
{
	public bool IsActive { get; set; }

	Vector3 position;
	Vector3 smoothPosition;

	Angles angles;
	Angles smoothAngles;

	float fov = 80;
	float smoothFov = 80;

	GameObject _ui;

	public FreeCamGameObjectSystem( Scene scene ) : base( scene )
	{
	}

	void ISceneStage.Start()
	{
		if ( !IsActive )
			return;

		Input.Suppressed = true;
	}

	void StartFreeCamMode()
	{
		if ( !_ui.IsValid() )
		{
			_ui = new GameObject( true, "freecam_overlay" );
			_ui.Flags = GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
			var fc = _ui.AddComponent<FreeCamOverlay>();
			var sp = _ui.AddComponent<ScreenPanel>();
		}

		if ( _ui.IsValid() )
		{
			_ui.Enabled = true;
		}

		smoothPosition = Scene.Camera.WorldPosition;
		position = smoothPosition + Scene.Camera.WorldRotation.Backward * 50;
		angles = smoothAngles = Scene.Camera.WorldRotation;
		smoothFov = fov = Scene.Camera.FieldOfView;

		Scene.Camera.RenderExcludeTags.Add( "firstperson" );
		Scene.Camera.RenderExcludeTags.Add( "ui" );
	}

	void EndFreeCamMode()
	{
		if ( _ui.IsValid() )
		{
			_ui.Enabled = false;
		}

		Scene.TimeScale = 1;
		Scene.Camera.RenderExcludeTags.Remove( "firstperson" );
		Scene.Camera.RenderExcludeTags.Remove( "ui" );
	}

	void ISceneStage.End()
	{
		if ( IsActive )
		{
			Input.Suppressed = false;
		}

		if ( Input.Keyboard.Pressed( "J" ) )
		{
			IsActive = !IsActive;

			if ( IsActive )
			{
				StartFreeCamMode();
			}
			else
			{
				EndFreeCamMode();
			}
		}

		if ( !IsActive )
			return;

		if ( _ui.IsValid() )
		{
			var fc = _ui.GetOrAddComponent<FreeCamOverlay>();
			fc.Update( Input.Down( "score" ) );

			UpdateCameraPosition( fc );
		}
	}

	void UpdateCameraPosition( FreeCamOverlay overlay )
	{
		var speed = 50;
		if ( Input.Down( "duck" ) ) speed = 5;
		if ( Input.Down( "run" ) ) speed = 300;

		if ( Input.Down( "attack2" ) )
		{
			fov += Input.MouseDelta.y * 0.1f;
			fov = fov.Clamp( 1, 120 );
		}
		else
		{
			angles += Input.AnalogLook * fov.Remap( 1, 100, 0.1f, 1 );
		}

		var velocity = angles.ToRotation() * Input.AnalogMove * speed;
		position += velocity * RealTime.SmoothDelta;

		smoothPosition = smoothPosition.LerpTo( position, MathF.Pow( RealTime.SmoothDelta * 16.0f, overlay.CameraSmoothing * 2.0f ) );
		Scene.Camera.WorldPosition = smoothPosition;

		smoothAngles = smoothAngles.LerpTo( angles, MathF.Pow( RealTime.SmoothDelta * 16.0f, overlay.CameraSmoothing * 2.0f ) );
		Scene.Camera.WorldRotation = smoothAngles + new Angles( Noise.Fbm( 2, Time.Now * 40, 1 ) * 2, Noise.Fbm( 2, Time.Now * 30, 60 ) * 2, 0 );

		smoothFov = smoothFov.LerpTo( fov, RealTime.SmoothDelta * 20.0f );
		Scene.Camera.FieldOfView = smoothFov;

		Scene.Camera.RenderExcludeTags.Remove( "viewer" );
	}
}
