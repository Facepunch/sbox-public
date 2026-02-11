using System.Threading;

namespace Sandbox;

/// <summary>
/// Used for c++ to make callbacks to a camera and be able to find it by id
/// </summary>
internal interface IManagedCamera
{
	static Lock _directoryLock = new();
	static Dictionary<int, WeakReference<IManagedCamera>> _directory = new();
	static int _indexer = 1000;


	/// <summary>
	/// Called when entering a specific pipeline stage
	/// </summary>
	void OnRenderStage( Rendering.Stage renderStage );

	/// <summary>
	/// Allocate a camera id for this camera. This is used to find the camera in the c++ code.
	/// </summary>
	int AllocateCameraId()
	{
		Cleanup();

		lock ( _directoryLock )
		{
			var cameraId = _indexer++;
			// We treat 0 as invalid, so skip it
			if ( cameraId == 0 ) cameraId = _indexer++;
			_directory[cameraId] = new WeakReference<IManagedCamera>( this );
			return cameraId;
		}
	}

	/// <summary>
	/// Find a camera by its id. This is used to find the camera by the calling c++ code. This is called
	/// in the render thread, so it's important to be thread-safe.
	/// </summary>
	public static IManagedCamera FindById( int cameraId )
	{
		lock ( _directoryLock )
		{
			if ( !_directory.TryGetValue( cameraId, out var reference ) )
				return null;

			if ( !reference.TryGetTarget( out var cam ) )
				return null;

			return cam;
		}
	}



	/// <summary>
	/// keep the directory clean by trimming all the old ones
	/// </summary>
	static void Cleanup()
	{
		lock ( _directoryLock )
		{
			foreach ( var old in _directory.Where( x => !x.Value.TryGetTarget( out var _ ) ).Select( x => x.Key ).ToArray() )
			{
				_directory.Remove( old );
			}
		}
	}
}
