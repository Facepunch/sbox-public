
namespace Sandbox;

/// <summary>
/// A model scene object that supports animations and can be rendered within a <see cref="SceneWorld"/>.
/// </summary>
public sealed partial class SceneModel : SceneObject
{
	/// <summary>
	/// Manually override the final bone transform.
	/// </summary>
	/// <param name="boneIndex"></param>
	/// <param name="transform">Local coordinates based on the SceneModel's transform</param>
	public void SetBoneOverride( int boneIndex, in Transform transform )
	{
		if ( boneIndex < 0 ) return;

		animNative.SetPhysicsBone( (ushort)boneIndex, transform );
	}

	/// <summary>
	/// Clears all bone transform overrides.
	/// </summary>
	public void ClearBoneOverrides()
	{
		animNative.ClearPhysicsBones();
	}

	/// <summary>
	/// Whether any bone transforms have been overridden.
	/// </summary>
	public bool HasBoneOverrides()
	{
		return animNative.HasPhysicsBones();
	}

	/// <summary>
	/// Calculates the velocity from the previous and current bone transforms.
	/// </summary>
	public void GetBoneVelocity( int boneIndex, out Vector3 linear, out Vector3 angular )
	{
		linear = default;
		angular = default;

		if ( animNative.IsNull )
			return;

		var delta = animNative.m_flDeltaTime;
		if ( delta <= 0.0f )
			return;

		var boneNow = animNative.GetWorldSpaceRenderBoneTransform( boneIndex );
		var boneThen = animNative.GetWorldSpaceRenderBonePreviousTransform( boneIndex );

		linear = (boneNow.Position - boneThen.Position) / delta;

		var diff = Rotation.Difference( boneThen.Rotation, boneNow.Rotation );
		angular = new Vector3( diff.x, diff.y, diff.z ) / delta;
	}
}
