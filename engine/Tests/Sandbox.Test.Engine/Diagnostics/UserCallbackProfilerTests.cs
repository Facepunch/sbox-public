using Sandbox.Diagnostics;
using Sandbox.Tasks;
using SceneTests;
using System;
using System.Collections.Generic;
using System.Threading;

namespace EngineTests;

[TestClass, DoNotParallelize]
public class UserCallbackProfilerTests : SceneTest
{
	static readonly ProfileScope Body = new( "Test.UserCallback.Body" );

	Task<Profiler.Capture> capture;

	[TestMethod]
	public void LambdaScopesReadAsTheMethodTheyAreWrittenIn()
	{
		var count = 0;
		Action closure = () => count++;
		Func<int> cached = () => 1;
		int Local() => count;
		Func<int> local = Local;
		Assert.AreEqual( "EngineTests.UserCallbackProfilerTests.LambdaScopesReadAsTheMethodTheyAreWrittenIn (lambda)", ProfileCallbackScopes.MethodName( closure.Method ) );
		Assert.AreEqual( "EngineTests.UserCallbackProfilerTests.LambdaScopesReadAsTheMethodTheyAreWrittenIn (lambda)", ProfileCallbackScopes.MethodName( cached.Method ) );
		Assert.AreEqual( "EngineTests.UserCallbackProfilerTests.LambdaScopesReadAsTheMethodTheyAreWrittenIn.Local (local function)", ProfileCallbackScopes.MethodName( local.Method ) );
		Assert.AreEqual( "EngineTests.UserCallbackProfilerTests.Cleanup()", ProfileCallbackScopes.MethodName( typeof( UserCallbackProfilerTests ).GetMethod( nameof( Cleanup ) ) ) );
	}

	[TestCleanup]
	public void Cleanup()
	{
		Profiler.StopCapture();
		capture?.GetAwaiter().GetResult();
	}

	Profiler.Capture Finish()
	{
		Profiler.StopCapture();
		var result = capture.GetAwaiter().GetResult();
		Assert.AreEqual( 0UL, result.DroppedEvents );
		Assert.AreEqual( 0, result.IncompleteScopes );
		return result;
	}

	[TestMethod]
	public void SystemCallbacksKeepOrderAndExposeNames()
	{
		var list = new TimedCallbackList();
		var calls = new List<int>();
		using var second = list.Add( 20, () => calls.Add( 2 ), "MySystem", "LateUpdate" );
		using var first = list.Add( 10, () => calls.Add( 1 ), "MySystem", "EarlyUpdate" );
		list.Run();
		CollectionAssert.AreEqual( new[] { 1, 2 }, calls );
		calls.Clear();
		capture = Profiler.StartCapture( 10 );
		list.Run();
		CollectionAssert.AreEqual( new[] { 1, 2 }, calls );
		var result = Finish();
		CollectionAssert.AreEquivalent( new[] { "System.MySystem.EarlyUpdate", "System.MySystem.LateUpdate" }, result.Sites.Select( x => x.Name ).ToArray() );
	}

	[TestMethod]
	public void LifecycleCallbacksAreNamedWithoutManualScopes()
	{
		var scene = new Scene();
		using var push = scene.Push();
		try
		{
			capture = Profiler.StartCapture( 10 );
			var probe = scene.CreateObject().Components.Create<CpuLifecycleProbe>();
			scene.RunPendingStarts();
			probe.Enabled = false;
			probe.Destroy();
			scene.ProcessDeletes();
			var names = Finish().Sites.Select( x => x.Name ).ToArray();
			foreach ( var operation in new[] { "OnAwake", "OnEnabled", "OnStart", "OnDisabled", "OnDestroy" } )
				Assert.IsTrue( names.Contains( $"{typeof( CpuLifecycleProbe ).FullName}.{operation}" ), operation );
		}
		finally { scene.Destroy(); }
	}

	static void QueuedWork() { using var cpu = Body.Start(); }

	[TestMethod]
	public void QueuedWorkHasItsMethodAsParent()
	{
		capture = Profiler.StartCapture( 10 );
		Task.Run( () => MainThread.Queue( QueuedWork ) ).GetAwaiter().GetResult();
		MainThread.RunMainThreadQueues();
		var result = Finish();
		var body = result.Spans.Single( x => result.Sites.Single( s => s.Id == x.ScopeId ).Name == "Test.UserCallback.Body" );
		var parent = result.Spans[body.Parent];
		StringAssert.Contains( result.Sites.Single( x => x.Id == parent.ScopeId ).Name, nameof( QueuedWork ) );
	}

	static async Task ResumeAfterSignal( Task signal )
	{
		await signal;
		using var cpu = Body.Start();
	}

	[TestMethod]
	public void AsyncContinuationIsNamedAndEndsBeforeReturningToQueue()
	{
		var context = new ExpirableSynchronizationContext( false );
		var previous = SynchronizationContext.Current;
		var signal = new TaskCompletionSource();
		Task pending;
		try
		{
			SynchronizationContext.SetSynchronizationContext( context );
			pending = ResumeAfterSignal( signal.Task );
		}
		finally { SynchronizationContext.SetSynchronizationContext( previous ); }
		capture = Profiler.StartCapture( 10 );
		signal.SetResult();
		context.ProcessQueue();
		Assert.IsTrue( pending.IsCompletedSuccessfully );
		var result = Finish();
		var site = result.Sites.Single( x => x.Name.EndsWith( $"{nameof( ResumeAfterSignal )}.Continuation" ) );
		var continuation = result.Spans.Single( x => x.ScopeId == site.Id );
		var body = result.Spans.Single( x => result.Sites.Single( s => s.Id == x.ScopeId ).Name == "Test.UserCallback.Body" );
		Assert.AreEqual( continuation.Index, body.Parent );
	}

	[TestMethod]
	public void LocalRpcScopeUsesOriginalMethodName()
	{
		capture = Profiler.StartCapture( 10 );
		Rpc.Resume( new WrappedMethod { TypeName = "MyGame.Inventory", MethodName = "UseItem", Resume = QueuedWork } );
		var result = Finish();
		Assert.IsTrue( result.Sites.Any( x => x.Name == "RPC.MyGame.Inventory.UseItem" ) );
	}

	[TestMethod, TestCategory( Allocations.Category )]
	public void DisabledSystemCallbacksAndWarmedMetadataDoNotAllocate()
	{
		var list = new TimedCallbackList();
		using var registration = list.Add( 0, static () => { }, "Test", "Callback" );
		Allocations.AssertSettles( "Disabled callback profiling", () =>
		{
			for ( int i = 0; i < 100; i++ ) list.Run();
		} );
		Action callback = QueuedWork;
		Allocations.AssertSettles( "Warmed automatic callback metadata", () =>
		{
			for ( int i = 0; i < 100; i++ )
			{
				ProfileCallbackScopes.ForMethod( callback.Method );
				ProfileCallbackScopes.ForType( typeof( CpuLifecycleProbe ), "OnStart" );
				ProfileCallbackScopes.ForRpc( new WrappedMethod { TypeName = "MyGame.Inventory", MethodName = "UseItem", Resume = callback } );
			}
		} );
	}

	[TestMethod]
	public void PanelOverrideReceivesAnAutomaticEventScope()
	{
		var panel = new CpuEventPanel();
		try
		{
			capture = Profiler.StartCapture( 10 );
			panel.DispatchEventImmediate( new Sandbox.UI.PanelEvent( "test", panel ) );
			var result = Finish();
			Assert.AreEqual( 1, panel.Calls );
			Assert.IsTrue( result.Sites.Any( x => x.Name == $"{typeof( CpuEventPanel ).FullName}.OnEvent" ) );
		}
		finally { panel.Delete( true ); }
	}
}

public class CpuLifecycleProbe : Component;

public class CpuEventPanel : Sandbox.UI.Panel
{
	public int Calls;
	protected override void OnEvent( Sandbox.UI.PanelEvent e ) => Calls++;
}
