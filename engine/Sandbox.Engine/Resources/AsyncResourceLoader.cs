using System.Threading;

namespace Sandbox;

//
// This is just a test right now
//
class AsyncResourceLoader : IDisposable
{
	HResourceManifest manifest;

	AsyncResourceLoader( HResourceManifest manifest )
	{
		this.manifest = manifest;
	}

	~AsyncResourceLoader()
	{
		if ( manifest.IsValid && !NativeEngine.g_pResourceSystem.IsShuttingDown() )
		{
			MainThread.QueueDispose( this );
		}
	}

	public async Task WaitForLoad( CancellationToken token = default )
	{
		// does this shit itself if the resource is missing?
		// does it handle compiling okay?
		while ( !NativeEngine.g_pResourceSystem.IsShuttingDown() && !NativeEngine.g_pResourceSystem.IsManifestLoaded( manifest ) )
		{
			await Task.Yield();
			token.ThrowIfCancellationRequested();

			if ( NativeEngine.g_pResourceSystem.IsShuttingDown() )
				return;
		}
	}

	public void Dispose()
	{
		if ( manifest.IsValid && !NativeEngine.g_pResourceSystem.IsShuttingDown() )
		{
			ThreadSafe.AssertIsMainThread();

			NativeEngine.g_pResourceSystem.DestroyResourceManifest( manifest );
		}

		manifest = default;
		GC.SuppressFinalize( this );
	}

	static public AsyncResourceLoader Load( string filename )
	{
		ThreadSafe.AssertIsMainThread();

		if ( NativeEngine.g_pResourceSystem.IsShuttingDown() )
			return default;

		var manifest = NativeEngine.g_pResourceSystem.LoadResourceInManifest( filename );
		if ( manifest.IsNull ) return default;

		return new AsyncResourceLoader( manifest );
	}
}
