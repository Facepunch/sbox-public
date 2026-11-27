using Sandbox.Diagnostics;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using NativeProfiler = NativeEngine.Profiler;

namespace EngineTests;

// The recorder is process-wide; other profiler tests must not change sessions while a test owns it.
[TestClass, DoNotParallelize]
public class ProfilerTests
{
	static System.Threading.Tasks.Task<Profiler.Capture> StartCapture( float seconds = 1, int eventLimit = Profiler.MaxEvents ) =>
		Profiler.StartCapture( seconds, eventLimit, collector: false );

	static readonly ProfileScope Parent = new( "Test.Profiler.Parent" );
	static readonly ProfileScope Child = new( "Test.Profiler.Child" );
	static readonly ProfileScope Worker = new( "Test.Profiler.Worker" );

	static Profiler.Capture ExampleCapture( bool incomplete = false, ulong dropped = 0 ) => new(
		1, 20, dropped, false, incomplete ? 1 : 0, [], [],
		[
			new( 0, 1, 1, -1, 0, 0, 10, incomplete ? null : 2, incomplete ),
			new( 1, 2, 1, 0, 1, 1, 7, 3, false ),
			new( 2, 1, 1, 1, 2, 2, 5, 3, false ),
			new( 3, 3, 1, 0, 1, 7, 9, 2, false ),
			new( 4, 2, 2, -1, 0, 0, 20, 20, false )
		], [] );

	[TestMethod]
	public void CallTreeKeepsRecursionAndExcludesOtherThreads()
	{
		var rows = Profiler.CallTree( ExampleCapture(), 0 );
		CollectionAssert.AreEqual( new[] { 0, 1, 2, 3 }, rows.Select( x => x.SpanIndex ).ToArray() );
		CollectionAssert.AreEqual( new double?[] { 100, 60, 30, 20 }, rows.Select( x => x.PercentOfRoot ).ToArray() );
		CollectionAssert.AreEqual( new double?[] { 100, 60, 50, 20 }, rows.Select( x => x.PercentOfParent ).ToArray() );
		Assert.AreEqual( 2, Profiler.CallTree( ExampleCapture(), 1 ).Length );
		Assert.AreEqual( 2, Profiler.CallTree( ExampleCapture(), 0, 2 ).Length );
		Profiler.CallTree( ExampleCapture(), 0, 4, 0, out var exactLimit );
		Assert.IsFalse( exactLimit );
		Profiler.CallTree( ExampleCapture(), 0, 3, 0, out var truncated );
		Assert.IsTrue( truncated );
	}

	[TestMethod]
	public void TreeLimitPreservesSiblingsBeforeDeeperDescendants()
	{
		var rows = Profiler.CallTree( ExampleCapture(), 0, 3, 0, out var truncated );
		CollectionAssert.AreEqual( new[] { 0, 1, 3 }, rows.Select( x => x.SpanIndex ).ToArray() );
		Assert.IsTrue( truncated );
		CollectionAssert.AreEqual( new double?[] { 100, 60, 20 }, rows.Select( x => x.PercentOfRoot ).ToArray() );
		var rootOnly = Profiler.CallTree( ExampleCapture(), 0, 1, 0, out truncated );
		Assert.AreEqual( 1, rootOnly.Length );
		Assert.IsTrue( truncated );
		var leaf = Profiler.CallTree( ExampleCapture(), 3, 1, 0, out truncated );
		Assert.AreEqual( 1, leaf.Length );
		Assert.IsFalse( truncated );
	}

	[TestMethod]
	public void AnalysisDoesNotInventSelfTimeOrPercentagesForMissingData()
	{
		var rows = Profiler.CallTree( ExampleCapture( incomplete: true ), 0 );
		Assert.IsTrue( rows.All( x => x.PercentOfRoot is null ) );
		Assert.IsNull( rows[1].PercentOfParent );
		Assert.AreEqual( 50d, rows[2].PercentOfParent );
		CollectionAssert.AreEqual( new[] { 0, 1, 2, 3 }, Profiler.CallTree( ExampleCapture( dropped: 1 ), 0 ).Select( x => x.SpanIndex ).ToArray() );
		var costs = Profiler.CostsInRange( ExampleCapture(), 2, 5, 1 );
		Assert.AreEqual( 6d, costs.Single( x => x.ScopeId == 1 ).InclusiveMs );
		Assert.IsTrue( costs.All( x => x.SelfMs is null ) );
	}

	[TestMethod]
	public void UnknownSelfTimeDoesNotOutrankMeasuredSelfTime()
	{
		var costs = Profiler.CostsInRange( ExampleCapture( incomplete: true ), 0, 20 );
		CollectionAssert.AreEqual( new uint[] { 2, 3, 1 }, costs.Select( x => x.ScopeId ).ToArray() );
		Assert.IsNull( costs[^1].SelfMs );
		Assert.IsTrue( costs[^1].InclusiveMs > costs[1].SelfMs.Value );
	}

	[TestMethod]
	public void DurationHistogramIncludesMaximumAndUsesNearestRankPercentiles()
	{
		var stats = Profiler.DescribeDurations( Enumerable.Range( 1, 100 ).Select( x => (double)x ).Concat( [double.NaN, double.PositiveInfinity, -1] ), 10 );
		Assert.AreEqual( 100, stats.Count );
		Assert.AreEqual( 100, stats.Bins.Sum() );
		Assert.AreEqual( 11, stats.Bins[^1] );
		Assert.AreEqual( 50d, stats.P50Ms );
		Assert.AreEqual( 95d, stats.P95Ms );
		Assert.AreEqual( 99d, stats.P99Ms );
		Assert.AreEqual( 0, Profiler.DescribeDurations( [] ).Count );
	}

	[TestInitialize]
	public void Initialize()
	{
		ThreadSafe.MarkMainThread();
		Profiler.StopCapture();
		Profiler.PendingCompletion?.GetAwaiter().GetResult();
		NativeProfiler.Stop();
	}

	[TestCleanup]
	public void Cleanup()
	{
		Profiler.StopCapture();
		Profiler.PendingCompletion?.GetAwaiter().GetResult();
		NativeProfiler.Stop();
	}

	static Profiler.Span Named( Profiler.Capture capture, string name )
	{
		var id = capture.Sites.Single( x => x.Name == name ).Id;
		return capture.Spans.Single( x => x.ScopeId == id );
	}

	[TestMethod]
	public void ManagedNativeManagedScopesShareTheSameTreeAndClock()
	{
		var nativeSite = NativeProfiler.RegisterScope( "Test.Profiler.Native", "test.cpp", 1 );
		var task = StartCapture( 2 );
		using ( Parent.Start() )
		{
			var native = NativeProfiler.Begin( nativeSite );
			try { using var sample = Child.Start(); }
			finally { NativeProfiler.End( native ); }
		}
		Profiler.StopCapture();
		var capture = task.GetAwaiter().GetResult();
		var parent = Named( capture, "Test.Profiler.Parent" );
		var middle = Named( capture, "Test.Profiler.Native" );
		var child = Named( capture, "Test.Profiler.Child" );
		Assert.AreEqual( 0UL, capture.DroppedEvents );
		Assert.AreEqual( 0, capture.IncompleteScopes );
		Assert.AreEqual( -1, parent.Parent );
		Assert.AreEqual( parent.Index, middle.Parent );
		Assert.AreEqual( middle.Index, child.Parent );
		Assert.AreEqual( parent.ThreadKey, child.ThreadKey );
		Assert.IsTrue( parent.StartMs <= middle.StartMs && middle.StartMs <= child.StartMs );
		Assert.IsTrue( child.EndMs <= middle.EndMs && middle.EndMs <= parent.EndMs );
		Assert.AreEqual( parent.DurationMs, parent.SelfMs.Value + middle.DurationMs, 0.000001 );
		Assert.AreEqual( middle.DurationMs, middle.SelfMs.Value + child.DurationMs, 0.000001 );
	}

	[TestMethod]
	public void CaptureBoundaryClipsOpenScopesAndDoesNotCarryParentsIntoNextCapture()
	{
		var first = StartCapture( 2 );
		var parent = Parent.Start();
		using ( Child.Start() ) { }
		Profiler.StopCapture();
		var capture = first.GetAwaiter().GetResult();
		var clipped = Named( capture, "Test.Profiler.Parent" );
		Assert.IsTrue( clipped.Incomplete );
		Assert.IsNull( clipped.SelfMs );
		Assert.AreEqual( capture.DurationMs, clipped.EndMs );

		var second = StartCapture( 2 );
		parent.Dispose();
		using ( Child.Start() ) { }
		Profiler.StopCapture();
		var next = second.GetAwaiter().GetResult();
		Assert.AreEqual( -1, Named( next, "Test.Profiler.Child" ).Parent );
		Assert.AreEqual( 0, next.IncompleteScopes );
	}

	[TestMethod]
	public void RecursionAndExceptionUnwindingRemainBalanced()
	{
		var task = StartCapture( 2 );
		using ( Parent.Start() )
		{
			try
			{
				using var recursive = Parent.Start();
				using var child = Child.Start();
				throw new InvalidOperationException( "Expected unwind" );
			}
			catch ( InvalidOperationException ) { }
			using var sibling = Child.Start();
		}
		Profiler.StopCapture();
		var capture = task.GetAwaiter().GetResult();
		CollectionAssert.AreEqual( new[] { -1, 0, 1, 0 }, capture.Spans.Select( x => x.Parent ).ToArray() );
		Assert.AreEqual( 0, capture.IncompleteScopes );
		Assert.IsTrue( capture.Spans.All( x => x.SelfMs >= 0 ) );
	}

	[TestMethod]
	public void WorkerThreadsHaveIndependentNesting()
	{
		var task = StartCapture( 2 );
		var threads = new Thread[4];
		for ( int n = 0; n < threads.Length; n++ )
		{
			threads[n] = new Thread( () =>
			{
				for ( int i = 0; i < 20; i++ )
				{
					using var parent = Worker.Start();
					using var child = Child.Start();
				}
			} );
			threads[n].Name = $"Test worker {n}";
		}
		foreach ( var thread in threads ) thread.Start();
		foreach ( var thread in threads ) Assert.IsTrue( thread.Join( TimeSpan.FromSeconds( 10 ) ) );
		Profiler.StopCapture();
		var capture = task.GetAwaiter().GetResult();
		Assert.AreEqual( 160, capture.Spans.Length );
		CollectionAssert.AreEquivalent( Enumerable.Range( 0, 4 ).Select( i => $"Test worker {i}" ).ToArray(), capture.Threads.Select( x => x.Name ).ToArray() );
		Assert.AreEqual( 0UL, capture.DroppedEvents );
		foreach ( var span in capture.Spans.Where( x => x.Parent >= 0 ) )
			Assert.AreEqual( span.ThreadKey, capture.Spans[span.Parent].ThreadKey );
	}

	[TestMethod]
	public void RingOverflowPreservesOtherThreadsAndRecoversAfterTheGap()
	{
		var outer = new ProfileScope( "Test.Profiler.OverflowOuter" );
		var recoveredParent = new ProfileScope( "Test.Profiler.RecoveredParent" );
		var recoveredChild = new ProfileScope( "Test.Profiler.RecoveredChild" );
		using var flooded = new ManualResetEventSlim();
		using var resume = new ManualResetEventSlim();
		var task = StartCapture( 2 );
		using ( Parent.Start() ) { using ( Child.Start() ) { } }
		Profiler.Update();
		var worker = Task.Run( () =>
		{
			using ( outer.Start() )
			{
				for ( int i = 0; i < Profiler.RingCapacity; i++ ) { using var scope = Worker.Start(); }
				flooded.Set();
				resume.Wait();
				using ( recoveredParent.Start() ) { using ( recoveredChild.Start() ) { } }
			}
		} );
		try
		{
			Assert.IsTrue( flooded.Wait( TimeSpan.FromSeconds( 5 ) ) );
			Profiler.Update();
			using ( Parent.Start() ) { using ( Child.Start() ) { } }
		}
		finally { resume.Set(); worker.GetAwaiter().GetResult(); }
		Profiler.StopCapture();
		var capture = task.GetAwaiter().GetResult();
		Assert.IsTrue( capture.DroppedEvents > 0 );
		Assert.IsFalse( capture.HierarchyReliable );
		var main = capture.Spans.Where( x => x.ThreadKey == capture.Spans[0].ThreadKey ).ToArray();
		Assert.AreEqual( 4, main.Length );
		Assert.IsTrue( main.All( x => !x.Incomplete && x.SelfMs.HasValue ) );
		Assert.AreEqual( main[0].Index, main[1].Parent );
		Assert.AreEqual( main[2].Index, main[3].Parent );
		var clipped = Named( capture, "Test.Profiler.OverflowOuter" );
		Assert.IsTrue( clipped.Incomplete );
		Assert.IsNull( clipped.SelfMs );
		var parent = Named( capture, "Test.Profiler.RecoveredParent" );
		var child = Named( capture, "Test.Profiler.RecoveredChild" );
		Assert.AreEqual( -1, parent.Parent );
		Assert.AreEqual( parent.Index, child.Parent );
		Assert.IsFalse( parent.Incomplete );
		Assert.IsNotNull( parent.SelfMs );
		Assert.AreEqual( 2, Profiler.CallTree( capture, parent.Index ).Length );
	}

	[TestMethod]
	public void DeadlineWaitsForAFrameBoundaryInsteadOfClippingAnInFrameDrain()
	{
		var task = StartCapture( 0.05f );
		using ( Parent.Start() )
		{
			Thread.Sleep( 60 );
			Profiler.Update();
			Assert.IsTrue( Profiler.IsRecording );
			using ( Child.Start() ) { }
		}
		Profiler.Update( frameBoundary: true );
		Assert.IsFalse( Profiler.IsRecording );
		var capture = task.GetAwaiter().GetResult();
		Assert.AreEqual( 0, capture.IncompleteScopes );
		Assert.IsNotNull( Named( capture, "Test.Profiler.Parent" ).SelfMs );
	}

	[TestMethod]
	public void EventLimitStopsCaptureAndNextCaptureRecovers()
	{
		const int eventLimit = 4000;
		var task = StartCapture( 10, eventLimit );
		for ( int batch = 0; batch < 100 && Profiler.IsRecording; batch++ )
		{
			for ( int i = 0; i < 1000; i++ ) { using ( Parent.Start() ) { using ( Child.Start() ) { } } }
			Profiler.Update();
		}
		Assert.IsFalse( Profiler.IsRecording, "The event limit must stop recording without waiting for the deadline." );
		var capture = task.GetAwaiter().GetResult();
		Assert.IsTrue( capture.EventLimitReached );
		Assert.IsFalse( Profiler.IsRecording );
		Assert.IsTrue( capture.Spans.Length <= eventLimit / 2 );
		Assert.AreEqual( 0UL, capture.DroppedEvents );
		Assert.IsTrue( capture.Spans.Any( x => x.SelfMs.HasValue ) );
		Assert.AreEqual( capture.Spans[0].Index, capture.Spans[1].Parent );
		Assert.AreEqual( 2, Profiler.CallTree( capture, 0 ).Length );

		var next = StartCapture( 2 );
		using ( Child.Start() ) { }
		Profiler.StopCapture();
		var recovered = next.GetAwaiter().GetResult();
		Assert.IsTrue( recovered.HierarchyReliable );
		Assert.AreEqual( 1, recovered.Spans.Length );
		Assert.IsFalse( Named( recovered, "Test.Profiler.Child" ).Incomplete );
	}

	[TestMethod]
	public void OnlyFourCapturesAreRetained()
	{
		var ids = new List<long>();
		for ( int i = 0; i < 5; i++ )
		{
			var task = StartCapture( 2 );
			using ( Parent.Start() ) { }
			Profiler.StopCapture();
			ids.Add( task.GetAwaiter().GetResult().Id );
		}
		Assert.IsNull( Profiler.Find( ids[0] ) );
		Assert.IsNotNull( Profiler.Find( ids[1] ) );
		Assert.AreEqual( ids[^1], Profiler.Find( 0 ).Id );
	}

	[TestMethod, TestCategory( Allocations.Category )]
	public void WarmedScopesDoNotAllocate()
	{
		StartCapture( 2 );
		Allocations.AssertSettles( "Cached profile scope begin/end", () =>
		{
			for ( int i = 0; i < 100; i++ ) { using var scope = Parent.Start(); }
		}, beforePass: () =>
		{
			Profiler.Update();
			if ( !Profiler.IsRecording ) StartCapture( 2 );
		} );
		Profiler.StopCapture();
		Allocations.AssertSettles( "Disabled profile scopes", () =>
		{
			for ( int i = 0; i < 100; i++ ) { using var scope = Parent.Start(); }
		} );
	}

	[TestMethod]
	public void LargerRingDrainsABurstBeyondTheOldCapacity()
	{
		var task = StartCapture( 2 );
		for ( int i = 0; i < 12_000; i++ ) { using var scope = Parent.Start(); }
		Profiler.StopCapture();
		var capture = task.GetAwaiter().GetResult();
		Assert.AreEqual( 12_000, capture.Spans.Length );
		Assert.AreEqual( 0UL, capture.DroppedEvents );
		Assert.AreEqual( 0, capture.IncompleteScopes );
	}

	[TestMethod]
	public void CaptureBudgetReservesFinalizationAndRetainedCaptures()
	{
		Assert.AreEqual( 48, Marshal.SizeOf<Profiler.Span>() );
		const long budget = 2L * 1024 * 1024 * 1024;
		var empty = Profiler.CaptureInstanceLimit( 0, budget );
		var withPrevious = Profiler.CaptureInstanceLimit( empty, budget );
		Assert.AreEqual( 16_777_216, empty );
		Assert.IsTrue( withPrevious < empty );
		Assert.IsTrue( 256L * 1024 * 1024 + empty * 64L + withPrevious * 112L <= budget );
	}

	[TestMethod]
	public void CollectorFailureFaultsTheTaskWithoutBreakingTheEngineLoop()
	{
		var task = StartCapture( 1 );
		var builder = typeof( Profiler ).GetField( "_builder", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic ).GetValue( null );
		builder.GetType().GetField( "CollectorError" ).SetValue( builder, new InvalidOperationException( "Injected collector failure" ) );
		Profiler.Update();
		Assert.IsFalse( Profiler.IsRecording );
		Assert.ThrowsException<InvalidOperationException>( () => task.GetAwaiter().GetResult() );
		var next = StartCapture( 1 );
		using ( Parent.Start() ) { }
		Profiler.StopCapture();
		Assert.AreEqual( 1, next.GetAwaiter().GetResult().Spans.Length );
	}

	[TestMethod]
	public void OpenEndedCaptureRecordsUntilStoppedOrTheLimit()
	{
		var task = Profiler.StartCapture( float.PositiveInfinity, collector: false );
		Assert.IsTrue( Profiler.RecordingIsOpenEnded );
		using ( Parent.Start() ) { }
		Profiler.Update( frameBoundary: true );
		Assert.IsTrue( Profiler.IsRecording );
		Profiler.StopCapture();
		Assert.AreEqual( 1, task.GetAwaiter().GetResult().Spans.Length );

		var limited = Profiler.StartCapture( float.PositiveInfinity, eventLimit: 64, collector: false );
		for ( int i = 0; i < 1000; i++ ) { using var scope = Parent.Start(); }
		Profiler.Update( frameBoundary: true );
		Assert.IsFalse( Profiler.IsRecording );
		Assert.IsTrue( limited.GetAwaiter().GetResult().EventLimitReached );
	}

	[TestMethod]
	public void BackgroundLimitStopsEmissionBeforeTheNextFrame()
	{
		var task = Profiler.StartCapture( 10, eventLimit: 64 );
		for ( int i = 0; i < 1000; i++ ) { using var scope = Parent.Start(); }
		Assert.IsTrue( SpinWait.SpinUntil( () => !Profiler.IsRecording, TimeSpan.FromSeconds( 5 ) ) );
		var dropped = NativeProfiler.DroppedEvents();
		for ( int i = 0; i < 100_000; i++ ) { using var scope = Parent.Start(); }
		Assert.AreEqual( dropped, NativeProfiler.DroppedEvents() );
		Profiler.StopCapture();
		var capture = task.GetAwaiter().GetResult();
		Assert.IsTrue( capture.EventLimitReached );
		Assert.AreEqual( 0UL, capture.DroppedEvents );
	}

	static Profiler.Capture FileCapture()
	{
		var capture = ExampleCapture() with
		{
			Sites = [new( 1, "Engine.Frame", "frame.cs", 1 ), new( 2, "Child", "child.cs", 2 ), new( 3, "Sibling", "child.cs", 3 )],
			Threads = [new( 1, 101, "Main" ), new( 2, 102, "Worker" )]
		};
		return capture with { ThreadSpans = new() { [1] = new( capture.Spans, 0, 4 ), [2] = new( capture.Spans, 4, 1 ) } };
	}

	static byte[] FileBytes()
	{
		using var stream = new System.IO.MemoryStream();
		Profiler.WriteCapture( FileCapture(), stream );
		return stream.ToArray();
	}

	[TestMethod]
	public void BinaryCaptureRoundTripPreservesScopesAndAnalysis()
	{
		var original = FileCapture();
		using var stream = new System.IO.MemoryStream( FileBytes() );
		var loaded = Profiler.ReadCapture( stream );
		CollectionAssert.AreEqual( original.Spans, loaded.Spans );
		CollectionAssert.AreEqual( original.Sites, loaded.Sites );
		CollectionAssert.AreEqual( original.Threads, loaded.Threads );
		CollectionAssert.AreEqual( Profiler.CallTree( original, 0 ), Profiler.CallTree( loaded, 0 ) );
		CollectionAssert.AreEqual( Profiler.CostsInRange( original, 2, 9 ), Profiler.CostsInRange( loaded, 2, 9 ) );
		Assert.AreEqual( 2, loaded.Frames.Length );
		Assert.AreSame( loaded.Spans, loaded.ThreadSpans[1].Array );
	}

	[TestMethod]
	public void BinaryCaptureRejectsTruncationBadHeadersAndInvalidParents()
	{
		void Invalid( byte[] bytes ) => Assert.ThrowsException<System.IO.InvalidDataException>( () => Profiler.ReadCapture( new System.IO.MemoryStream( bytes ) ) );
		var bytes = FileBytes();
		Invalid( bytes[..^1] );
		var bad = bytes.ToArray(); bad[0] ^= 1; Invalid( bad );
		bad = bytes.ToArray(); BitConverter.GetBytes( int.MaxValue ).CopyTo( bad, 64 + 36 ); Invalid( bad );
		bad = bytes.ToArray(); BitConverter.GetBytes( double.NaN ).CopyTo( bad, 64 + 16 ); Invalid( bad );
		var spanOffset = bytes.Length - 44 - 5 * 48;
		bad = bytes.ToArray(); BitConverter.GetBytes( 4 ).CopyTo( bad, spanOffset + 48 + 40 ); Invalid( bad );
		bad = bytes.ToArray(); bad[spanOffset + 46] = 2; Invalid( bad );
		bad = bytes.ToArray(); BitConverter.GetBytes( double.NaN ).CopyTo( bad, spanOffset ); Invalid( bad );
		bad = bytes.ToArray(); BitConverter.GetBytes( 999u ).CopyTo( bad, spanOffset + 36 ); Invalid( bad );
		Assert.ThrowsException<System.IO.InvalidDataException>( () => Profiler.ReadCapture( new System.IO.MemoryStream( bytes ), maximumSpans: 4 ) );
	}

	[TestMethod]
	public void UnknownTrackSectionsAreSkipped()
	{
		var bytes = FileBytes();
		var oldDirectory = BitConverter.ToInt64( bytes, 32 );
		using var stream = new System.IO.MemoryStream();
		using var writer = new System.IO.BinaryWriter( stream, System.Text.Encoding.UTF8, true );
		writer.Write( bytes, 0, (int)oldDirectory );
		writer.Write( 16u ); writer.Write( 7u ); writer.Write( 8L );
		var unknownOffset = stream.Position;
		writer.Write( 123L );
		var directory = stream.Position;
		writer.Write( 0xffffu ); writer.Write( 1u ); writer.Write( 52L ); writer.Write( 2 );
		writer.Write( bytes, (int)oldDirectory + 20, 24 );
		writer.Write( 16u ); writer.Write( 7u ); writer.Write( unknownOffset ); writer.Write( 8L );
		stream.Position = 32; writer.Write( directory ); stream.Position = 0;
		CollectionAssert.AreEqual( FileCapture().Spans, Profiler.ReadCapture( stream ).Spans );
	}

	[TestMethod]
	public void StreamContinuesAfterRamLimitAndLoadsIndexedWindows()
	{
		string path = null;
		try
		{
			var task = Profiler.StartCapture( 10, eventLimit: 64, collector: false, streamToDisk: true );
			using ( Parent.Start() )
			{
				for ( int batch = 0; batch < 10; batch++ )
				{
					for ( int i = 0; i < 1000; i++ ) { using var child = Child.Start(); }
					Profiler.Update();
				}
			}
			Profiler.StopCapture();
			var preview = task.GetAwaiter().GetResult();
			Assert.IsTrue( preview.EventLimitReached );
			Assert.IsTrue( preview.Spans.Length <= 32 );
			Assert.IsNull( preview.StorageError, preview.StorageError );
			Assert.IsNotNull( preview.SourceFile );
			path = Profiler.CapturePath( preview.SourceFile );
			var loaded = Profiler.LoadCapture( preview.SourceFile ).GetAwaiter().GetResult();
			Assert.AreEqual( 10_001, loaded.Spans.Length );
			Assert.IsFalse( loaded.EventLimitReached );
			Assert.AreEqual( 0, loaded.IncompleteScopes );
			Assert.AreEqual( 0UL, loaded.DroppedEvents );
			var start = loaded.Spans[5000].StartMs;
			var end = loaded.Spans[5500].EndMs;
			var window = Profiler.LoadCapture( preview.SourceFile, start, end - start ).GetAwaiter().GetResult();
			Assert.IsTrue( window.Spans.Length is > 1 and < 1000 );
			Assert.AreEqual( start, window.SourceOffsetMs );
			Assert.IsTrue( window.Spans[0].Incomplete, "The checkpoint restores the parent crossing the window start." );
			Assert.IsTrue( window.Spans.Skip( 1 ).All( x => x.Parent == 0 ) );
			Assert.IsTrue( window.Spans.All( x => x.EndMs <= window.DurationMs ) );
		}
		finally { if ( path is not null ) System.IO.File.Delete( path ); }
	}

	[TestMethod]
	public void StreamWriteFailureReleasesAndDeletesThePartialFile()
	{
		var task = Profiler.StartCapture( 10, collector: false, streamToDisk: true );
		var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
		var builder = typeof( Profiler ).GetField( "_builder", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic ).GetValue( null );
		var archive = builder.GetType().GetField( "Archive" ).GetValue( builder );
		var partial = (string)archive.GetType().GetField( "_temporary", flags ).GetValue( archive );
		((System.IO.Stream)archive.GetType().GetField( "_stream", flags ).GetValue( archive )).Dispose();
		using ( Parent.Start() ) { }
		Profiler.Update();
		Profiler.StopCapture();
		var capture = task.GetAwaiter().GetResult();
		Assert.IsNotNull( capture.StorageError );
		Assert.IsNull( capture.SourceFile );
		Assert.IsFalse( System.IO.File.Exists( partial ) );
	}

	[TestMethod]
	[DataRow( false, false )]
	[DataRow( true, false )]
	[DataRow( false, true )]
	public void StreamCheckpointRestoresQuietScopesAndToleratesLateEvents( bool lateEvent, bool stopped )
	{
		var nested = System.Reflection.BindingFlags.NonPublic;
		var writerType = typeof( Profiler ).GetNestedType( "ArchiveWriter", nested );
		var eventType = typeof( Profiler ).GetNestedType( "NativeEvent", nested );
		var threadType = typeof( Profiler ).GetNestedType( "NativeThread", nested );
		var writer = Activator.CreateInstance( writerType, 1000UL );
		var thread = Activator.CreateInstance( threadType );
		threadType.GetField( "Key" ).SetValue( thread, 1UL ); threadType.GetField( "Id" ).SetValue( thread, 101UL );
		var events = Array.CreateInstance( eventType, lateEvent ? 2 : 1 );
		for ( int i = 0; i < events.Length; i++ )
		{
			var e = Activator.CreateInstance( eventType );
			eventType.GetField( "Ticks" ).SetValue( e, (ulong)(1000 + i * 1000) );
			eventType.GetField( "Token" ).SetValue( e, (ulong)(i + 1) );
			eventType.GetField( "Scope" ).SetValue( e, (uint)(i + 1) );
			eventType.GetField( "Session" ).SetValue( e, 1u ); events.SetValue( e, i );
		}
		Assert.IsTrue( (bool)writerType.GetMethod( "Enqueue" ).Invoke( writer, [thread, events, events.Length] ) );
		if ( stopped )
		{
			var stop = writerType.GetMethod( "RequestStop", System.Reflection.BindingFlags.Instance | nested );
			stop.Invoke( writer, ["disk size limit", 1300UL] );
			var rejected = events.GetValue( 0 );
			eventType.GetField( "Ticks" ).SetValue( rejected, 1200UL ); events.SetValue( rejected, 0 );
			Assert.IsFalse( (bool)writerType.GetMethod( "Enqueue" ).Invoke( writer, [thread, events, events.Length] ) );
			stop.Invoke( writer, ["disk writer backlog", 1400UL] );
		}
		var metadata = new Profiler.Capture( 0, 1500, 0, false, 0, [new( 1, 101, "Main" )], [new( 1, "Parent", "", 0 ), new( 2, "Child", "", 0 )], [], [] )
		{ ThreadSpans = new() { [1] = ArraySegment<Profiler.Span>.Empty } };
		var name = (string)writerType.GetMethod( "Finish" ).Invoke( writer, [metadata, 1500UL] );
		try
		{
			var whole = Profiler.LoadCapture( name ).GetAwaiter().GetResult();
			Assert.AreEqual( 1, whole.Spans.Length );
			Assert.AreEqual( stopped ? 1200d : 1500d, whole.DurationMs );
			if ( stopped ) Assert.AreEqual( "disk size limit", whole.LimitReason );
			var window = Profiler.LoadCapture( name, 1100, 100 ).GetAwaiter().GetResult();
			Assert.AreEqual( 1, window.Spans.Length );
			Assert.AreEqual( 0d, window.Spans[0].StartMs );
			Assert.AreEqual( 100d, window.Spans[0].EndMs );
			Assert.IsTrue( window.Spans[0].Incomplete );
		}
		finally { System.IO.File.Delete( Profiler.CapturePath( name ) ); }
	}

	[TestMethod]
	public void CaptureNamesCannotEscapeTheProfilesDirectory()
	{
		foreach ( var name in new[] { "", "../outside", "C:/outside", "a\\b", ".hidden", "a:b" } )
			Assert.ThrowsException<ArgumentException>( () => Profiler.CapturePath( name ) );
		Assert.IsTrue( Profiler.CapturePath( "example" ).EndsWith( "example.sprof" ) );
	}

	[StructLayout( LayoutKind.Sequential )]
	struct Event { public ulong Ticks, Token; public uint Scope, Session; }
	[StructLayout( LayoutKind.Sequential )]
	unsafe struct ThreadInfo { public ulong Id, Key; public fixed byte Name[64]; }

	[TestMethod]
	public unsafe void RetiredThreadSlotsAreReusedAfterDrain()
	{
		var events = new Event[8192];
		var keys = new HashSet<ulong>();
		var site = NativeProfiler.RegisterScope( "Test.Profiler.RetiredThread", "test.cpp", 2 );
		var session = NativeProfiler.Start();
		var dropped = NativeProfiler.DroppedEvents();
		for ( int i = 0; i < NativeProfiler.ThreadSlots() + 8; i++ )
		{
			var thread = new Thread( () => { NativeProfiler.AttachThread(); var token = NativeProfiler.Begin( site ); NativeProfiler.End( token ); } );
			thread.Start();
			Assert.IsTrue( thread.Join( TimeSpan.FromSeconds( 10 ) ) );
			fixed ( Event* buffer = events )
			{
				for ( int slot = 0; slot < NativeProfiler.ThreadSlots(); slot++ )
				{
					ThreadInfo info = default;
					var count = NativeProfiler.Drain( slot, (IntPtr)buffer, events.Length, (IntPtr)(&info) );
					if ( count > 0 && events.Take( count ).Any( x => x.Session == session && x.Scope == site ) ) keys.Add( info.Key );
				}
			}
		}
		NativeProfiler.Stop();
		Assert.AreEqual( NativeProfiler.ThreadSlots() + 8, keys.Count );
		Assert.AreEqual( dropped, NativeProfiler.DroppedEvents() );
	}
}
