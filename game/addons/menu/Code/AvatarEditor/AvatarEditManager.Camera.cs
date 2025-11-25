using Sandbox;

public sealed partial class AvatarEditManager
{
	float _center = 35f;
	Angles _angle = new Angles( 0, 180, 0 );

	void UpdateEyes( SkinnedModelRenderer renderer )
	{
		var headTransform = renderer.GetAttachment( "eyes" ) ?? renderer.WorldTransform;

		//renderer.WorldRotation = new Angles( 0, Yaw, 0 );

		// cursor position
		var ray = Scene.Camera.ScreenPixelToRay( Mouse.Position );


		var eyePos = headTransform.Position;
		var distanceToCamera = (eyePos + renderer.WorldRotation.Forward * 20.0f).Distance( ray.Position );
		var targetPos = ray.Project( distanceToCamera );

		if ( distance < 100 )
		{
			targetPos = eyePos + new Vector3( 1000, -500, 0 );
		}

		renderer.SetLookDirection( "aim_eyes", targetPos - eyePos, 1 );
		renderer.SetLookDirection( "aim_body", targetPos - eyePos, 0.5f );
		renderer.SetLookDirection( "aim_head", targetPos - eyePos, 0.8f );
	}

	//float _fovVelocity;
	float distance = 200;
	float _zoomvelocity;
	Vector2 _mousePos;
	float offsetLeftRight = 0;

	void UpdateCamera( SkinnedModelRenderer renderer )
	{
		bool rotating = false;

		if ( Input.Down( "attack1" ) )
		{
			_angle.yaw += Mouse.Delta.x * 0.4f;
			_angle.pitch -= Mouse.Delta.y * 0.2f;
			rotating = true;
		}

		_angle.pitch = _angle.pitch.Clamp( -70, 70 );
		_angle.roll = 0;

		bool moveView = false;
		var mousePos = Mouse.Position;

		var preRay = Scene.Camera.ScreenPixelToRay( _mousePos );

		_mousePos = mousePos;

		if ( Input.Down( "attack2" ) )
		{

			moveView = Mouse.Delta != 0.0f;

			offsetLeftRight += Mouse.Delta.x;
			offsetLeftRight = offsetLeftRight.Clamp( -1000, 1000 );
		}

		if ( Input.MouseWheel.y != 0 )
		{
			_zoomvelocity += Input.MouseWheel.y * -30f;
			moveView = true;

		}

		distance += _zoomvelocity * Time.Delta;
		distance = distance.Clamp( 0, 200 );
		_zoomvelocity = _zoomvelocity.LerpTo( 0, Time.Delta * 3.0f );

		var sideOffset = offsetLeftRight * _angle.ToRotation().Left * 0.05f;

		Scene.Camera.WorldRotation = _angle;
		Scene.Camera.FieldOfView = float.Lerp( 6, 50, distance.Remap( 0, 150 ) );
		Scene.Camera.WorldPosition = (Vector3.Up * _center) + _angle.Forward * -distance.Clamp( 50, 200 ) + sideOffset;

		if ( moveView && !rotating )
		{
			var postRay = Scene.Camera.ScreenPixelToRay( mousePos );
			var plane = new Plane( 0, _angle.Forward * -1 );
			var hitDelta = plane.Trace( preRay ) - plane.Trace( postRay );
			if ( hitDelta.HasValue )
			{
				_center += hitDelta.Value.z;
				_center = _center.Clamp( 0, 70 );
			}
		}

		Scene.Camera.WorldPosition = (Vector3.Up * _center) + _angle.Forward * -distance.Clamp( 50, 200 ) + sideOffset;

		var dof = Scene.Camera.GetComponent<DepthOfField>();
		if ( dof is not null )
		{
			dof.FocalDistance = distance + 32;
			dof.BlurSize = distance.Remap( 200, 0, 16, 128 );
		}
	}


}
