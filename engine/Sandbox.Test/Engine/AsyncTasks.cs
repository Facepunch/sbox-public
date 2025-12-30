using Sandbox.Engine;
using Sandbox.Tasks;
using System;
using System.Threading;

namespace Engine;

[TestClass]
public class AsyncTasks
{
	private TaskSource TaskSource;

	[TestInitialize]
	public void TestInitialize()
	{
		SyncContext.Init();
		SyncContext.Reset();

		TaskSource = new TaskSource( 0 );
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
		while ( true )
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

		Assert.IsFalse( task.Wait( 1000 ) );
		Assert.IsTrue( _counter <= counterValue + 1 );
	}

	private async Task ExampleTask4Async()
	{
		var src = TaskSource;

		while ( true )
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
