using Sandbox.Engine;
using Sandbox.Tasks;
using Sandbox.Resources;
using System;
using System.Threading;

namespace EngineTests;

[TestClass]
public class AsyncTaskTest
{
	private TaskSource TaskSource;

	sealed class CallbackCompiler( Func<Task<bool>> callback ) : ResourceCompiler
	{
		protected override Task<bool> Compile() => callback();
	}

	sealed class CallbackTextureGenerator( Func<Task> callback ) : TextureGenerator
	{
		protected override async ValueTask<Texture> CreateTexture( Options options, CancellationToken ct )
		{
			await callback();

			// This test checks scheduling without allocating a native texture.
			return null;
		}
	}

	/// <summary>
	/// Compiler texture callbacks and nested child compiles retain the engine main thread
	/// before and after asynchronous work, without dispatching to the blocked engine queue.
	/// </summary>
	[TestMethod]
	public void TextureCompilationPreservesMainThread()
	{
		ThreadSafe.AssertIsMainThread();
		var previous = SynchronizationContext.Current;
		var childCompleted = false;
		var child = new CallbackCompiler( async () =>
		{
			Assert.IsTrue( ThreadSafe.IsMainThread );
			await Task.Yield();
			Assert.IsTrue( ThreadSafe.IsMainThread );
			childCompleted = true;
			return true;
		} );
		var generator = new CallbackTextureGenerator( async () =>
		{
			Assert.IsTrue( ThreadSafe.IsMainThread );
			var context = SynchronizationContext.Current;
			await Task.Yield();
			await Task.Run( () => Assert.IsFalse( ThreadSafe.IsMainThread ) );
			Assert.IsTrue( ThreadSafe.IsMainThread );

			// Check completion first so a regression fails instead of blocking the test.
			Assert.IsTrue( MainThread.Wait().IsCompleted );
			await MainThread.Wait();
			Assert.IsTrue( child.CompileInternal() );
			Assert.AreSame( context, SynchronizationContext.Current );
		} );
		CallbackCompiler compiler = null;
		compiler = new CallbackCompiler( async () =>
		{
			await generator.CreateAsync( new ResourceGenerator.Options { ForDisk = true, Compiler = compiler }, default );
			Assert.IsTrue( ThreadSafe.IsMainThread );
			return true;
		} );

		Assert.IsTrue( compiler.CompileInternal() );
		Assert.IsTrue( childCompleted );
		Assert.AreSame( previous, SynchronizationContext.Current );
	}

	/// <summary>
	/// A native callback can await the main thread inside a private pump without leaving that pump.
	/// Frame waits must still yield to the engine queue.
	/// </summary>
	[TestMethod]
	public void MainThreadSwitchInsideAsyncPump()
	{
		ThreadSafe.AssertIsMainThread();
		var previous = SynchronizationContext.Current;

		AsyncPump.RunSynchronously( async () =>
		{
			var context = SynchronizationContext.Current;
			Assert.IsTrue( MainThread.Wait().IsCompleted );
			Assert.IsTrue( GameTask.MainThread().IsCompleted );
			Assert.IsFalse( new SyncTask( SyncContext.MainThread ).IsCompleted );

			await MainThread.Wait();
			await Task.Yield();
			await GameTask.MainThread();

			Assert.AreSame( context, SynchronizationContext.Current );
			ThreadSafe.AssertIsMainThread();
		} );

		Assert.AreSame( previous, SynchronizationContext.Current );
	}

	/// <summary>
	/// The cancellation-swallowing tasks in <see cref="TaskCancel3"/> and <see cref="TaskCancel4"/>
	/// loop forever by design. Without an exit they outlive their test and keep getting pumped by
	/// every later test that processes the main thread queue - this flag lets cleanup kill them.
	/// </summary>
	private static volatile bool _stopZombies;

	[TestInitialize]
	public void TestInitialize()
	{
		_stopZombies = false;

		SyncContext.Init();
		SyncContext.Reset();

		TaskSource = new TaskSource( 0 );
	}

	/// <summary>
	/// Signals any zombie tasks left behind by the test to exit, then pumps the main
	/// thread queue so they actually get a chance to observe the flag and die.
	/// </summary>
	[TestCleanup]
	public void TestCleanup()
	{
		_stopZombies = true;

		for ( var i = 0; i < 10; ++i )
		{
			Thread.Sleep( 10 );
			SyncContext.MainThread.ProcessQueue();
		}
	}

	private void Reset()
	{
		GlobalContext.Current.Reset();

		SyncContext.Reset();

		// Give tasks time to handle being cancelled

		for ( var i = 1; i <= 10; ++i )
		{
			Console.WriteLine( $"Reset + {i * 10}ms" );
			Thread.Sleep( 10 );
			SyncContext.MainThread.ProcessQueue();
		}
	}

	[TestMethod]
	public void TaskCancel1()
	{
		var task = ExampleTask1Async();

		Reset();

		Assert.IsTrue( task.IsCanceled );
	}

	private Task<string> TaskWithResultAsync()
	{
		return Task.FromResult( "Hello world!" );
	}

	private async Task ExampleTask1Async( int initialDelay = 50 )
	{
		await TaskSource.DelayRealtime( initialDelay );

		while ( true )
		{
			await TaskWithResultAsync();
			await TaskSource.DelayRealtime( 50 );
		}
	}

	[TestMethod]
	public void TaskCancel2()
	{
		Assert.AreEqual( SyncContext.MainThread, SynchronizationContext.Current );

		var tasks = Enumerable.Range( 0, 1_000 ).Select( x => ExampleTask2Async( x * 10 ) ).ToArray();

		for ( var i = 0; i < 20; ++i )
		{
			Thread.Sleep( 10 );
			SyncContext.MainThread.ProcessQueue();
		}

		Reset();

		foreach ( var task in tasks )
		{
			Assert.IsTrue( task.IsCompleted );
			Assert.IsTrue( task.IsCompletedSuccessfully );
			Assert.IsTrue( task.Result );
		}
	}

	private async Task<bool> ExampleTask2Async( int initialDelay )
	{
		while ( true )
		{
			try
			{
				await ExampleTask1Async( initialDelay );
			}
			catch ( TaskCanceledException )
			{
				Console.WriteLine( $"  Cancelled: {initialDelay}" );
				return true;
			}
		}
	}

	[TestMethod]
	public void TaskCancel3()
	{
		var task = ExampleTask3Async();

		Reset();

		Assert.IsFalse( task.IsCanceled );
	}

	private async Task ExampleTask3Async()
	{
		while ( !_stopZombies )
		{
			try
			{
				await Task.Yield();
				await ExampleTask1Async();
			}
			catch ( TaskCanceledException )
			{
				Console.WriteLine( "I was cancelled!" );
			}
		}
	}

	private static int _counter = 0;

	[TestMethod]
	public void TaskCancel4()
	{
		var task = ExampleTask4Async();

		Reset();

		var counterValue = _counter;

		// Check that the task stalls without pumping engine continuations. The engine
		// synchronization context deliberately processes its queue during timed waits.
		using ( SyncContext.Scope( null ) )
		{
			Assert.IsFalse( task.Wait( 1000 ) );
		}
		Assert.IsTrue( _counter <= counterValue + 1 );
	}

	private async Task ExampleTask4Async()
	{
		var src = TaskSource;

		while ( !_stopZombies )
		{
			try
			{
				await Task.Run( async () =>
				{
					++_counter;
					await src.DelayRealtime( 10 );
				} );
			}
			catch ( TaskCanceledException )
			{
				Console.WriteLine( "I was cancelled!" );
			}
		}
	}

	[TestMethod]
	public void SwitchThreads1()
	{
		var task = ExampleTask5Async();

		for ( var i = 0; i < 100 && !task.IsCompleted; ++i )
		{
			Thread.Sleep( 10 );
			SyncContext.MainThread.ProcessQueue();
		}

		if ( task.Exception != null )
		{
			throw task.Exception;
		}

		Assert.IsTrue( task.IsCompletedSuccessfully );
	}

	private async Task ExampleTask5Async()
	{
		Console.WriteLine( $"Hello from {(ThreadSafe.IsMainThread ? "the main thread" : "a worker thread")}!" );
		ThreadSafe.AssertIsMainThread();

		await GameTask.WorkerThread();

		Console.WriteLine( $"Hello from {(ThreadSafe.IsMainThread ? "the main thread" : "a worker thread")}!" );
		ThreadSafe.AssertIsNotMainThread();

		await GameTask.MainThread();

		Console.WriteLine( $"Hello from {(ThreadSafe.IsMainThread ? "the main thread" : "a worker thread")}!" );
		ThreadSafe.AssertIsMainThread();
	}
}
