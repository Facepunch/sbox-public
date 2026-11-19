namespace Sandbox;

public sealed partial class SceneModel
{
	/// <summary>
	/// A scene model writes its own mesh object, its pose included (<see cref="BonesChanged"/>).
	/// </summary>
	internal override bool OwnsRenderObject => true;

	internal override Borealis.RenderObject MakeRenderObject( Rendering.ISceneWorldRender render ) => new Borealis.MeshObject( null, Transform );

	/// <summary>
	/// Its bones moved. Animated on an instance, they go straight into its own mesh object from whichever thread posed it: only
	/// this object's bone array is written, so the parallel animation passes write theirs at once, and its bounds, which move it
	/// in its world, wait for the world's next frame. Otherwise the pose is written once in the next frame however many times it
	/// moved (a copy posed bone by bone through <see cref="SetBoneWorldTransform"/>).
	/// </summary>
	void BonesChanged()
	{
		if ( animNative.HasInstance && !ThreadSafe.IsMainThread && OwnsRenderObject
			&& RenderObject is Borealis.MeshObject { World: not null, Mesh: { IsSkinned: true } drawn } mesh && !mesh.InRender )
		{
			GetBoneWorldMatrices( mesh.PoseBoneMatrices( drawn.BindPose.Length ) );
			NotifyChanged( Rendering.SceneObjectChange.Bounds );
			return;
		}

		World?.Render?.Later( this, Rendering.SceneObjectChange.Bones );
	}
}
