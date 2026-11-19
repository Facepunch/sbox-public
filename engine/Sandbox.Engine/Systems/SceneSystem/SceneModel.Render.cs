namespace Sandbox;

public sealed partial class SceneModel
{
	/// <summary>
	/// A scene model managed code made writes its own mesh object, its pose included (<see cref="BonesChanged"/>). One native code
	/// made (a map's) is written by its world's renderer.
	/// </summary>
	internal override bool OwnsRenderObject => ResolvesDraws;

	internal override void WriteRender( Borealis.RenderObject obj, Rendering.SceneObjectChange change, Rendering.ISceneWorldRender render )
	{
		if ( (change & Rendering.SceneObjectChange.Tags) != 0 ) obj.Tags = RenderTags();
		if ( obj is Borealis.MeshObject mesh ) render.WriteMesh( this, mesh, change );
	}

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
