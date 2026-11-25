using Sandbox.Internal;
using System;

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

		var thrown = Assert.ThrowsException<System.Reflection.TargetInvocationException>( () => system.GetGroup( "eventsystemtest.throws", false ).Run( "eventsystemtest.throws" ) );

		Assert.IsInstanceOfType<InvalidOperationException>( thrown.InnerException );
		Assert.AreEqual( "handler", thrown.InnerException.Message );
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
