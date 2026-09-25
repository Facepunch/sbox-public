using System.Runtime.InteropServices;

namespace Sandbox;

public sealed partial class SceneModel
{
	private readonly List<SceneDeformationVolumeData> _volumeSnapshot = new();

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
	}
}
