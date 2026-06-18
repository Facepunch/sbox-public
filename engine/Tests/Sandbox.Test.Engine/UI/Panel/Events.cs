using Sandbox.UI;
namespace UITests.Panels;

[TestClass]
[DoNotParallelize] // Modifies UI System Global + the shared panel clock
public class PanelEventDispatchTest
{
	/// <summary>
	/// Builds a RootPanel with screen-like bounds, matching how the real UI system hosts panels.
	/// </summary>
	static RootPanel CreateRoot()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );
		return root;
	}

	/// <summary>
	/// CreateEvent( string ) queues an event that is dispatched on the next tick to both
	/// AddEventListener overloads - the bare Action and the Action that receives the PanelEvent.
	/// The PanelEvent carries the name, value and the panel that created it as Target.
	/// </summary>
	[TestMethod]
	public void ListenerOverloadsReceiveEvent()
	{
		var root = CreateRoot();
		var p = new Panel { Parent = root };

		int plainCalls = 0;
		PanelEvent received = null;

		p.AddEventListener( "custom", () => plainCalls++ );
		p.AddEventListener( "custom", e => received = e );

		p.CreateEvent( "custom", "payload" );

		// Nothing runs until the panel is ticked
		Assert.AreEqual( 0, plainCalls );
		Assert.IsNull( received );

		root.TickInternal();

		Assert.AreEqual( 1, plainCalls );
		Assert.IsNotNull( received );
		Assert.AreEqual( "custom", received.Name );
		Assert.AreEqual( "payload", received.Value );
		Assert.AreEqual( p, received.Target );
	}

	/// <summary>
	/// Calling CreateEvent( string ) twice with the same event name before the queue is processed
	/// updates the already-pending event instead of queueing a second one, so the listener fires
	/// once with the latest value.
	/// </summary>
	[TestMethod]
	public void PendingEventsCoalesceByName()
	{
		var root = CreateRoot();
		var p = new Panel { Parent = root };

		int calls = 0;
		object lastValue = null;

		p.AddEventListener( "custom", e => { calls++; lastValue = e.Value; } );

		p.CreateEvent( "custom", 1 );
		p.CreateEvent( "custom", 2 );

		root.TickInternal();

		Assert.AreEqual( 1, calls );
		Assert.AreEqual( 2, lastValue );

		// The queue is drained - ticking again doesn't re-fire
		root.TickInternal();
		Assert.AreEqual( 1, calls );
	}

	/// <summary>
	/// Events propagate from the panel that created them up through every ancestor. At each level
	/// the event's This is rewritten to the panel currently handling it, while Target stays the
	/// panel the event was created on.
	/// </summary>
	[TestMethod]
	public void EventPropagatesChildToParent()
	{
		var root = CreateRoot();
		var parent = new Panel { Parent = root };
		var child = new Panel { Parent = parent };

		Panel childThis = null, childTarget = null;
		Panel parentThis = null, parentTarget = null;
		Panel rootThis = null, rootTarget = null;

		child.AddEventListener( "custom", e => { childThis = e.This; childTarget = e.Target; } );
		parent.AddEventListener( "custom", e => { parentThis = e.This; parentTarget = e.Target; } );
		root.AddEventListener( "custom", e => { rootThis = e.This; rootTarget = e.Target; } );

		child.CreateEvent( "custom" );
		root.TickInternal();

		Assert.AreEqual( child, childThis );
		Assert.AreEqual( child, childTarget );

		Assert.AreEqual( parent, parentThis );
		Assert.AreEqual( child, parentTarget );

		Assert.AreEqual( root, rootThis );
		Assert.AreEqual( child, rootTarget );
	}

	/// <summary>
	/// StopPropagation prevents the event from reaching ancestor panels, but the remaining
	/// listeners on the panel that stopped it still run - the listener loop isn't aborted.
	/// </summary>
	[TestMethod]
	public void StopPropagationHaltsAtParent()
	{
		var root = CreateRoot();
		var parent = new Panel { Parent = root };
		var child = new Panel { Parent = parent };

		bool secondChildListenerRan = false;
		bool parentListenerRan = false;

		child.AddEventListener( "custom", e => e.StopPropagation() );
		child.AddEventListener( "custom", e => secondChildListenerRan = true );
		parent.AddEventListener( "custom", e => parentListenerRan = true );

		child.CreateEvent( "custom" );
		root.TickInternal();

		Assert.IsTrue( secondChildListenerRan );
		Assert.IsFalse( parentListenerRan );
	}

	/// <summary>
	/// CreateEvent( PanelEvent ) queues the exact event object given - listeners receive the same
	/// instance with its Name, Value and Target intact.
	/// </summary>
	[TestMethod]
	public void CreateEventObjectForm()
	{
		var root = CreateRoot();
		var p = new Panel { Parent = root };

		PanelEvent received = null;
		p.AddEventListener( "custom", e => received = e );

		var ev = new PanelEvent( "custom", p ) { Value = 42 };
		p.CreateEvent( ev );

		root.TickInternal();

		Assert.AreSame( ev, received );
		Assert.AreEqual( 42, received.Value );
		Assert.AreEqual( p, received.Target );
	}

	/// <summary>
	/// The OnClick virtual is invoked when an "onclick" MousePanelEvent is processed. A plain
	/// PanelEvent with the same name (the string CreateEvent form) does not reach OnClick, since
	/// the handler requires a MousePanelEvent.
	/// </summary>
	[TestMethod]
	public void OnClickVirtualFiresForMouseEvent()
	{
		var root = CreateRoot();
		var p = new ClickProbePanel { Parent = root };

		p.CreateEvent( new MousePanelEvent( "onclick", p, "mouseleft" ) );
		root.TickInternal();

		Assert.AreEqual( 1, p.Clicks );

		// String form creates a plain PanelEvent - OnClick is not invoked for it
		p.CreateEvent( "onclick" );
		root.TickInternal();

		Assert.AreEqual( 1, p.Clicks );
	}

	/// <summary>
	/// RemoveEventListener removes every listener registered under the given event name,
	/// while listeners for other events are unaffected.
	/// </summary>
	[TestMethod]
	public void ListenerRemovalByName()
	{
		var root = CreateRoot();
		var p = new Panel { Parent = root };

		int pingCalls = 0;
		int otherCalls = 0;

		p.AddEventListener( "ping", () => pingCalls++ );
		p.AddEventListener( "ping", e => pingCalls++ );
		p.AddEventListener( "other", () => otherCalls++ );

		p.RemoveEventListener( "ping" );

		p.CreateEvent( "ping" );
		p.CreateEvent( "other" );
		root.TickInternal();

		Assert.AreEqual( 0, pingCalls );
		Assert.AreEqual( 1, otherCalls );
	}

	/// <summary>
	/// The debounce parameter of CreateEvent delays dispatch until the panel clock passes the
	/// scheduled time - the event stays queued through earlier ticks and fires exactly once after.
	/// </summary>
	[TestMethod]
	public void DebouncedEventFiresAfterDelay()
	{
		var root = CreateRoot();
		var p = new Panel { Parent = root };

		int calls = 0;
		p.AddEventListener( "custom", () => calls++ );

		PanelRealTime.TimeNow = 0;
		p.CreateEvent( "custom", "v", 0.5f );

		root.TickInternal();
		Assert.AreEqual( 0, calls );

		PanelRealTime.TimeNow = 1.0;
		root.TickInternal();
		Assert.AreEqual( 1, calls );

		// Drained - doesn't fire again
		root.TickInternal();
		Assert.AreEqual( 1, calls );
	}

	/// <summary>
	/// A parameterless [PanelEvent] method is auto-subscribed using the lowercased method name
	/// with a trailing "event" stripped, so FlashEvent() listens for "flash".
	/// </summary>
	[TestMethod]
	public void PanelEventAttributeNameFromMethodSuffix()
	{
		var root = CreateRoot();
		var p = new AutoEventPanel { Parent = root };

		p.CreateEvent( "flash" );
		root.TickInternal();

		Assert.AreEqual( 1, p.FlashCount );
	}

	/// <summary>
	/// A [PanelEvent( name )] method with a PanelEvent parameter is auto-subscribed under the
	/// attribute's explicit name and receives the dispatched event with its value.
	/// </summary>
	[TestMethod]
	public void PanelEventAttributeExplicitName()
	{
		var root = CreateRoot();
		var p = new AutoEventPanel { Parent = root };

		p.CreateEvent( "custom.signal", "hello" );
		root.TickInternal();

		Assert.AreEqual( 1, p.CustomCount );
		Assert.AreEqual( "hello", p.CustomValue );
	}

	/// <summary>
	/// A [PanelEvent] method with a non-PanelEvent parameter receives the event's Value converted
	/// to the parameter type via Convert.ChangeType.
	/// </summary>
	[TestMethod]
	public void PanelEventAttributeTypedArgument()
	{
		var root = CreateRoot();
		var p = new AutoEventPanel { Parent = root };

		p.CreateEvent( "score.changed", 7 );
		root.TickInternal();

		Assert.AreEqual( 7, p.ScoreValue );
	}

	/// <summary>
	/// A [PanelEvent] method that returns false stops the event from propagating to ancestor
	/// panels, mirroring StopPropagation.
	/// </summary>
	[TestMethod]
	public void PanelEventBoolReturnFalseStopsPropagation()
	{
		var root = CreateRoot();
		var p = new AutoEventPanel { Parent = root };

		bool rootSawEvent = false;
		root.AddEventListener( "veto.signal", () => rootSawEvent = true );

		p.CreateEvent( "veto.signal" );
		root.TickInternal();

		Assert.AreEqual( 1, p.VetoCount );
		Assert.IsFalse( rootSawEvent );
	}
}

/// <summary>
/// Counts OnClick virtual invocations so tests can observe standard mouse event handling.
/// </summary>
public class ClickProbePanel : Panel
{
	public int Clicks { get; set; }

	/// <summary>
	/// Records that the standard "onclick" handler ran.
	/// </summary>
	protected override void OnClick( MousePanelEvent e )
	{
		Clicks++;
	}
}

/// <summary>
/// Exercises the [PanelEvent] auto-subscription paths: method-name suffix stripping, explicit
/// attribute names, typed value arguments and bool returns vetoing propagation.
/// </summary>
public class AutoEventPanel : Panel
{
	public int FlashCount { get; set; }
	public int CustomCount { get; set; }
	public string CustomValue { get; set; }
	public int ScoreValue { get; set; }
	public int VetoCount { get; set; }

	/// <summary>
	/// Subscribed automatically as "flash" - the lowercased method name with "event" stripped.
	/// </summary>
	[PanelEvent]
	public void FlashEvent()
	{
		FlashCount++;
	}

	/// <summary>
	/// Subscribed under the explicit attribute name, receiving the PanelEvent itself.
	/// </summary>
	[PanelEvent( "custom.signal" )]
	public void HandleCustomSignal( PanelEvent e )
	{
		CustomCount++;
		CustomValue = e.Value as string;
	}

	/// <summary>
	/// Subscribed under the explicit attribute name, receiving the event's Value converted to int.
	/// </summary>
	[PanelEvent( "score.changed" )]
	public void HandleScoreChanged( int value )
	{
		ScoreValue = value;
	}

	/// <summary>
	/// Returning false from a [PanelEvent] method stops the event propagating to ancestors.
	/// </summary>
	[PanelEvent( "veto.signal" )]
	public bool HandleVeto()
	{
		VetoCount++;
		return false;
	}
}
