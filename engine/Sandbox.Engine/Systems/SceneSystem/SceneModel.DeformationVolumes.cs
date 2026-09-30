using System.Runtime.InteropServices;

namespace Sandbox;

public sealed partial class SceneModel
{
	private readonly List<SceneDeformationVolumeData> _volumeSnapshot = new();

	/// <summary>
	/// The deformation volumes applied to this model, as last set - what the managed scene renderer skins it with.
	/// </summary>
	internal ReadOnlySpan<SceneDeformationVolumeData> DeformationVolumes => CollectionsMarshal.AsSpan( _volumeSnapshot );

	/// <summary>
	/// Replaces the active, validated deformation volumes applied to this model.
	/// The packed values are copied, so later edits require another call.
	/// </summary>
	internal unsafe void SetDeformationVolumes( ReadOnlySpan<SceneDeformationVolumeData> volumes )
	{
		if ( volumes.SequenceEqual( CollectionsMarshal.AsSpan( _volumeSnapshot ) ) )
		{
			return;
		}

		fixed ( SceneDeformationVolumeData* data = volumes )
		{
			animNative.SetDeformationVolumes( volumes.Length, (IntPtr)data );
		}

		_volumeSnapshot.Clear();
		foreach ( var volume in volumes )
		{
			_volumeSnapshot.Add( volume );
		}

		// Its vertices move as if its bones had, and its bounds grow
		NotifyChanged( Rendering.SceneObjectChange.Bones );
	}
}
