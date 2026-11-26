using Borealis.Backend;
using Borealis.Gpu;
using System.Threading;
using System.Threading.Tasks;

namespace BorealisGpuTests;

/// <summary>
/// A backend device's Dispose waits for every item a worker is running or has queued that makes resources on its device
/// (a view's visibility and ids targets made ahead, a content read's images, a map's lightmaps), and those make nothing
/// once it has begun: one that made a resource after the device went crashed the process.
/// </summary>
[TestClass]
[DoNotParallelize]
public class BackendDisposeTests : GpuTest
{
	[TestMethod]
	[DataRow( "visibility" )]
	[DataRow( "ids" )]
	[DataRow( "read" )]
	[DataRow( "lightmaps" )]
	public void DisposeWaitsForWorkersMakingResources( string what )
	{
		BackendDevice backend = null;
		Task disposing = null;
		using var started = new ManualResetEventSlim();
		using var release = new ManualResetEventSlim();
		var queued = 0;
		var view = new object();
		BackendDevice.VisibilityView state = null;
		try
		{
			backend = new BackendDevice( Device );
			backend.WorkItemStarting = item =>
			{
				if ( item != what ) return;
				Interlocked.Increment( ref queued );
				started.Set();
				release.Wait( TimeSpan.FromSeconds( 30 ) );
			};

			backend.BeginFrame();
			var depth = TextureInfo.RenderTarget( 64, 32, Format.D32Float );
			switch ( what )
			{
				case "visibility":
					backend.AsyncModels = true;
					Assert.IsNull( backend.VisibilityFor( view, depth ), "made on a worker" );
					break;
				case "ids":
					state = backend.VisibilityFor( view, depth );
					backend.IdsTargetsFor( state );
					backend.AsyncModels = true;
					backend.PrepareIdsTargets( state );
					Assert.IsFalse( state.IdsReady, "made on a worker" );
					break;
				case "read":
					Assert.IsNull( backend.LoadTextureAsync( "materials/dev/no_such_texture.vtex", out var pending ) );
					Assert.IsTrue( pending, "read on a worker" );
					break;
				case "lightmaps":
					backend.ReadMapLightmapsAhead( "maps/no_such_map" );
					break;
			}

			Assert.IsTrue( started.Wait( TimeSpan.FromSeconds( 10 ) ), $"the {what} item started" );
			disposing = Task.Run( backend.Dispose );
			backend = null;
			Assert.IsFalse( disposing.Wait( 500 ), $"Dispose returned while the {what} item was still running" );

			release.Set();
			Assert.IsTrue( disposing.Wait( TimeSpan.FromSeconds( 10 ) ), "Dispose returned once it finished" );

			// Nothing made since: the item saw the device going
			var (buffers, textures) = (Device.Buffers.Count, Device.Textures.Count);
			Thread.Sleep( 100 );
			Assert.AreEqual( (buffers, textures), (Device.Buffers.Count, Device.Textures.Count) );
			if ( state is not null ) Assert.IsFalse( state.PendingIds.IsValid || state.Ids.IsValid, "no ids targets made" );
			Assert.AreEqual( 1, queued );
		}
		finally
		{
			release.Set();
			backend?.Dispose();
			disposing?.Wait( TimeSpan.FromSeconds( 30 ) );
		}

		GC.KeepAlive( view );
	}
}
