namespace Sandbox;

/// <summary>
/// The camera view being composed for a frame - position, rotation and field of view. The camera's
/// owner writes the base values (its transform and <see cref="CameraComponent.FieldOfView"/>), then
/// <see cref="ICameraModifier"/>s reshape them in order, then transient render effects
/// (<see cref="CameraEffectSystem"/>) apply on top. Read the composed result from
/// <see cref="CameraComponent.View"/>.
/// </summary>
public struct CameraView : IEquatable<CameraView>
{
	/// <summary>Where the camera sits, in world space.</summary>
	public Vector3 Position;

	/// <summary>Which way the camera looks.</summary>
	public Rotation Rotation;

	/// <summary>Field of view, in degrees.</summary>
	public float FieldOfView;

	/// <summary>Near clip plane distance.</summary>
	public float ZNear;

	/// <summary>Far clip plane distance.</summary>
	public float ZFar;

	/// <summary>The view's position and rotation as a transform.</summary>
	public readonly Transform Transform => new( Position, Rotation );

	/// <summary>The ray looking out of the view - for aim traces from the composed camera.</summary>
	public readonly Ray ForwardRay => new( Position, Rotation.Forward );

	public readonly bool Equals( CameraView other ) =>
		Position == other.Position &&
		Rotation == other.Rotation &&
		FieldOfView == other.FieldOfView &&
		ZNear == other.ZNear &&
		ZFar == other.ZFar;

	public override readonly bool Equals( object obj ) => obj is CameraView other && Equals( other );
	public override readonly int GetHashCode() => HashCode.Combine( Position, Rotation, FieldOfView, ZNear, ZFar );

	public static bool operator ==( CameraView a, CameraView b ) => a.Equals( b );
	public static bool operator !=( CameraView a, CameraView b ) => !a.Equals( b );
}

/// <summary>
/// Reshapes a camera's view each frame - scope zoom, vehicle roll, aim-down-sights. Implement on any
/// component: when a camera composes its view it runs every modifier in <see cref="CameraOrder"/>,
/// then hands the final view to <see cref="PostCameraSetup"/> so things can be placed against it.
/// </summary>
public interface ICameraModifier
{
	/// <summary>Lower runs first - a player's camera ~0, vehicles ~100, held weapons ~200.</summary>
	public int CameraOrder => 0;

	/// <summary>
	/// Reshape the view. Runs once per camera per frame, in order, at the camera stage of the
	/// tick - after Update and bone merging, before PreRender.
	/// </summary>
	public void ModifyCamera( CameraComponent camera, ref CameraView view ) { }

	/// <summary>
	/// The view is final, before render effects - place things against it (view models, attached
	/// props). Don't modify the camera here.
	/// </summary>
	public void PostCameraSetup( CameraComponent camera, in CameraView view ) { }
}
