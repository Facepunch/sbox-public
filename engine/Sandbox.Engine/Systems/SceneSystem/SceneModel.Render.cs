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
	/// this object's bone array is written, so the parallel animation passes write theirs at once. Its bounds, which move it in
	/// its world, are written on the main thread; posed on another, they wait for the world's next render.
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

		NotifyChanged( Rendering.SceneObjectChange.Bones );
	}
}
