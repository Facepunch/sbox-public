using Sandbox.Internal;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace EventTest;

/// <summary>
/// Tests for <see cref="EventSystem"/>.
/// </summary>
[TestClass]
public class EventSystemTest
{
	private static EventSystem CreateEventSystem() => new EventSystem();

	/// <summary>
	/// Events must be dispatched to handlers registered with <see cref="EventSystem.Register"/>.
	/// </summary>
	[TestMethod]
	public void Registered()
	{
		using var system = CreateEventSystem();

		var handler = new EventHandler();

		system.Register( handler );

		Assert.IsFalse( handler.Handled );

		system.RunInterface<IEventInterface>( x => x.EventMethod() );

		Assert.IsTrue( handler.Handled );
	}

	/// <summary>
	/// Events must <i>only</i> be dispatched to handlers registered with <see cref="EventSystem.Register"/>.
	/// </summary>
	[TestMethod]
	public void NotRegistered()
	{
		using var system = CreateEventSystem();

		var handler = new EventHandler();

		Assert.IsFalse( handler.Handled );

		system.RunInterface<IEventInterface>( x => x.EventMethod() );

		Assert.IsFalse( handler.Handled );
	}

	/// <summary>
	/// Events must <i>not</i> be dispatched to handlers unregistered with <see cref="EventSystem.Unregister"/>.
	/// </summary>
	[TestMethod]
	public void Unregistered()
	{
		using var system = CreateEventSystem();

		var handler = new EventHandler();

		system.Register( handler );
		system.Unregister( handler );

		Assert.IsFalse( handler.Handled );

		system.RunInterface<IEventInterface>( x => x.EventMethod() );

		Assert.IsFalse( handler.Handled );
	}

	private static WeakReference<EventHandler> RegisterHandlerAndReturnWeakReference( EventSystem system )
	{
		var handler = new EventHandler();

		system.Register( handler );

		return new WeakReference<EventHandler>( handler );
	}

	/// <summary>
	/// Registered handlers must be garbage collectable if not referenced elsewhere.
	/// </summary>
	[TestMethod]
	public async Task AllowCollection()
	{
		using var system = CreateEventSystem();

		var weakRef = RegisterHandlerAndReturnWeakReference( system );

		// Weak ref to something that definitely isn't referenced anywhere else,
		// if this doesn't get collected either then we know something else is wrong.

		var canary = new WeakReference<object>( new object() );

		const int maxAttempts = 100;

		var attempts = 0;

		while ( weakRef.TryGetTarget( out _ ) )
		{
			if ( attempts++ >= maxAttempts )
			{
				if ( canary.TryGetTarget( out _ ) )
				{
					Assert.Inconclusive( "No garbage collections were actually happening." );
				}
				else
				{
					Assert.Fail( "Handler wasn't garbage collected." );
				}
			}

			await Task.Delay( 1 );

			GC.Collect();
		}

		// James: Gets collected after the first attempt when I test locally in Debug

		Console.WriteLine( $"Collected after {attempts} attempt(s)" );
	}

	// [TestMethod]
	public void Benchmark()
	{
		var legacy = new LegacyWeakHashSet<object>();
		var rewrite = new WeakHashSet<object>();

		var handlers = Enumerable.Range( 0, 1_000_000 )
			.Select( x => new EventHandler() )
			.ToList();

		foreach ( var handler in handlers )
		{
			legacy.Add( handler );
			rewrite.Add( handler );
		}

		Stopwatch timer;
		int count;

		void RunBenchmarks()
		{
			for ( var i = 0; i < 5; ++i )
			{
				timer = Stopwatch.StartNew();
				count = 0;

				foreach ( var handler in legacy.OfType<IEventInterface>() )
				{
					handler.EventMethod();
					++count;
				}

				timer.Stop();

				Console.WriteLine( $"Old: {timer.Elapsed.TotalMilliseconds:F3}ms, Count: {count:N0}" );

				timer = Stopwatch.StartNew();
				count = 0;

				foreach ( var handler in rewrite.OfType<IEventInterface>() )
				{
					handler.EventMethod();
					++count;
				}

				timer.Stop();

				Console.WriteLine( $"New: {timer.Elapsed.TotalMilliseconds:F3}ms, Count: {count:N0}" );
			}

			Console.WriteLine();
		}

		RunBenchmarks();

		Console.WriteLine( "Garbage collecting..." );
		Console.WriteLine();

		GC.Collect();

		RunBenchmarks();

		Console.WriteLine( "Removing references to items and garbage collecting..." );
		Console.WriteLine();

		handlers.RemoveRange( handlers.Count / 2, handlers.Count / 2 );
		GC.Collect();

		RunBenchmarks();

		Console.WriteLine( "Garbage collecting..." );
		Console.WriteLine();

		GC.Collect();

		RunBenchmarks();

	}

	private interface IEventInterface
	{
		void EventMethod();
	}

	private sealed class EventHandler : IEventInterface
	{
		public bool Handled { get; private set; }

		public void EventMethod()
		{
			Handled = true;
		}
	}
}

file class LegacyWeakHashSet<T> where T : class
{
	private HashSet<WeakReference<T>> _set = new();

	public void Add( T item )
	{
		_set.Add( new WeakReference<T>( item ) );
		//Cleanup();
	}

	public bool Contains( T item )
	{
		return _set.Any( wr => wr.TryGetTarget( out var target ) && ReferenceEquals( target, item ) );
	}

	private void Cleanup()
	{
		_set.RemoveWhere( wr => !wr.TryGetTarget( out _ ) );
	}

	public bool Remove( T item )
	{
		bool removed = false;
		_set.RemoveWhere( wr =>
		{
			if ( !wr.TryGetTarget( out var target ) ) return true;
			if ( ReferenceEquals( target, item ) ) { removed = true; return true; }
			return false;
		} );
		Cleanup();
		return removed;
	}

	public IEnumerable<T2> OfType<T2>()
	{
		foreach ( var wr in _set )
		{
			if ( wr.TryGetTarget( out var target ) && target is T2 t2 )
				yield return t2;
		}
	}
}
