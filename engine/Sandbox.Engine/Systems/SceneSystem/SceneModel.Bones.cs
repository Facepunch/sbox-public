
namespace Sandbox;

/// <summary>
/// A model scene object that supports animations and can be rendered within a <see cref="SceneWorld"/>.
/// </summary>
public sealed partial class SceneModel : SceneObject
{
	public void SetBoneOverride( int boneIndex, in Transform transform )
	{
		if ( boneIndex < 0 ) return;

		animNative.SetPhysicsBone( (ushort)boneIndex, transform );
	}

	public void ClearBoneOverrides()
	{
		animNative.ClearPhysicsBones();
	}

	internal bool HasBoneOverrides()
	{
		return animNative.HasPhysicsBones();
	}

	/// <summary>
	/// Calculate velocity from previous and current bone transform 
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
