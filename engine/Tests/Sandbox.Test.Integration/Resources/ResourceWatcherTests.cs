using Sandbox.Engine;
using System;
using System.Runtime.CompilerServices;

namespace ResourceTests;

/// <summary>
/// Resource notifications follow the context and filesystem which registered their watcher.
/// </summary>
[TestClass, DoNotParallelize]
public class ResourceWatcherTests
{
	private const string ResourcePath = "watcher.blobres";

	/// <summary>
	/// Every context receives its own resource deletion even when notifications are dispatched elsewhere.
	/// Separate mounts must both be watched, and a shared mount must notify each owning context.
	/// </summary>
	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void ChangesUseEachOwningContext( bool sharedMount )
	{
		ThreadSafe.MarkMainThread();
		var previous = GlobalContext.Current;
		var firstMount = new MemoryFileSystem();
		var secondMount = sharedMount ? null : new MemoryFileSystem();
		var first = CreateContext( firstMount );
		var second = CreateContext( secondMount ?? firstMount );

		try
		{
			var firstResource = RegisterResource( first );
			var secondResource = RegisterResource( second );

			DispatchDeletion( firstMount );
			Assert.AreSame( previous, GlobalContext.Current );
			Assert.IsFalse( firstResource.IsValid );
			Assert.IsNull( first.ResourceSystem.Get<BlobResource>( ResourcePath ) );
			Assert.AreEqual( !sharedMount, secondResource.IsValid );

			if ( !sharedMount )
			{
				DispatchDeletion( secondMount );
				Assert.AreSame( previous, GlobalContext.Current );
			}

			Assert.IsFalse( secondResource.IsValid );
			Assert.IsNull( second.ResourceSystem.Get<BlobResource>( ResourcePath ) );
		}
		finally
		{
			using ( new GlobalContext.GlobalContextScope( first ) )
			{
				first.Shutdown();
			}
			using ( new GlobalContext.GlobalContextScope( second ) )
			{
				second.Shutdown();
			}
			firstMount.Dispose();
			secondMount?.Dispose();
		}
	}

	/// <summary>
	/// Cleared or cancelled watchers can be collected while the context remains alive, then registered again.
	/// </summary>
	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void ReleasedWatchersCanBeCollectedAndRearmed( bool resetContext )
	{
		ThreadSafe.MarkMainThread();
		var mount = new MemoryFileSystem();
		var context = CreateContext( mount );
		using var scope = new GlobalContext.GlobalContextScope( context );

		try
		{
			var references = new WeakReference[3];
			for ( var i = 0; i < references.Length; i++ )
			{
				references[i] = CreateAndReleaseWatcher( context, resetContext );
				Assert.AreEqual( 0, mount.watchers.Count );
			}

			GC.Collect();
			GC.WaitForPendingFinalizers();
			GC.Collect();

			foreach ( var reference in references )
			{
				Assert.IsFalse( reference.IsAlive, "The context must not retain a released watcher through its cancellation callbacks." );
			}

			var resource = RegisterResource( context );
			DispatchDeletion( mount );
			Assert.IsFalse( resource.IsValid );
			GC.KeepAlive( context.CancellationTokenSource );
		}
		finally
		{
			context.Shutdown();
			mount.Dispose();
		}
	}

	/// <summary>
	/// Keeps the watcher's strong reference off the collection test's stack.
	/// </summary>
	[MethodImpl( MethodImplOptions.NoInlining )]
	private static WeakReference CreateAndReleaseWatcher( GlobalContext context, bool resetContext )
	{
		ResourceLoader.LoadAllGameResource( context.FileMount );
		var reference = new WeakReference( context.FileMount.watchers.First() );

		if ( resetContext )
		{
			context.Reset();
		}
		else
		{
			ResourceLoader.Clear();
		}

		return reference;
	}

	private static GlobalContext CreateContext( BaseFileSystem mount ) => new()
	{
		TypeLibrary = SharedTypeLibrary.EngineAndTests,
		FileMount = mount
	};

	private static BlobResource RegisterResource( GlobalContext context )
	{
		using var scope = new GlobalContext.GlobalContextScope( context );
		ResourceLoader.LoadAllGameResource( context.FileMount );
		var resource = new BlobResource();
		resource.Register( ResourcePath );
		return resource;
	}

	private static void DispatchDeletion( BaseFileSystem mount )
	{
		mount.AddChangedFile( ResourcePath + "_c" );
		FileWatch.TimeSinceLastChange = 1;
		FileWatch.Tick();
	}
}
