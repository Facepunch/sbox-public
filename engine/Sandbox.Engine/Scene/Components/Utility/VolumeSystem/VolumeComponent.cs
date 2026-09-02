namespace Sandbox.Volumes;

public abstract class VolumeComponent : Component, VolumeSystem.IVolume
{
	[InlineEditor, Property]
	public SceneVolume SceneVolume { get; set; } = new SceneVolume();

	/// <summary>
	/// True if SceneVolume.Type == SceneVolume.VolumeTypes.Infinite
	/// </summary>
	public bool IsInfinite => SceneVolume.Type == SceneVolume.VolumeTypes.Infinite;

	private IDisposable _volumeUndoScope;

	protected override void DrawGizmos()
	{
		base.DrawGizmos();

		if ( !Gizmo.Pressed.Any )
		{
			_volumeUndoScope?.Dispose();
			_volumeUndoScope = null;
		}

		if ( !Gizmo.IsSelected )
			return;

		var vol = SceneVolume;

		vol.DrawGizmos( true, out var changed );

		if ( changed )
		{
			_volumeUndoScope ??= Scene.Editor?.UndoScope( "Resize Volume" ).WithComponentChanges( this ).Push();

			SceneVolume = vol;
		}
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
