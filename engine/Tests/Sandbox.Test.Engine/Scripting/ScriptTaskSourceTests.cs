using Sandbox.Engine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace ScriptingTests;

/// <summary>
/// Task sources expose the existing engine API without exposing their private implementation.
/// </summary>
[TestClass, DoNotParallelize]
public class ScriptTaskSourceTests
{
	GlobalContext previous;
	GlobalContext previousGame;

	/// <summary>
	/// Creates an isolated script runtime using the engine's normal catalogue.
	/// </summary>
	[TestInitialize]
	public void Initialize()
	{
		previous = GlobalContext.Current;
		previousGame = GlobalContext.Game;
		GlobalContext.Current = GlobalContext.Game = new GlobalContext { TypeLibrary = SharedTypeLibrary.ScriptingWithSystem };
	}

	/// <summary>
	/// Cancels owned work and restores the previous engine context.
	/// </summary>
	[TestCleanup]
	public void Cleanup()
	{
		GlobalContext.Current.Shutdown();
		GlobalContext.Current = previous;
		GlobalContext.Game = previousGame;
	}

	/// <summary>
	/// Scripts can call nonblocking task helpers through the exposed source.
	/// </summary>
	[TestMethod]
	[DataRow( "return Source.Delay(0);" )]
	[DataRow( "return Source.DelaySeconds(0f);" )]
	[DataRow( "return Source.DelayRealtime(0);" )]
	[DataRow( "return Source.DelayRealtimeSeconds(0f);" )]
	[DataRow( "return Source.WhenAll(new Task[] { Source.CompletedTask });" )]
	[DataRow( "return Source.WhenAny(new Task[] { Source.CompletedTask });" )]
	public void ScriptsCallTaskSource( string code )
	{
		var script = Game.Scripting.CreateScript( code );
		script.SetArgument( "Source", TaskSource.Create() );
		var task = script.RunAndReturn<Task>();
		Assert.IsNotNull( task, script.LastError?.Message );
		Assert.IsTrue( task.IsCompletedSuccessfully, task.Exception?.ToString() );
	}

	/// <summary>
	/// Scripts can create a session-scoped source through its public factory.
	/// </summary>
	[TestMethod]
	public void ScriptsCreateTaskSource()
	{
		var script = Game.Scripting.CreateScript( "return Sandbox.TaskSource.Create().CompletedTask;" );
		var task = script.RunAndReturn<Task>();
		Assert.IsNotNull( task, script.LastError?.Message );
		Assert.IsTrue( task.IsCompletedSuccessfully );
	}

	/// <summary>
	/// Native cancellation remains attached when a delay is invoked from script.
	/// </summary>
	[TestMethod]
	public void ScriptDelayPreservesCancellation()
	{
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		var script = Game.Scripting.CreateScript( "return Source.DelayRealtime(100);" );
		script.SetArgument( "Source", TaskSource.Create( cancellation.Token ) );
		var task = script.RunAndReturn<Task>();
		Assert.IsNotNull( task, script.LastError?.Message );
		Assert.IsTrue( task.IsCanceled );
	}

	/// <summary>
	/// Task sources expose frame and worker APIs while hiding blocking waits and private helpers.
	/// </summary>
	[TestMethod]
	public void PublicSurfaceExcludesPrivateImplementation()
	{
		Assert.IsNotNull( Game.TypeLibrary.GetType( typeof( TaskSource ) ) );
		var members = new List<MemberInfo>();
		new TypeLibraryScriptResolver( Game.TypeLibrary ).GetMembers( typeof( TaskSource ), members );
		foreach ( var name in new[] { "Frame", "FrameEnd", "FixedUpdate", "Yield", "RunInThreadAsync", "WorkerThread" } )
		{
			Assert.IsTrue( members.Any( member => member.Name == name ), name );
		}

		Assert.IsFalse( members.Any( member => member.Name is "WaitAll" or "WaitAny" or "Expire" or "CancelIfInvalid" or "Cancellation" or "_cancellation" or "GetType" ) );
		foreach ( var nested in typeof( TaskSource ).GetNestedTypes( BindingFlags.NonPublic ) )
		{
			Assert.IsNull( Game.TypeLibrary.GetType( nested ), nested.FullName );
		}
	}

	/// <summary>
	/// Script binding still rejects internal state and reflection access.
	/// </summary>
	[TestMethod]
	[DataRow( "Source.Expire();" )]
	[DataRow( "return Source._cancellation;" )]
	[DataRow( "return Source.GetType();" )]
	public void PrivateTaskSourceMembersRemainUnavailable( string code )
	{
		var script = Game.Scripting.CreateScript( code );
		script.SetArgument( "Source", TaskSource.Create() );
		Assert.IsFalse( script.Run() );
		Assert.IsNotNull( script.LastError );
	}

	/// <summary>
	/// UI scripts can inspect completed native tasks without blocking or exposing task schedulers.
	/// </summary>
	[TestMethod]
	public void ScriptsObserveNativeTasks()
	{
		var script = Game.Scripting.CreateScript( "return Work.IsCompleted && Work.IsCompletedSuccessfully && !Work.IsCanceled && !Work.IsFaulted;" );
		script.SetArgument( "Work", Task.FromResult( 42 ) );
		Assert.IsTrue( script.RunAndReturn<bool>(), script.LastError?.Message );

		var fault = Game.Scripting.CreateScript( "return Work.IsFaulted ? Work.Exception.GetBaseException().Message : \"missing\";" );
		fault.SetArgument( "Work", Task.FromException<int>( new InvalidOperationException( "Expected failure" ) ) );
		Assert.AreEqual( "Expected failure", fault.RunAndReturn<string>(), fault.LastError?.Message );
	}

	/// <summary>
	/// A script can cancel and dispose its session-linked request source.
	/// </summary>
	[TestMethod]
	public void ScriptsCancelRequests()
	{
		var script = Game.Scripting.CreateScript( "var source = TaskSource.CreateLinkedTokenSource(); var token = source.Token; source.Cancel(); var cancelled = token.IsCancellationRequested; source.Dispose(); return cancelled;" );
		Assert.IsTrue( script.RunAndReturn<bool>(), script.LastError?.Message );
	}

	/// <summary>
	/// Task observation does not expose blocking waits, scheduling, wait handles or exception reflection.
	/// </summary>
	[TestMethod]
	[DataRow( "return Work.AsyncState;" )]
	[DataRow( "return Work.Result;" )]
	[DataRow( "Work.Wait();" )]
	[DataRow( "Source.WaitAll(new Task[] { Work });" )]
	[DataRow( "Source.WaitAny(new Task[] { Work });" )]
	[DataRow( "return Task.Run(() => 42);" )]
	[DataRow( "return Token.WaitHandle;" )]
	[DataRow( "return Work.Exception.TargetSite;" )]
	public void AsyncObservationKeepsUnapprovedMembersHidden( string code )
	{
		var script = Game.Scripting.CreateScript( code );
		if ( script.HasArgument( "Work" ) ) script.SetArgument( "Work", Task.FromResult( 42 ) );
		if ( script.HasArgument( "Source" ) ) script.SetArgument( "Source", TaskSource.Create() );
		if ( script.HasArgument( "Token" ) ) script.SetArgument( "Token", CancellationToken.None );
		Assert.IsFalse( script.Run() );
		Assert.IsNotNull( script.LastError );
	}

	/// <summary>
	/// Scripts can configure the real AI service without making a paid request.
	/// </summary>
	[TestMethod]
	public void ScriptsConfigureAiChat()
	{
		var script = Game.Scripting.CreateScript( "var config = new Sandbox.Services.Ai.Config { Instructions = \"Test\", MaxToolRounds = 3 }; var chat = config.CreateChat(); return chat.MaxToolRounds;" );
		Assert.AreEqual( 3, script.RunAndReturn<int>(), script.LastError?.Message );
	}
}
