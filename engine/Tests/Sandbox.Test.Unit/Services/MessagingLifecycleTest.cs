using Azure.Messaging.WebPubSub.Clients;
using Sandbox.Services;
using System;
using System.Collections.Generic;
using System.Threading;

namespace ServicesTests;

/// <summary>
/// Verifies connection replacement without a network connection or native engine.
/// </summary>
[TestClass]
[DoNotParallelize]
public class MessagingLifecycleTest
{
	private sealed class Client( string name, List<string> calls ) : WebPubSubClient
	{
		internal Task StopWait = Task.CompletedTask;
		internal bool FailStart;
		internal bool FailStop;

		/// <inheritdoc />
		public override Task StartAsync( CancellationToken cancellationToken = default )
		{
			calls.Add( $"{name}.start" );
			return FailStart ? Task.FromException( new InvalidOperationException( "start failed" ) ) : Task.CompletedTask;
		}

		/// <inheritdoc />
		public override async Task StopAsync()
		{
			calls.Add( $"{name}.stop" );
			await StopWait;
			if ( FailStop ) throw new InvalidOperationException( "stop failed" );
		}

		/// <inheritdoc />
		protected override ValueTask DisposeAsyncCore()
		{
			calls.Add( $"{name}.dispose" );
			return ValueTask.CompletedTask;
		}
	}

	/// <summary>
	/// Release the static messaging connection even if an assertion fails.
	/// </summary>
	[TestCleanup]
	public Task Cleanup() => Messaging.Shutdown();

	/// <summary>
	/// Shutdown waits for replacement, and replacement waits for the old connection to stop.
	/// </summary>
	[TestMethod]
	public async Task ReplacementAndShutdownAreSerialized()
	{
		var calls = new List<string>();
		var stop = new TaskCompletionSource();
		await Messaging.Initialize( new Client( "first", calls ) { StopWait = stop.Task } );
		var replacement = Messaging.Initialize( new Client( "second", calls ) );
		var shutdown = Messaging.Shutdown();

		try
		{
			CollectionAssert.AreEqual( new[] { "first.start", "first.stop" }, calls );
			Assert.IsFalse( replacement.IsCompleted );
			Assert.IsFalse( shutdown.IsCompleted );
		}
		finally
		{
			stop.TrySetResult();
			await Task.WhenAll( replacement, shutdown ).WaitAsync( TimeSpan.FromSeconds( 5 ) );
		}

		CollectionAssert.AreEqual( new[]
		{
			"first.start", "first.stop", "first.dispose", "second.start", "second.stop", "second.dispose"
		}, calls );
	}

	/// <summary>
	/// A failed connection is disposed and does not prevent the next login from connecting.
	/// </summary>
	[TestMethod]
	public async Task FailedStartDisposesConnectionAndAllowsRetry()
	{
		var calls = new List<string>();
		await Assert.ThrowsExceptionAsync<InvalidOperationException>( () =>
			Messaging.Initialize( new Client( "failed", calls ) { FailStart = true } ) );
		await Messaging.Initialize( new Client( "retry", calls ) );
		await Messaging.Shutdown();

		CollectionAssert.AreEqual( new[]
		{
			"failed.start", "failed.dispose", "retry.start", "retry.stop", "retry.dispose"
		}, calls );
	}

	/// <summary>
	/// A stop failure still disposes both the old connection and the unused replacement.
	/// </summary>
	[TestMethod]
	public async Task FailedStopDisposesConnectionsAndAllowsRetry()
	{
		var calls = new List<string>();
		await Messaging.Initialize( new Client( "first", calls ) { FailStop = true } );
		await Assert.ThrowsExceptionAsync<InvalidOperationException>( () =>
			Messaging.Initialize( new Client( "unused", calls ) ) );
		await Messaging.Initialize( new Client( "retry", calls ) );
		await Messaging.Shutdown();

		CollectionAssert.AreEqual( new[]
		{
			"first.start", "first.stop", "first.dispose", "unused.dispose", "retry.start", "retry.stop", "retry.dispose"
		}, calls );
	}
}
