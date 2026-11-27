using Sandbox.Diagnostics;
using Sandbox.Internal;
using SceneTests;
using System;
using System.Collections.Generic;

namespace EngineTests;

[TestClass, DoNotParallelize]
public class ComponentTickProfilerTests : SceneTest
{
	Scene scene;
	Task<Profiler.Capture> capture;

	[TestInitialize]
	public void Initialize()
	{
		Profiler.StopCapture();
		scene = new Scene();
	}

	[TestCleanup]
	public void Cleanup()
	{
		Profiler.StopCapture();
		capture?.GetAwaiter().GetResult();
		scene.Destroy();
	}

	void Tick( string phase )
	{
		switch ( phase )
		{
			case "Update": scene.InternalUpdate(); break;
			case "FixedUpdate": scene.InternalFixedUpdate(); break;
			case "PreRender": scene.PreRender(); break;
			default: throw new ArgumentOutOfRangeException( nameof( phase ) );
		}
	}

	T Add<T>( int id, List<int> calls ) where T : CpuTickProbe, new()
	{
		var probe = scene.CreateObject().Components.Create<T>();
		probe.TickId = id;
		probe.Calls = calls;
		return probe;
	}

	[TestMethod]
	[DataRow( "Update" )]
	[DataRow( "FixedUpdate" )]
	[DataRow( "PreRender" )]
	public void RecordingPreservesOrderAndNestsEachTick( string phase )
	{
		using var push = scene.Push();
		var calls = new List<int>();
		Add<CpuTickProbe>( 1, calls );
		Add<OtherCpuTickProbe>( 2, calls );
		Add<CpuTickProbe>( 3, calls );
		Tick( phase );
		CollectionAssert.AreEqual( new[] { 1, 2, 3 }, calls );
		calls.Clear();

		capture = Profiler.StartCapture( 10 );
		Tick( phase );
		Profiler.StopCapture();
		var result = capture.GetAwaiter().GetResult();
		CollectionAssert.AreEqual( new[] { 1, 2, 3 }, calls );
		Assert.AreEqual( 0UL, result.DroppedEvents );
		Assert.AreEqual( 0, result.IncompleteScopes );
		var names = result.Sites.ToDictionary( x => x.Id, x => x.Name );
		var ticks = result.Spans.Where( x => names[x.ScopeId] == $"{typeof( CpuTickProbe ).FullName}.{phase}"
			|| names[x.ScopeId] == $"{typeof( OtherCpuTickProbe ).FullName}.{phase}" ).ToArray();
		CollectionAssert.AreEqual( new[] { $"{typeof( CpuTickProbe ).FullName}.{phase}", $"{typeof( OtherCpuTickProbe ).FullName}.{phase}",
			$"{typeof( CpuTickProbe ).FullName}.{phase}" }, ticks.Select( x => names[x.ScopeId] ).ToArray() );
		var children = result.Spans.Where( x => names[x.ScopeId] == "Test.ComponentTick.Body" ).ToArray();
		CollectionAssert.AreEqual( ticks.Select( x => x.Index ).ToArray(), children.Select( x => x.Parent ).ToArray() );
	}

	[TestMethod]
	public void CaptureLimitDoesNotStopRemainingTicks()
	{
		using var push = scene.Push();
		var calls = new List<int>();
		for ( int i = 0; i < 600; i++ ) Add<CpuTickProbe>( i, calls );
		capture = Profiler.StartCapture( 10, 64, collector: false );
		scene.InternalUpdate();
		Profiler.Update();
		Assert.IsFalse( Profiler.IsRecording );
		Assert.IsTrue( capture.GetAwaiter().GetResult().EventLimitReached );
		CollectionAssert.AreEqual( Enumerable.Range( 0, 600 ).ToArray(), calls );
		calls.Clear();
		scene.InternalUpdate();
		CollectionAssert.AreEqual( Enumerable.Range( 0, 600 ).ToArray(), calls );
	}
}

public class CpuTickProbe : Component, IUpdateSubscriber, IFixedUpdateSubscriber, IPreRenderSubscriber
{
	static readonly ProfileScope Body = new( "Test.ComponentTick.Body" );

	public int TickId;
	public List<int> Calls;
	void Tick()
	{
		using var body = Body.Start();
		Calls?.Add( TickId );
	}
	protected override void OnUpdate() => Tick();
	protected override void OnFixedUpdate() => Tick();
	protected override void OnPreRender() => Tick();
}

public class OtherCpuTickProbe : CpuTickProbe;
