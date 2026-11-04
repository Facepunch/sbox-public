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

	private readonly List<Vector4> _anchorSnapshot = new();

	/// <summary>
	/// The model-space anchor per model bone this model is rigidly deformed by, as last set, or none.
	/// </summary>
	internal ReadOnlySpan<Vector4> DeformationAnchors => CollectionsMarshal.AsSpan( _anchorSnapshot );

	/// <summary>
	/// Makes the deformation volumes move this model rigidly: each part of it moves as they move its bone's anchor, a
	/// model-space point per model bone, without being reshaped. None deforms it as usual.
	/// </summary>
	internal unsafe void SetDeformationAnchors( ReadOnlySpan<Vector4> anchors )
	{
		if ( anchors.SequenceEqual( CollectionsMarshal.AsSpan( _anchorSnapshot ) ) )
		{
			return;
		}

		fixed ( Vector4* data = anchors )
		{
			animNative.SetDeformationAnchors( anchors.Length, (IntPtr)data );
		}

		_anchorSnapshot.Clear();
		_anchorSnapshot.AddRange( anchors );
		NotifyChanged( Rendering.SceneObjectChange.Bones );
	}
}
