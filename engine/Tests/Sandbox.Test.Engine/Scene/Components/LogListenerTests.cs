using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SceneTests.Components;

/// <summary>
/// Runtime diagnostics are delivered without cross-scene leakage or listener feedback loops.
/// </summary>
[TestClass]
[DoNotParallelize]
public class LogListenerTests : SceneTest
{
	/// <summary>
	/// The logging pipeline forwards actual exceptions, including their formatted stack text.
	/// </summary>
	[TestMethod]
	public void RealLoggingReachesComponentListener()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var listener = scene.CreateObject().Components.Create<LogTestListener>();
		new Sandbox.Diagnostics.Logger( "LogListenerTests" ).Error( new InvalidOperationException( "log-listener-pipeline-test" ) );
		Assert.AreEqual( 0, listener.Events.Count );
		scene.DispatchLogEvents();
		var entry = listener.Events.Single( x => x.Message.Contains( "log-listener-pipeline-test" ) );
		Assert.AreEqual( LogLevel.Error, entry.Level );
		Assert.IsTrue( entry.Stack.Contains( "InvalidOperationException" ) );
		Assert.IsNull( entry.Exception );
		scene.Destroy();
	}

	/// <summary>
	/// Components receive queued messages only in their originating scene and while enabled.
	/// </summary>
	[TestMethod]
	public void DeliveryIsDeferredAndSceneLocal()
	{
		var first = new Scene();
		var second = new Scene();
		using var scope = first.Push();
		var listener = first.CreateObject().Components.Create<LogTestListener>();
		var other = second.CreateObject().Components.Create<LogTestListener>();
		Scene.CaptureLogEvent( new LogEvent { Message = "first", Level = LogLevel.Warn } );
		Assert.AreEqual( 0, listener.Events.Count );
		second.DispatchLogEvents();
		Assert.AreEqual( 0, other.Events.Count );
		first.DispatchLogEvents();
		Assert.AreEqual( "first", listener.Events[0].Message );
		listener.Enabled = false;
		Scene.CaptureLogEvent( new LogEvent { Message = "disabled" } );
		first.DispatchLogEvents();
		Assert.AreEqual( 1, listener.Events.Count );
		first.Destroy();
		second.Destroy();
	}

	/// <summary>
	/// Log callbacks cannot form a feedback loop, and exceptions do not prevent later listeners.
	/// </summary>
	[TestMethod]
	public void ListenerLogsDoNotReenterDelivery()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var listener = scene.CreateObject().Components.Create<LogTestListener>();
		listener.Reenter = true;
		var second = scene.CreateObject().Components.Create<LogTestListener>();
		Scene.CaptureLogEvent( new LogEvent { Message = "initial" } );
		scene.DispatchLogEvents();
		scene.DispatchLogEvents();
		Assert.AreEqual( 1, listener.Events.Count );
		Assert.AreEqual( 1, second.Events.Count );
		scene.Destroy();
	}

	/// <summary>
	/// Background emissions retain their originating async context and the bounded queue sheds old entries.
	/// </summary>
	[TestMethod]
	public void BackgroundQueueIsBoundedAndDoesNotRetainExceptions()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var listener = scene.CreateObject().Components.Create<LogTestListener>();
		Task.Run( () =>
		{
			for ( var i = 0; i < 600; i++ )
			{
				Scene.CaptureLogEvent( new LogEvent { Message = i.ToString(), Exception = new Exception( "not retained" ) } );
			}
		} ).GetAwaiter().GetResult();
		scene.DispatchLogEvents();
		Assert.AreEqual( 512, listener.Events.Count );
		Assert.AreEqual( "88", listener.Events[0].Message );
		Assert.IsNull( listener.Events[0].Exception );
		scene.Destroy();
	}
}

/// <summary>
/// Records delivered diagnostics for the scene tests.
/// </summary>
public sealed class LogTestListener : Component, Component.ILogListener
{
	/// <summary>
	/// Delivered events, in delivery order.
	/// </summary>
	public List<LogEvent> Events { get; } = new();

	/// <summary>
	/// Exercises recursive emission and exception isolation.
	/// </summary>
	public bool Reenter { get; set; }

	/// <summary>
	/// Records an event, optionally trying to recursively emit another.
	/// </summary>
	public void OnLog( LogEvent entry )
	{
		Events.Add( entry );
		if ( Reenter )
		{
			Scene.CaptureLogEvent( new LogEvent { Message = "nested" } );
			throw new InvalidOperationException( "listener failure" );
		}
	}
}
