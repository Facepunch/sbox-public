using System;
using System.Collections.Generic;
using Sandbox.UI;

namespace UITests;

/// <summary>
/// Event callbacks may rebuild the UI and change the listeners being dispatched.
/// </summary>
[TestClass]
public class PanelEventTests
{
	/// <summary>
	/// Listeners added by a callback start receiving events on the next dispatch.
	/// </summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void AddedListenerWaitsForNextDispatch( bool automatic )
	{
		var panel = new Panel();
		var calls = new List<string>();
		var added = false;

		panel.AddEventListener( "onclick", () =>
		{
			calls.Add( "first" );

			if ( !added )
			{
				added = true;
				if ( automatic )
				{
					panel.AddAutomaticEventListener( "onclick", e => calls.Add( "added" ) );
				}
				else
				{
					panel.AddEventListener( "onclick", () => calls.Add( "added" ) );
				}
			}
		} );
		panel.AddEventListener( "onclick", e => calls.Add( "second" ) );
		panel.AddEventListener( "onother", () => Assert.Fail( "Wrong event dispatched." ) );

		panel.DispatchEventImmediate( new PanelEvent( "onclick", panel ) );
		CollectionAssert.AreEqual( new[] { "first", "second" }, calls );

		calls.Clear();
		panel.DispatchEventImmediate( new PanelEvent( "onclick", panel ) );
		CollectionAssert.AreEqual( new[] { "first", "second", "added" }, calls );
	}

	/// <summary>
	/// Replacing listeners during a callback preserves the current dispatch and parent bubbling.
	/// </summary>
	[TestMethod]
	public void ReplacedListenersTakeEffectOnNextDispatch()
	{
		var parent = new Panel();
		var panel = new Panel( parent );
		var calls = new List<string>();

		panel.AddEventListener( "onclick", () =>
		{
			calls.Add( "first" );
			panel.RemoveEventListener( "onclick" );
			panel.AddEventListener( "onclick", () => calls.Add( "replacement" ) );
		} );
		panel.AddEventListener( "onclick", () => calls.Add( "second" ) );
		parent.AddEventListener( "onclick", () => calls.Add( "parent" ) );

		panel.DispatchEventImmediate( new PanelEvent( "onclick", panel ) );
		CollectionAssert.AreEqual( new[] { "first", "second", "parent" }, calls );

		calls.Clear();
		panel.DispatchEventImmediate( new PanelEvent( "onclick", panel ) );
		CollectionAssert.AreEqual( new[] { "replacement", "parent" }, calls );
	}

	/// <summary>
	/// Nested dispatch keeps the locked listeners until the outermost dispatch finishes.
	/// </summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void NestedDispatchDefersListenerChanges( bool replace )
	{
		var panel = new Panel();
		var calls = new List<string>();
		var nested = false;

		panel.AddEventListener( "onclick", () =>
		{
			calls.Add( "first" );
			if ( nested ) return;

			nested = true;
			panel.RemoveEventListener( "onclick" );
			if ( replace )
			{
				panel.AddEventListener( "onclick", () => calls.Add( "replacement" ) );
			}
			panel.DispatchEventImmediate( new PanelEvent( "onclick", panel ) );
		} );
		panel.AddEventListener( "onclick", () => calls.Add( "second" ) );

		panel.DispatchEventImmediate( new PanelEvent( "onclick", panel ) );
		CollectionAssert.AreEqual( new[] { "first", "first", "second", "second" }, calls );

		calls.Clear();
		panel.DispatchEventImmediate( new PanelEvent( "onclick", panel ) );
		CollectionAssert.AreEqual( replace ? new[] { "replacement" } : Array.Empty<string>(), calls );
	}

	/// <summary>
	/// Removal includes listeners added during the current dispatch but not yet in its cached list.
	/// </summary>
	[TestMethod]
	public void RemovingNewListenerDuringDispatchDoesNotLeaveItRegistered()
	{
		var panel = new Panel();
		var calls = 0;
		panel.AddEventListener( "custom", () =>
		{
			calls++;
			panel.AddEventListener( "custom", () => Assert.Fail( "Removed listener ran." ) );
			panel.RemoveEventListener( "custom" );
		} );

		panel.DispatchEventImmediate( new PanelEvent( "custom", panel ) );
		panel.DispatchEventImmediate( new PanelEvent( "custom", panel ) );
		Assert.AreEqual( 1, calls );
	}

	/// <summary>
	/// Repeated dispatch reuses the listener cache without creating enumeration garbage.
	/// </summary>
	[TestMethod]
	[TestCategory( Allocations.Category )]
	public void ListenerDispatchDoesNotAllocate()
	{
		var panel = new Panel();
		var calls = 0;
		panel.AddEventListener( "custom", () => calls++ );
		var ev = new PanelEvent( "custom", panel );

		Allocations.AssertSettles( "panel listener dispatch", () => panel.DispatchEventImmediate( ev ) );
		Assert.IsTrue( calls > 0 );
	}

	/// <summary>
	/// Hashing a new registration and comparing a duplicate must not box the callback value.
	/// Removal's existing capturing predicate is outside the registration measurement.
	/// </summary>
	[TestMethod]
	[TestCategory( Allocations.Category )]
	public void ListenerRegistrationDoesNotAllocate()
	{
		var panel = new Panel();
		var calls = 0;
		Action listener = () => calls++;

		Allocations.AssertSettles( "panel listener registration", () =>
		{
			panel.AddEventListener( "custom", listener );
			panel.AddEventListener( "custom", listener );
		}, beforePass: () => panel.RemoveEventListener( "custom" ) );

		panel.DispatchEventImmediate( new PanelEvent( "custom", panel ) );
		Assert.AreEqual( 1, calls );
	}

	/// <summary>
	/// An inner dispatch can replace listeners shared with the outer dispatch.
	/// </summary>
	[TestMethod]
	public void NestedDispatchCanChangeSharedListeners()
	{
		var panel = new Panel();
		var calls = new List<string>();

		panel.AddEventListener( "onclick", () =>
		{
			calls.Add( "first" );
			panel.DispatchEventImmediate( new PanelEvent( "onnested", panel ) );
		} );
		panel.AddEventListener( "onclick", () => calls.Add( "second" ) );
		panel.AddEventListener( "onnested", () =>
		{
			calls.Add( "nested" );
			panel.RemoveEventListener( "onclick" );
			panel.AddEventListener( "onclick", () => calls.Add( "replacement" ) );
		} );

		panel.DispatchEventImmediate( new PanelEvent( "onclick", panel ) );
		CollectionAssert.AreEqual( new[] { "first", "nested", "second" }, calls );

		calls.Clear();
		panel.DispatchEventImmediate( new PanelEvent( "onclick", panel ) );
		CollectionAssert.AreEqual( new[] { "replacement" }, calls );
	}

	/// <summary>
	/// A failed nested callback must leave the outer dispatch protected from mutations.
	/// </summary>
	[TestMethod]
	public void NestedExceptionRestoresOuterDispatch()
	{
		var panel = new Panel();
		var calls = new List<string>();
		var failure = new InvalidOperationException( "Callback failure" );

		panel.AddEventListener( "onclick", () =>
		{
			calls.Add( "first" );
			var thrown = Assert.ThrowsException<InvalidOperationException>( () =>
				panel.DispatchEventImmediate( new PanelEvent( "onnested", panel ) ) );
			Assert.AreSame( failure, thrown );

			panel.RemoveEventListener( "onclick" );
			panel.AddEventListener( "onclick", () => calls.Add( "replacement" ) );
		} );
		panel.AddEventListener( "onclick", () => calls.Add( "second" ) );
		panel.AddEventListener( "onnested", () => { throw failure; } );

		panel.DispatchEventImmediate( new PanelEvent( "onclick", panel ) );
		CollectionAssert.AreEqual( new[] { "first", "second" }, calls );

		calls.Clear();
		panel.DispatchEventImmediate( new PanelEvent( "onclick", panel ) );
		CollectionAssert.AreEqual( new[] { "replacement" }, calls );
	}

	private sealed class ReinitializablePanel : Panel
	{
		internal void ResetEvents() => InitializeEvents();
	}

	/// <summary>
	/// Reinitializing automatic listeners during dispatch preserves the active callbacks.
	/// </summary>
	[TestMethod]
	public void ReinitializingEventsPreservesActiveDispatch()
	{
		var panel = new ReinitializablePanel();
		var calls = new List<string>();

		panel.AddEventListener( "onclick", () =>
		{
			calls.Add( "first" );
			panel.ResetEvents();
		} );
		panel.AddAutomaticEventListener( "onclick", e => calls.Add( "automatic" ) );

		panel.DispatchEventImmediate( new PanelEvent( "onclick", panel ) );
		CollectionAssert.AreEqual( new[] { "first", "automatic" }, calls );

		calls.Clear();
		panel.DispatchEventImmediate( new PanelEvent( "onclick", panel ) );
		CollectionAssert.AreEqual( new[] { "first" }, calls );
	}
}
