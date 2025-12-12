namespace Sandbox;

public sealed partial class SceneModel
{
	/// <summary>
	/// Update this animation. Delta is the time you want to advance, usually RealTime.Delta
	/// </summary>
	public void Update( float delta )
	{
		animNative.Update( delta );
		FinishBoneUpdate();
	}

	/// <summary>
	/// Update this animation. Delta is the time you want to advance, usually RealTime.Delta
	/// </summary>
	internal void Update( float delta, Action updateBones )
	{
		animNative.Update( delta );
		updateBones.Invoke();
		FinishBoneUpdate();
	}

	/// <summary>
	/// Update all of the bones to the bind pose
	/// </summary>
	public void UpdateToBindPose()
	{
		animNative.SetBindPose();
		FinishBoneUpdate();
	}

	/// <summary>
	/// Update all of the bones to the bind pose
	/// </summary>
	internal void UpdateToBindPose( Action updateBones )
	{
		animNative.SetBindPose();
		updateBones.Invoke();
		FinishBoneUpdate();
	}

	/// <summary>
	/// Updates attachments, ao proxies etc
	/// Should be called any time the world transform change
	/// </summary>
	internal void FinishBoneUpdate()
	{
		if ( Parent.IsValid() )
			return;

		animNative.CalculateWorldSpaceBones();
		animNative.FinishUpdate();
	}

	/// <summary>
	/// Update our bones to match the target's bones. This is a manual bone merge.
	/// </summary>
	public void MergeBones( SceneModel parent )
	{
		Assert.IsValid( parent );
		Assert.False( parent == this );

		animNative.MergeFrom( parent );
	}

	/// <summary>
	/// Returns the parent space transform of a bone by its index.
	/// </summary>
	/// <param name="i">Index of the bone to calculate transform of.</param>
	/// <returns>The parent space transform, or an identity transform on failure.</returns>
	public Transform GetParentSpaceBone( int i ) => animNative.GetParentSpaceBone( i );

	internal void SetParentSpaceBone( int i, in Transform tx ) => animNative.SetParentSpaceBone( i, tx );
}
