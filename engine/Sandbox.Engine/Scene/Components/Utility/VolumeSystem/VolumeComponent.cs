namespace Sandbox.Volumes;

public abstract class VolumeComponent : Component, VolumeSystem.IVolume
{
	[InlineEditor, Property]
	public SceneVolume SceneVolume { get; set; } = new SceneVolume();

	/// <summary>
	/// True if SceneVolume.Type == SceneVolume.VolumeTypes.Infinite
	/// </summary>
	public bool IsInfinite => SceneVolume.Type == SceneVolume.VolumeTypes.Infinite;

	protected override void DrawGizmos()
	{
		base.DrawGizmos();

		if ( !Gizmo.IsSelected )
			return;

		var vol = SceneVolume;
		vol.DrawGizmos( true );
		SceneVolume = vol;
	}

	bool VolumeSystem.IVolume.Test( Vector3 worldPosition )
	{
		return SceneVolume.Test( WorldTransform, worldPosition );
	}
	bool VolumeSystem.IVolume.Test( BBox worldBBox )
	{
		return SceneVolume.Test( WorldTransform, worldBBox );
	}

	bool VolumeSystem.IVolume.Test( Sphere worldSphere )
	{
		return SceneVolume.Test( WorldTransform, worldSphere );
	}

	SceneVolume VolumeSystem.IVolume.GetVolume()
	{
		return SceneVolume;
	}

	/// <summary>
	/// Calculates the shortest distance from the specified world position to the nearest edge of the scene volume.
	/// </summary>
	public float GetEdgeDistance( Vector3 worldPosition )
	{
		return SceneVolume.GetEdgeDistance( WorldTransform, worldPosition );
	}
}
