namespace Sandbox.Movement;

partial class MoveMode
{
	protected virtual void OnUpdateAnimationGraph( SkinnedModelRenderer renderer )
	{
		if ( !renderer.Animation.IsValid )
			return;

		var parameters = renderer.Animation.Parameters;
		var eye = Controller.EyeTransform;
		var aimRotation = renderer.WorldRotation.Inverse * eye.Rotation;
		var aimAngles = aimRotation.Angles();
		var aim = aimRotation.Forward;

		parameters.TrySet( "aim_body", aim );
		parameters.TrySet( "aim_target", eye.Position + eye.Forward * 10000f );
		parameters.TrySet( "aim_body_pitch", aimAngles.pitch );
		parameters.TrySet( "aim_body_yaw", aimAngles.yaw );
		parameters.TrySet( "aim_body_weight", Controller.AimStrengthBody );
		parameters.TrySet( "aim_head", aim );
		parameters.TrySet( "aim_head_pitch", aimAngles.pitch );
		parameters.TrySet( "aim_head_yaw", aimAngles.yaw );
		parameters.TrySet( "aim_head_weight", Controller.AimStrengthHead );
		parameters.TrySet( "aim_eyes", aim );
		parameters.TrySet( "aim_eyes_weight", Controller.AimStrengthEyes );
	}
}
