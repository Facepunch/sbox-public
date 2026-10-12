using Sandbox.Engine;
using Sandbox.Internal;
using System;
using System.Collections.Generic;
using System.Threading;

namespace SystemTests;

/// <summary>
/// Main-thread callbacks must retain their own reflection library when another context drains them.
/// </summary>
[TestClass]
[DoNotParallelize]
public class MainThreadContextTests
{
	/// <summary>
	/// Menu types stay inaccessible to queued game callbacks, including with flow suppressed.
	/// Trusted menu callbacks also retain their library when drained from game scope.
	/// </summary>
	[TestMethod]
	[DataRow( false, false )]
	[DataRow( false, true )]
	[DataRow( true, false )]
	[DataRow( true, true )]
	public void QueuePreservesOriginatingLibrary( bool fromMenu, bool suppressFlow )
	{
		ThreadSafe.MarkMainThread();
		var game = new GlobalContext { TypeLibrary = new TypeLibrary() };
		var menu = new GlobalContext { TypeLibrary = new TypeLibrary() };
		using var gameCancellation = game.CancellationTokenSource;
		using var menuCancellation = menu.CancellationTokenSource;
		menu.TypeLibrary.AddAssembly( typeof( MainThreadContextTests ).Assembly, false );

		var origin = fromMenu ? menu : game;
		var drainer = fromMenu ? game : menu;
		GlobalContext observedContext = null;
		TypeLibrary observedLibrary = null;
		bool sawMenuType = false;

		OnWorker( origin, suppressFlow, () => MainThread.Queue( () =>
		{
			observedContext = GlobalContext.Current;
			observedLibrary = Game.TypeLibrary;
			sawMenuType = Game.TypeLibrary.GetType( typeof( MenuOnlyMarker ).FullName, exactFullName: true ) is not null;
		} ) );

		using var scope = new GlobalContext.GlobalContextScope( drainer );
		MainThread.RunMainThreadQueues();

		Assert.AreSame( origin, observedContext );
		Assert.AreSame( origin.TypeLibrary, observedLibrary );
		Assert.AreEqual( fromMenu, sawMenuType );
		Assert.AreSame( drainer, GlobalContext.Current );
	}

	/// <summary>
	/// A throwing callback cannot change the authority of later callbacks or their drainer.
	/// </summary>
	[TestMethod]
	public void RestoresContextAfterException()
	{
		ThreadSafe.MarkMainThread();
		var first = new GlobalContext();
		var second = new GlobalContext();
		using var firstCancellation = first.CancellationTokenSource;
		using var secondCancellation = second.CancellationTokenSource;
		var observed = new List<GlobalContext>();

		OnWorker( first, false, () => MainThread.Queue( () =>
		{
			observed.Add( GlobalContext.Current );
			throw new InvalidOperationException( "Expected queue regression exception" );
		} ) );

		OnWorker( second, false, () => MainThread.Queue( () => observed.Add( GlobalContext.Current ) ) );

		using var scope = new GlobalContext.GlobalContextScope( second );
		MainThread.RunMainThreadQueues();

		CollectionAssert.AreEqual( new[] { first, second }, observed );
		Assert.AreSame( second, GlobalContext.Current );
	}

	/// <summary>
	/// Calls made on the main thread still execute immediately in the caller's context.
	/// </summary>
	[TestMethod]
	public void MainThreadCallsRemainSynchronous()
	{
		ThreadSafe.MarkMainThread();
		var origin = GlobalContext.Current;
		GlobalContext observed = null;

		MainThread.Queue( () => observed = GlobalContext.Current );

		Assert.AreSame( origin, observed );
	}

	private static void OnWorker( GlobalContext context, bool suppressFlow, Action action )
	{
		Exception failure = null;
		var worker = new Thread( () =>
		{
			try
			{
				using var scope = new GlobalContext.GlobalContextScope( context );

				if ( suppressFlow )
				{
					using var flow = ExecutionContext.SuppressFlow();
					action();
				}
				else
				{
					action();
				}
			}
			catch ( Exception e )
			{
				failure = e;
			}
		} )
		{ IsBackground = true };

		worker.Start();
		Assert.IsTrue( worker.Join( TimeSpan.FromSeconds( 10 ) ), "The queue producer must complete." );
		Assert.IsNull( failure, failure?.ToString() );
	}

	/// <summary>
	/// Harmless stand-in for a privileged menu type that game reflection must not discover.
	/// </summary>
	[Expose]
	public sealed class MenuOnlyMarker
	{
	}
}
