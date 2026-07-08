namespace Sandbox;

public sealed partial class CameraComponent
{
	//
	// The front door for camera effects - forwards into the scene's CameraEffectSystem, which owns
	// the effects. Use the system directly for world-epicenter shakes, custom effects and Scale.
	//

	/// <summary>
	/// Add a custom camera effect (a <see cref="CameraEffectSystem.BaseEffect"/> subclass), targeted
	/// at this camera.
	/// </summary>
	public T AddEffect<T>( T effect ) where T : CameraEffectSystem.BaseEffect
	{
		if ( effect is null )
			return null;

		effect.Camera = this;
		return CameraEffectSystem.Get( Scene )?.Add( effect );
	}

	/// <summary>
	/// Shake this camera - the classic screen shake (Half-Life's env_shake). Every
	/// 1/<paramref name="frequency"/> seconds a new random offset within <paramref name="amplitude"/>
	/// world units, settling to nothing over <paramref name="duration"/>. A duration of zero shakes
	/// until <see cref="CameraEffectSystem.BaseEffect.Stop"/>.
	/// </summary>
	public CameraEffectSystem.BaseEffect AddShake( float amplitude, float frequency, float duration )
	{
		return CameraEffectSystem.Get( Scene )?.AddShake( this, amplitude, frequency, duration );
	}

	/// <summary>
	/// Punch this camera along a camera-local direction, oscillating and dying out -
	/// <see cref="Vector3.Up"/> bounces the view upward, <paramref name="frequency"/> is how many
	/// oscillations before it lapses.
	/// </summary>
	public CameraEffectSystem.BaseEffect AddPunch( Vector3 direction, float amplitude, float frequency = 1f, float duration = 0.3f, float fovAmplitude = 0f )
	{
		return CameraEffectSystem.Get( Scene )?.AddPunch( this, direction, amplitude, frequency, duration, fovAmplitude );
	}

	/// <summary>
	/// Punch this camera's view angles - it kicks to <paramref name="angles"/> and bounces back,
	/// oscillating <paramref name="frequency"/> times before it lapses. Melee swings, landings,
	/// launches. Render-only - the player's aim never moves.
	/// </summary>
	public CameraEffectSystem.BaseEffect AddPunch( Angles angles, float frequency = 1f, float duration = 0.3f, float fovAmplitude = 0f )
	{
		return CameraEffectSystem.Get( Scene )?.AddPunch( this, angles, frequency, duration, fovAmplitude );
	}

	/// <summary>
	/// Tilt this camera by <paramref name="angle"/>, eased in over <paramref name="easeTime"/> and
	/// back out before <paramref name="duration"/> ends.
	/// </summary>
	public CameraEffectSystem.BaseEffect AddTilt( Angles angle, float duration, float easeTime = 0.2f )
	{
		return CameraEffectSystem.Get( Scene )?.AddTilt( this, angle, duration, easeTime );
	}
}
