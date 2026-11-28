using Sandbox.Internal;
using System;
using System.Runtime.CompilerServices;

namespace EventTests;

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

	/// <summary>
	/// Attributed handlers run whether static, on an instance, private on a base class, or taking arguments.
	/// </summary>
	[TestMethod]
	public void AttributedHandlersRun()
	{
		using var system = CreateEventSystem();
		system.RegisterAssembly( typeof( AttributedHandler ).Assembly );

		var handler = new AttributedHandler();
		system.Register( handler );
		AttributedHandler.StaticCalls = 0;

		system.Run( "eventsystemtest.static" );
		system.Run( "eventsystemtest.instance" );
		system.Run( "eventsystemtest.base" );
		system.Run( "eventsystemtest.argument", 5 );

		Assert.AreEqual( 1, AttributedHandler.StaticCalls );
		Assert.AreEqual( 1, handler.InstanceCalls );
		Assert.AreEqual( 1, handler.BaseCalls );
		Assert.AreEqual( 5, handler.Argument );
	}

	/// <summary>
	/// A handler's exception is reported as itself, not as the exception inside it.
	/// </summary>
	[TestMethod]
	public void HandlerExceptionIsReportedAsItself()
	{
		using var system = CreateEventSystem();
		system.RegisterAssembly( typeof( AttributedHandler ).Assembly );
		system.Register( new AttributedHandler() );

		var thrown = Assert.ThrowsException<System.Reflection.TargetInvocationException>( () => system.RunHandlers( "eventsystemtest.throws" ) );

		Assert.IsInstanceOfType<InvalidOperationException>( thrown.InnerException );
		Assert.AreEqual( "handler", thrown.InnerException.Message );
	}

	/// <summary>
	/// An instance handler runs for its type's targets only: none before one registers, again once it does, not after the last
	/// unregisters, and again when one registers anew (the event list keeps which of its actions have targets).
	/// </summary>
	[TestMethod]
	public void InstanceHandlersRunWhileTheirTypeHasTargets()
	{
		using var system = CreateEventSystem();
		system.RegisterAssembly( typeof( AttributedHandler ).Assembly );
		var first = new AttributedHandler();
		var second = new AttributedHandler();

		system.Run( "eventsystemtest.instance" );

		system.Register( first );
		system.Run( "eventsystemtest.instance" );
		Assert.AreEqual( 1, first.InstanceCalls );

		system.Register( second );
		system.Unregister( first );
		system.Run( "eventsystemtest.instance" );
		Assert.AreEqual( 1, first.InstanceCalls );
		Assert.AreEqual( 1, second.InstanceCalls );

		system.Unregister( second );
		system.Run( "eventsystemtest.instance" );
		Assert.AreEqual( 1, second.InstanceCalls );

		system.Register( first );
		system.Run( "eventsystemtest.instance" );
		Assert.AreEqual( 2, first.InstanceCalls );
		Assert.AreEqual( 1, second.InstanceCalls );
	}

	/// <summary>
	/// Handlers run in the event list's priority order (the lower priority first), whichever of their types got a target first.
	/// </summary>
	[TestMethod]
	public void HandlersRunInPriorityOrderWhicheverGotTargetsFirst()
	{
		using var system = CreateEventSystem();
		system.RegisterAssembly( typeof( AttributedHandler ).Assembly );
		PriorityHandler.Order.Clear();

		var low = new LowPriorityHandler();
		var high = new HighPriorityHandler();

		system.Register( low );
		system.Run( "eventsystemtest.priority" );
		system.Register( high );
		system.Run( "eventsystemtest.priority" );

		CollectionAssert.AreEqual( new[] { "low", "low", "high" }, PriorityHandler.Order );
	}

	private class PriorityHandler
	{
		public static readonly System.Collections.Generic.List<string> Order = new();
	}

	private sealed class LowPriorityHandler : PriorityHandler
	{
		[Event( "eventsystemtest.priority", Priority = 1 )]
		public void OnPriority() => Order.Add( "low" );
	}

	private sealed class HighPriorityHandler : PriorityHandler
	{
		[Event( "eventsystemtest.priority", Priority = 10 )]
		public void OnPriority() => Order.Add( "high" );
	}

	private class AttributedHandlerBase
	{
		public int BaseCalls;

		[Event( "eventsystemtest.base" )]
		void OnBase() => BaseCalls++;
	}

	private sealed class AttributedHandler : AttributedHandlerBase
	{
		public static int StaticCalls;
		public int InstanceCalls;
		public int Argument;

		[Event( "eventsystemtest.static" )]
		static void OnStatic() => StaticCalls++;

		[Event( "eventsystemtest.instance" )]
		public void OnInstance() => InstanceCalls++;

		[Event( "eventsystemtest.argument" )]
		public void OnArgument( int value ) => Argument = value;

		[Event( "eventsystemtest.throws" )]
		public void OnThrows() => throw new InvalidOperationException( "handler", new FormatException( "inner" ) );
	}

	/// <summary>
	/// Registration changes during a callback take effect on the next dispatch.
	/// </summary>
	[TestMethod]
	public void InterfaceDispatchUsesSnapshot()
	{
		using var system = new EventSystem();
		var first = new EventHandler();
		var second = new EventHandler();
		var added = new EventHandler();
		system.Register( first );
		system.Register( second );

		system.RunInterface<IEventInterface>( listener =>
		{
			system.Unregister( first );
			system.Unregister( second );
			system.Register( added );
			listener.EventMethod();
		} );

		Assert.IsTrue( first.Handled && second.Handled );
		Assert.IsFalse( added.Handled );
		system.RunInterface<IEventInterface>( listener => listener.EventMethod() );
		Assert.IsTrue( added.Handled );
	}

	/// <summary>
	/// Each handler receives the original arguments in priority order, including after its assembly is restored.
	/// </summary>
	[TestMethod]
	public void NamedHandlersSurviveReregistration()
	{
		using var system = new EventSystem();
		var handler = new NamedHandler();
		var assembly = typeof( NamedHandler ).Assembly;
		object[] args = [7];

		system.RegisterAssembly( assembly );
		system.Register( handler );
		system.Run( "event-test.ref", args );
		Assert.AreEqual( "first:7 second:7 ", handler.Calls );
		Assert.AreEqual( 7, args[0] );

		handler.Calls = "";
		system.UnregisterAssembly( assembly );
		system.Run( "event-test.ref", args );
		Assert.AreEqual( "", handler.Calls );

		system.RegisterAssembly( assembly );
		system.Run( "event-test.ref", args );
		Assert.AreEqual( "first:7 second:7 ", handler.Calls );
	}

	/// <summary>
	/// A registered handler must release its argument once dispatch finishes.
	/// </summary>
	[TestMethod]
	public void NamedHandlerReleasesArgument()
	{
		using var system = new EventSystem();
		system.RegisterAssembly( typeof( NamedHandler ).Assembly );
		var argument = DispatchTemporaryArgument( system );

		GC.Collect();
		Assert.IsFalse( argument.IsAlive );
		GC.KeepAlive( system );
	}

	[MethodImpl( MethodImplOptions.NoInlining )]
	private static WeakReference DispatchTemporaryArgument( EventSystem system )
	{
		bool invoked = false;
		Action argument = () => invoked = true;

		system.Run( "event-test.consume", argument );
		Assert.IsTrue( invoked );

		return new WeakReference( argument );
	}

	private class NamedHandlerBase
	{
		internal string Calls = "";

		[EventAttribute( "event-test.ref", Priority = -1 )]
		private void First( ref int value )
		{
			Calls += $"first:{value} ";
			value++;
		}
	}

	private sealed class NamedHandler : NamedHandlerBase
	{
		[EventAttribute( "event-test.ref" )]
		private void Second( int value ) => Calls += $"second:{value} ";

		[EventAttribute( "event-test.consume" )]
		private static void Consume( Action action ) => action();
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
