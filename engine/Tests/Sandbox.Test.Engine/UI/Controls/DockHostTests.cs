using Sandbox.UI;
using System;
using static UITests.UiTesting;

namespace UITests.Controls;

/// <summary>
/// Docked panel lifetimes, ownership and input on a native-backed UI surface.
/// </summary>
[TestClass]
[DoNotParallelize] // Labels need the shared text renderer disabled
public partial class DockHostTests
{
	sealed class RecordingPanel : Panel
	{
		/// <summary>Number of deletion callbacks.</summary>
		public int DeletedCount { get; private set; }

		/// <inheritdoc/>
		public override void OnDeleted()
		{
			DeletedCount++;
			base.OnDeleted();
		}
	}

	bool previousRenderText;
	UISurface surface;
	DockHost host;

	/// <summary>Creates an isolated surface without GPU text textures.</summary>
	[TestInitialize]
	public void Setup()
	{
		ThreadSafe.MarkMainThread();
		previousRenderText = DisableTextRendering();
		surface = new UISurface { Size = new Vector2( 800, 600 ), DpiScale = 1, MouseInside = true };
		host = new DockHost { Parent = surface.Root };
	}

	/// <summary>Deletes the surface and restores text rendering.</summary>
	[TestCleanup]
	public void Cleanup()
	{
		try
		{
			surface?.Dispose();
		}
		finally
		{
			TextBlock.ui_rendertext = previousRenderText;
		}
	}

	/// <summary>Closed registrations stay lazy through saving and ticking, and reuse content on reopen.</summary>
	[TestMethod]
	public void FactoryCreatesContentOnlyWhenOpened()
	{
		int calls = 0;
		var item = host.Register( "lazy", "Lazy", () => { calls++; return new Panel(); } );
		var closed = host.State;
		Frame();
		Assert.AreEqual( 0, calls );
		Assert.IsNull( item.Content );
		Assert.IsTrue( host.RestoreState( closed ) );
		Assert.AreEqual( 0, calls );
		host.Dock( "lazy" );
		var content = item.Content;
		var opened = host.State;
		host.Close( "lazy" );
		Assert.IsTrue( host.RestoreState( opened ) );
		Assert.AreEqual( 1, calls );
		Assert.AreSame( content, item.Content );
	}

	/// <summary>A failing factory must not edit the visible layout.</summary>
	[TestMethod]
	public void FactoryFailureLeavesLayoutUnchanged()
	{
		host.Register( "bad", "Bad", () => throw new InvalidOperationException( "Factory failure" ) );
		var before = host.State;
		Assert.ThrowsException<InvalidOperationException>( () => host.Dock( "bad" ) );
		Assert.AreEqual( before, host.State );
	}

	void Frame()
	{
		// Input events and split sizes settle over successive ticks and layouts.
		for ( int i = 0; i < 3; i++ ) UiTesting.Frame( surface );
	}

	void MoveTo( Vector2 position )
	{
		surface.MouseMoved( position );
		Frame();
		surface.MouseMoved( position );
	}

	void MouseButton( bool down, MouseButtons button = MouseButtons.Left )
	{
		// The engine tier has no native virtual-key mapper. Drive the same panel mouse state directly.
		var code = button == MouseButtons.Middle ? NativeEngine.ButtonCode.MouseMiddle : NativeEngine.ButtonCode.MouseLeft;
		surface.Input.AddMouseButton( code, down, default );
		Frame();
	}

	static Panel Tab( DockHost owner, string title ) => owner.Descendants.OfType<Label>()
		.Single( x => x.HasClass( "dock-tab-title" ) && x.Text == title ).Parent;

	DockItem[] OpenTabs()
	{
		var items = new[] { "a", "b", "c" }.Select( id => host.Register( id, id, new RecordingPanel() ) ).ToArray();
		foreach ( var item in items ) host.Dock( item.Id );
		Frame();
		return items;
	}

	static void AssertRetained( DockHost owner, DockItem[] items, Panel[] tabs )
	{
		Assert.AreEqual( items.Length, owner.Items.Count );
		for ( int i = 0; i < items.Length; i++ )
		{
			Assert.AreSame( items[i], owner.Find( items[i].Id ) );
			Assert.IsTrue( items[i].Content.IsValid );
			Assert.IsTrue( items[i].Content.Ancestors.Contains( owner ) );
			Assert.AreEqual( 0, ((RecordingPanel)items[i].Content).DeletedCount );
			Assert.AreSame( tabs[i], Tab( owner, items[i].Title ) );
			Assert.IsTrue( tabs[i].IsValid );
		}
	}

	void BeginDrag( Panel tab, Vector2 destination )
	{
		var title = tab.Children.OfType<Label>().Single( x => x.HasClass( "dock-tab-title" ) );
		Assert.IsTrue( title.Box.Rect.Width > 0 );
		MoveTo( title.Box.Rect.Center );
		Assert.AreSame( tab, surface.Hovered, "the title is hit through, not the close button" );
		MouseButton( true );
		Assert.AreSame( tab, surface.Focus );
		MoveTo( destination );
		Assert.IsTrue( tab.HasClass( "dragging" ), "real mouse input crossed the drag threshold" );
		Frame();
		Assert.IsTrue( host.Descendants.Single( x => x.HasClass( "dock-targets" ) ).IsVisible );
	}

	/// <summary>Switching, splitting, closing and restoring keep content and tab instances alive.</summary>
	[TestMethod]
	public void LayoutEditsRetainContentAndTabs()
	{
		var items = OpenTabs();
		var tabs = items.Select( x => Tab( host, x.Id ) ).ToArray();
		var containers = items.Select( x => x.Content.Parent ).ToArray();
		var entry = new TextEntry { Parent = items[0].Content, Text = "unsaved edit" };

		Assert.IsFalse( items[0].Content.IsVisible );
		Assert.IsTrue( items[2].Content.IsVisible );
		Assert.IsTrue( host.Activate( "a" ) );
		Frame();
		Assert.IsTrue( items[0].Content.IsVisible );
		Assert.IsFalse( items[2].Content.IsVisible );
		Assert.IsTrue( tabs[0].HasClass( "selected" ) );
		Assert.IsFalse( tabs[2].HasClass( "selected" ) );
		AssertRetained( host, items, tabs );

		host.Dock( "b", "a", DockPosition.Right, 0.3f );
		host.Dock( "c", "b", DockPosition.Bottom, 0.4f );
		Frame();
		var saved = host.State;
		Assert.AreEqual( 3, host.Descendants.Count( x => x.HasClass( "dock-group" ) ) );
		Assert.IsTrue( items.All( x => x.Content.IsVisible ) );
		AssertRetained( host, items, tabs );

		foreach ( var item in items ) Assert.IsTrue( host.Close( item.Id ) );
		Frame();
		Assert.AreEqual( 0, host.Descendants.Count( x => x.HasClass( "dock-group" ) ) );
		Assert.IsTrue( items.All( x => !x.Content.IsVisible && !host.IsOpen( x.Id ) ) );
		Assert.IsTrue( tabs.All( x => !x.IsVisible ) );
		AssertRetained( host, items, tabs );

		host.Dock( "a" );
		host.Dock( "b", "a" );
		var twoTabs = host.State;
		Frame();
		AssertRetained( host, items, tabs );
		Assert.IsTrue( host.RestoreState( saved ) );
		Frame();
		Assert.AreEqual( saved, host.State );
		AssertRetained( host, items, tabs );
		Assert.IsTrue( items.All( x => x.Content.IsVisible ) );

		Assert.IsTrue( host.RestoreState( twoTabs ) );
		Frame();
		Assert.IsFalse( host.IsOpen( "c" ) );
		Assert.IsFalse( items[2].Content.IsVisible );
		Assert.IsFalse( tabs[2].IsVisible );
		AssertRetained( host, items, tabs );
		CollectionAssert.AreEqual( containers, items.Select( x => x.Content.Parent ).ToArray() );
		Assert.IsTrue( entry.IsValid );
		Assert.AreSame( items[0].Content, entry.Parent );
		Assert.AreEqual( "unsaved edit", entry.Text );
	}

	/// <summary>Ancestor teardown owns open, closed and never-opened content exactly once.</summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void AncestorDeletionDeletesAllContentOnce( bool immediate )
	{
		var ancestor = surface.Root.Add.Panel();
		ancestor.Style.FlexGrow = 1;
		host.Parent = ancestor;
		var items = OpenTabs();
		var neverOpened = new RecordingPanel();
		host.Register( "never", "never", neverOpened );
		var child = new RecordingPanel { Parent = items[0].Content };
		var tabs = items.Select( x => Tab( host, x.Id ) ).ToArray();
		host.Close( "a" );
		host.Close( "b" );
		Frame();
		Assert.IsFalse( items[0].Content.IsVisible );
		Assert.IsTrue( items[2].Content.IsVisible );

		ancestor.Delete( immediate );
		if ( !immediate )
		{
			Assert.AreEqual( 0, ((RecordingPanel)items[0].Content).DeletedCount );
			Assert.ThrowsException<InvalidOperationException>( () => host.Dock( "a" ) );
		}
		surface.System.RunDeferredDeletion();
		ancestor.Delete( true );
		surface.System.RunDeferredDeletion();

		Assert.IsFalse( host.IsValid );
		Assert.IsTrue( tabs.All( x => !x.IsValid ) );
		foreach ( var content in items.Select( x => (RecordingPanel)x.Content ).Append( neverOpened ).Append( child ) )
		{
			Assert.IsFalse( content.IsValid );
			Assert.AreEqual( 1, content.DeletedCount );
		}
	}

	/// <summary>Transfers preserve the content, container and registration across independent surfaces.</summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void TransferMovesOwnershipWithoutRecreatingContent( bool closed )
	{
		var content = new RecordingPanel();
		var item = host.Register( "a", "a", content, icon: "folder" );
		host.Register( "b", "b", new RecordingPanel() );
		host.Dock( "a" );
		host.Dock( "b", "a", DockPosition.Right );
		var container = content.Parent;
		if ( closed ) host.Close( "a" );
		Frame();

		using var otherSurface = new UISurface { Size = new Vector2( 500, 400 ), DpiScale = 1 };
		var target = new DockHost { Parent = otherSurface.Root };
		target.Register( "target", "target", new RecordingPanel() );
		target.Dock( "target" );
		host.TransferTo( target, "a", "target", DockPosition.Bottom, 0.3f );
		Frame();
		for ( int i = 0; i < 3; i++ ) UiTesting.Frame( otherSurface );

		Assert.IsNull( host.Find( "a" ) );
		Assert.IsFalse( host.IsOpen( "a" ) );
		Assert.IsTrue( host.IsOpen( "b" ) );
		Assert.AreEqual( 1, host.Items.Count );
		Assert.AreSame( item, target.Find( "a" ) );
		Assert.AreSame( content, target.Find( "a" ).Content );
		Assert.AreSame( container, content.Parent );
		Assert.AreSame( target, content.Ancestors.OfType<DockHost>().First() );
		Assert.AreSame( otherSurface.System, content.UISystem );
		Assert.IsTrue( target.IsOpen( "a" ) );
		Assert.IsTrue( content.IsVisible );
		Assert.AreEqual( 2, target.Items.Count );
		Assert.AreEqual( "folder", target.Find( "a" ).Icon );
		var targetTab = Tab( target, "a" );
		Assert.AreEqual( "folder", targetTab.Children.OfType<IconPanel>().Single( x => x.HasClass( "dock-tab-icon" ) ).Text );
		Assert.AreEqual( "Close panel", targetTab.Children.Single( x => x.HasClass( "dock-tab-action" ) ).Tooltip );

		host.Delete( true );
		Assert.IsTrue( content.IsValid );
		Assert.AreEqual( 0, content.DeletedCount );
		otherSurface.Root.Delete( true );
		Assert.IsFalse( content.IsValid );
		Assert.AreEqual( 1, content.DeletedCount );
	}

	/// <summary>Current focus leaves with the subtree, and its queued blur survives source disposal.</summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void TransferReleasesCurrentFocusAndDeliversBlurOnDestination( bool pendingFocus )
	{
		var content = new RecordingPanel { AcceptsFocus = true };
		var focused = new Panel { Parent = content, AcceptsFocus = true };
		var pending = new Panel { Parent = content, AcceptsFocus = true };
		host.Register( "a", "a", content );
		host.Dock( "a" );
		Frame();
		Assert.IsTrue( focused.Focus() );
		Frame();
		Assert.AreSame( focused, surface.Focus );
		Assert.IsTrue( focused.HasFocus );
		Assert.IsTrue( content.HasFocus );

		int blurs = 0;
		int pendingFocusEvents = 0;
		int ancestorFocusEvents = 0;
		UISystem blurSystem = null;
		focused.AddEventListener( "onblur", () =>
		{
			blurs++;
			blurSystem = focused.UISystem;
		} );
		pending.AddEventListener( "onfocus", () => pendingFocusEvents++ );
		content.AddEventListener( "onfocus", e =>
		{
			if ( e.Target == content ) ancestorFocusEvents++;
		} );

		using var otherSurface = new UISurface { Size = new Vector2( 500, 400 ), DpiScale = 1 };
		var target = new DockHost { Parent = otherSurface.Root };
		if ( pendingFocus )
		{
			Assert.IsTrue( pending.Focus() );
			Assert.AreSame( pending, surface.System.NextFocus );
			Assert.IsTrue( surface.System.FocusPendingChange );
		}

		host.TransferTo( target, "a" );
		Assert.IsNull( surface.Focus );
		Assert.IsNull( surface.System.NextFocus );
		Assert.IsFalse( surface.System.FocusPendingChange );
		Assert.IsFalse( focused.HasFocus );
		Assert.IsFalse( content.HasFocus );
		Assert.IsFalse( surface.Root.HasFocus );
		Assert.AreEqual( 0, blurs, "blur is queued, not dispatched inside transfer" );

		// Settle source focus without ticking panels or delivering the queued blur.
		surface.System.TickFocus();
		Assert.IsNull( surface.Focus, "the focusable content ancestor must not inherit stale source focus" );
		surface.Dispose();
		Assert.IsTrue( focused.IsValid );
		Assert.AreEqual( 0, blurs );
		for ( int i = 0; i < 3; i++ ) UiTesting.Frame( otherSurface );

		Assert.AreEqual( 1, blurs );
		Assert.AreSame( otherSurface.System, blurSystem );
		Assert.AreEqual( 0, pendingFocusEvents );
		Assert.AreEqual( 0, ancestorFocusEvents );
		Assert.IsNull( otherSurface.Focus );
		Assert.IsFalse( focused.HasFocus );
		Assert.IsFalse( pending.HasFocus );
		Assert.IsFalse( content.HasFocus );
		Assert.AreEqual( 0, content.DeletedCount );
	}

	/// <summary>Pending focus in transferred content is discarded without clearing unrelated current focus.</summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void TransferClearsPendingFocusWithoutFocusingMovedContent( bool unrelatedFocus )
	{
		var content = new RecordingPanel { AcceptsFocus = true };
		var pending = new Panel { Parent = content, AcceptsFocus = true };
		var remaining = new RecordingPanel { AcceptsFocus = true };
		host.Register( "a", "a", content );
		host.Register( "b", "b", remaining );
		host.Dock( "a" );
		host.Dock( "b", "a", DockPosition.Right );
		Frame();
		if ( unrelatedFocus )
		{
			Assert.IsTrue( remaining.Focus() );
			Frame();
		}
		var previousFocus = surface.Focus;
		Assert.AreSame( unrelatedFocus ? remaining : null, previousFocus );
		int focusEvents = 0;
		pending.AddEventListener( "onfocus", () => focusEvents++ );
		Assert.IsTrue( pending.Focus() );
		Assert.AreSame( pending, surface.System.NextFocus );
		Assert.IsTrue( surface.System.FocusPendingChange );

		using var otherSurface = new UISurface { Size = new Vector2( 500, 400 ), DpiScale = 1 };
		var target = new DockHost { Parent = otherSurface.Root };
		host.TransferTo( target, "a" );
		Assert.AreSame( previousFocus, surface.Focus );
		Assert.IsNull( surface.System.NextFocus );
		Assert.IsFalse( surface.System.FocusPendingChange );
		Frame();
		Assert.AreSame( previousFocus, surface.Focus );
		surface.Dispose();
		for ( int i = 0; i < 3; i++ ) UiTesting.Frame( otherSurface );

		Assert.AreEqual( 0, focusEvents );
		Assert.IsNull( otherSurface.Focus );
		Assert.IsFalse( pending.HasFocus );
		Assert.IsFalse( content.HasFocus );
		Assert.AreSame( otherSurface.System, pending.UISystem );
		Assert.IsTrue( target.IsOpen( "a" ) );
		Assert.AreEqual( 0, content.DeletedCount );
	}

	/// <summary>Source notifications see both committed views and can close the destination placement target.</summary>
	[TestMethod]
	public void TransferNotificationCanCloseRelativeTarget()
	{
		var content = new RecordingPanel();
		var item = host.Register( "a", "a", content );
		host.Register( "b", "b", new RecordingPanel() );
		host.Dock( "a" );
		host.Dock( "b", "a", DockPosition.Right );
		var sourceTab = Tab( host, "a" );
		var target = new DockHost { Parent = surface.Root };
		var relative = target.Register( "relative", "relative", new RecordingPanel() );
		target.Dock( "relative" );
		Frame();
		var container = content.Parent;
		var expectedSource = new DockLayout();
		expectedSource.Dock( "b" );
		var expectedTarget = new DockLayout();
		expectedTarget.Dock( "relative" );
		expectedTarget.Dock( "a", "relative", DockPosition.Bottom, 0.3f );
		int sourceChanges = 0;
		int targetChanges = 0;
		Panel destinationTab = null;
		host.LayoutChanged += () =>
		{
			sourceChanges++;
			Assert.IsNull( host.Find( "a" ) );
			Assert.AreEqual( expectedSource.Save(), host.State );
			Assert.IsFalse( sourceTab.IsValid );
			Assert.AreEqual( 1, host.Descendants.Count( x => x.HasClass( "dock-group" ) ) );
			Assert.AreSame( item, target.Find( "a" ) );
			Assert.AreEqual( expectedTarget.Save(), target.State );
			Assert.AreEqual( 2, target.Descendants.Count( x => x.HasClass( "dock-group" ) ) );
			destinationTab = Tab( target, "a" );
			Assert.IsTrue( destinationTab.HasClass( "selected" ) );
			Assert.AreSame( container, content.Parent );
			Assert.IsTrue( container.Parent.HasClass( "dock-body" ) );
			Assert.AreSame( destinationTab.Parent.Parent, container.Parent.Parent );
			Assert.IsTrue( target.Close( "relative" ) );
		};
		target.LayoutChanged += () =>
		{
			targetChanges++;
			Assert.IsNull( host.Find( "a" ) );
			Assert.IsTrue( target.IsOpen( "a" ) );
			Assert.AreSame( item, target.Find( "a" ) );
		};

		host.TransferTo( target, "a", "relative", DockPosition.Bottom, 0.3f );
		Frame();
		Assert.AreEqual( 1, sourceChanges );
		Assert.IsTrue( targetChanges > 0 );
		Assert.IsFalse( target.IsOpen( "relative" ) );
		Assert.IsTrue( target.IsOpen( "a" ) );
		Assert.AreEqual( 1, target.Descendants.Count( x => x.HasClass( "dock-group" ) ) );
		Assert.AreSame( destinationTab, Tab( target, "a" ) );
		Assert.IsTrue( content.IsVisible );
		Assert.IsFalse( relative.Content.IsVisible );
		Assert.AreEqual( 0, content.DeletedCount );
		Assert.AreEqual( 0, ((RecordingPanel)relative.Content).DeletedCount );
	}

	/// <summary>A throwing source observer cannot leave destination ownership, layout or views incomplete.</summary>
	[TestMethod]
	public void ThrowingTransferNotificationLeavesDestinationCommitted()
	{
		var content = new RecordingPanel();
		var item = host.Register( "a", "a", content );
		host.Dock( "a" );
		var sourceTab = Tab( host, "a" );
		var target = new DockHost { Parent = surface.Root };
		target.Register( "relative", "relative", new RecordingPanel() );
		target.Dock( "relative" );
		Frame();
		var container = content.Parent;
		var expectedTarget = new DockLayout();
		expectedTarget.Dock( "relative" );
		expectedTarget.Dock( "a", "relative", DockPosition.Right, 0.3f );
		var failure = new InvalidOperationException( "source observer failed" );
		int sourceChanges = 0;
		int targetChanges = 0;
		host.LayoutChanged += () =>
		{
			sourceChanges++;
			Assert.IsNull( host.Find( "a" ) );
			Assert.IsTrue( target.IsOpen( "a" ) );
			Assert.AreSame( item, target.Find( "a" ) );
			Assert.AreEqual( expectedTarget.Save(), target.State );
			Assert.IsTrue( Tab( target, "a" ).IsValid );
			Assert.IsTrue( container.Parent.HasClass( "dock-body" ) );
			throw failure;
		};
		target.LayoutChanged += () =>
		{
			targetChanges++;
			Assert.IsNull( host.Find( "a" ) );
			Assert.IsTrue( target.IsOpen( "a" ) );
			Assert.AreEqual( expectedTarget.Save(), target.State );
		};

		var thrown = Assert.ThrowsException<InvalidOperationException>( () => host.TransferTo( target, "a", "relative", DockPosition.Right, 0.3f ) );
		Assert.AreSame( failure, thrown );
		Assert.AreEqual( 1, sourceChanges );
		Assert.AreEqual( 1, targetChanges, "the destination is notified even when the source observer throws" );
		Frame();
		Assert.AreEqual( new DockLayout().Save(), host.State );
		Assert.AreEqual( 0, host.Items.Count );
		Assert.IsFalse( sourceTab.IsValid );
		Assert.AreEqual( 0, host.Descendants.Count( x => x.HasClass( "dock-group" ) ) );
		Assert.AreEqual( expectedTarget.Save(), target.State );
		Assert.AreSame( item, target.Find( "a" ) );
		Assert.AreSame( container, content.Parent );
		Assert.AreSame( Tab( target, "a" ).Parent.Parent, container.Parent.Parent );
		Assert.AreEqual( 2, target.Descendants.Count( x => x.HasClass( "dock-group" ) ) );
		Assert.IsTrue( content.IsVisible );
		Assert.AreEqual( 0, content.DeletedCount );
	}

	/// <summary>Placement validation happens before either host changes ownership or its views.</summary>
	[TestMethod]
	[DataRow( "missing", DockPosition.Center, 0.5f, -1 )]
	[DataRow( "target", (DockPosition)99, 0.5f, -1 )]
	[DataRow( "target", DockPosition.Right, 0.0f, -1 )]
	[DataRow( "target", DockPosition.Center, 0.5f, 2 )]
	public void InvalidTransferIsAtomic( string relativeTo, DockPosition position, float fraction, int tabIndex )
	{
		var items = OpenTabs();
		var tabs = items.Select( x => Tab( host, x.Id ) ).ToArray();
		var target = new DockHost { Parent = surface.Root };
		var targetItem = target.Register( "target", "target", new RecordingPanel() );
		target.Dock( "target" );
		var targetTab = Tab( target, "target" );
		Frame();
		var sourceLayout = host.State;
		var targetLayout = target.State;
		var parent = items[0].Content.Parent.Parent;
		int changes = 0;
		host.LayoutChanged += () => changes++;
		target.LayoutChanged += () => changes++;

		if ( relativeTo == "missing" )
			Assert.ThrowsException<ArgumentException>( () => host.TransferTo( target, "a", relativeTo, position, fraction, tabIndex ) );
		else
			Assert.ThrowsException<ArgumentOutOfRangeException>( () => host.TransferTo( target, "a", relativeTo, position, fraction, tabIndex ) );

		Frame();
		Assert.AreEqual( sourceLayout, host.State );
		Assert.AreEqual( targetLayout, target.State );
		Assert.AreEqual( 0, changes );
		Assert.AreSame( parent, items[0].Content.Parent.Parent );
		AssertRetained( host, items, tabs );
		AssertRetained( target, new[] { targetItem }, new[] { targetTab } );
	}

	/// <summary>A duplicate destination ID cannot steal or delete either registration.</summary>
	[TestMethod]
	public void TransferRejectsDuplicateId()
	{
		var content = new RecordingPanel();
		var item = host.Register( "a", "a", content );
		host.Dock( "a" );
		var target = new DockHost { Parent = surface.Root };
		var other = new RecordingPanel();
		var otherItem = target.Register( "a", "a", other );
		target.Dock( "a" );
		var tab = Tab( host, "a" );
		var otherTab = Tab( target, "a" );
		var before = host.State;
		var targetBefore = target.State;
		int changes = 0;
		host.LayoutChanged += () => changes++;
		target.LayoutChanged += () => changes++;

		Assert.ThrowsException<ArgumentException>( () => host.TransferTo( target, "a" ) );
		Assert.AreEqual( before, host.State );
		Assert.AreEqual( targetBefore, target.State );
		Assert.AreEqual( 0, changes );
		AssertRetained( host, new[] { item }, new[] { tab } );
		AssertRetained( target, new[] { otherItem }, new[] { otherTab } );
	}

	/// <summary>Content cannot be transferred into a host inside its own subtree.</summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void TransferRejectsOwnershipCycles( bool closed )
	{
		var content = new RecordingPanel();
		var item = host.Register( "a", "a", content );
		host.Dock( "a" );
		var target = new DockHost { Parent = content };
		if ( closed ) host.Close( "a" );
		var tab = Tab( host, "a" );
		var parent = content.Parent;
		var before = host.State;
		var targetBefore = target.State;
		int changes = 0;
		host.LayoutChanged += () => changes++;
		target.LayoutChanged += () => changes++;

		Assert.ThrowsException<ArgumentException>( () => host.TransferTo( target, "a" ) );
		Assert.AreEqual( before, host.State );
		Assert.AreEqual( targetBefore, target.State );
		Assert.AreEqual( 0, changes );
		Assert.AreEqual( 0, target.Items.Count );
		Assert.AreSame( parent, content.Parent );
		Assert.AreSame( content, target.Parent );
		AssertRetained( host, new[] { item }, new[] { tab } );
	}

	/// <summary>Duplicate IDs, reused content and ancestor content leave existing ownership alone.</summary>
	[TestMethod]
	public void RegistrationRejectsDuplicatesAndCycles()
	{
		var content = new RecordingPanel();
		var item = host.Register( "a", "a", content );
		var spare = new RecordingPanel { Parent = surface.Root };
		var parent = content.Parent;
		var other = new DockHost { Parent = surface.Root };

		Assert.ThrowsException<ArgumentException>( () => host.Register( "a", "duplicate", spare ) );
		Assert.ThrowsException<ArgumentException>( () => host.Register( "alias", "alias", content ) );
		Assert.ThrowsException<ArgumentException>( () => other.Register( "alias", "alias", content ) );
		Assert.ThrowsException<ArgumentException>( () => host.Register( "self", "self", host ) );
		Assert.ThrowsException<ArgumentException>( () => host.Register( "root", "root", surface.Root ) );
		Assert.AreSame( item, host.Find( "a" ) );
		Assert.AreSame( parent, content.Parent );
		Assert.AreSame( surface.Root, spare.Parent );
		Assert.AreEqual( 1, host.Items.Count );
		Assert.AreEqual( 0, other.Items.Count );
		Assert.IsFalse( host.IsOpen( "a" ), "registration alone leaves content closed" );
		Assert.AreEqual( 0, content.DeletedCount );
		Assert.AreEqual( 0, spare.DeletedCount );
	}

	/// <summary>Floating IDs cannot be reused, but their original content can return.</summary>
	[TestMethod]
	public void FloatingReservationRejectsIdReuseAndAllowsOriginalReturn()
	{
		var original = host.Register( "a", "a", new RecordingPanel() );
		host.Dock( "a" );
		var floating = new DockHost { Parent = surface.Root };
		host.ReservedItems.Add( "a", original );
		host.TransferTo( floating, "a" );
		var spare = new RecordingPanel { Parent = surface.Root };
		Assert.ThrowsException<ArgumentException>( () => host.Register( "a", "a", spare ) );

		var other = new DockHost { Parent = surface.Root };
		other.Register( "a", "a", spare );
		other.Dock( "a" );
		Assert.ThrowsException<ArgumentException>( () => other.TransferTo( host, "a" ) );
		Assert.IsTrue( other.IsOpen( "a" ) );
		floating.TransferTo( host, "a" );
		Assert.AreSame( original, host.Find( "a" ) );
		Assert.IsTrue( host.IsOpen( "a" ) );
	}

	/// <summary>A lone floating tab remains visually distinct from the window's native drag header.</summary>
	[TestMethod]
	public void FloatingSingleTabKeepsNormalTabWidth()
	{
		host.AddClass( "floating" );
		host.Register( "a", "Inspector", new RecordingPanel(), icon: "tune" );
		host.Dock( "a" );
		Frame();
		var tab = Tab( host, "Inspector" );
		var close = tab.Children.Single( x => x.HasClass( "dock-tab-action" ) );
		Assert.IsTrue( tab.Box.Rect.Width < tab.Parent.Box.Rect.Width );
		var singleWidth = tab.Box.Rect.Width;
		Assert.IsTrue( close.Box.Rect.Right > tab.Box.Rect.Right - 12 );
		Assert.IsTrue( tab.Parent.Parent.HasClass( "single-tab" ) );

		host.Register( "b", "Console", new RecordingPanel(), icon: "terminal" );
		host.Dock( "b" );
		Frame();
		Assert.IsFalse( tab.Parent.Parent.HasClass( "single-tab" ) );
		Assert.AreEqual( singleWidth, tab.Box.Rect.Width, 1.0f );
		Assert.IsTrue( tab.Box.Rect.Width < tab.Parent.Box.Rect.Width );
	}

	/// <summary>A returned, never-opened pane retains its closed state even if it cannot be closed by the user.</summary>
	[TestMethod]
	public void ReturningClosedNonclosableItemPreservesItsState()
	{
		var item = host.Register( "a", "a", new RecordingPanel(), canClose: false );
		var target = new DockHost { Parent = surface.Root };
		host.TransferTo( target, "a" );
		target.HideTransferredItem( "a" );
		Assert.IsFalse( target.IsOpen( "a" ) );
		Assert.AreSame( item, target.Find( "a" ) );
		Assert.IsTrue( item.Content.IsValid );
	}

	/// <summary>Unknown IDs and invalid saved registrations cannot disturb live content or tabs.</summary>
	[TestMethod]
	public void UnknownIdsAndInvalidRestoresLeaveViewsUntouched()
	{
		var items = OpenTabs();
		var tabs = items.Select( x => Tab( host, x.Id ) ).ToArray();
		var before = host.State;
		int changes = 0;
		host.LayoutChanged += () => changes++;

		Assert.IsNull( host.Find( "missing" ) );
		Assert.IsNull( host.Find( null ) );
		Assert.IsFalse( host.IsOpen( "missing" ) );
		Assert.IsFalse( host.Close( "missing" ) );
		Assert.IsFalse( host.Activate( "missing" ) );
		Assert.ThrowsException<ArgumentException>( () => host.Dock( "missing" ) );
		foreach ( var json in new[]
		{
			"not json",
			"""{"version":1,"root":{"type":"group","tabs":["a","missing"],"activeId":"a"}}""",
			"""{"version":1,"root":{"type":"group","tabs":["a","a"],"activeId":"a"}}"""
		} )
		{
			Assert.IsFalse( host.RestoreState( json ) );
			Assert.AreEqual( before, host.State );
			AssertRetained( host, items, tabs );
		}
		Assert.AreEqual( 0, changes );
	}

	/// <summary>Noncloseable tabs resist API close, middle click and omission during restore.</summary>
	[TestMethod]
	public void NoncloseablePanelsStayOpen()
	{
		var pinned = host.Register( "pinned", "pinned", new RecordingPanel(), canClose: false );
		var optional = host.Register( "optional", "optional", new RecordingPanel() );
		host.Dock( "optional" );
		var withoutPinned = host.State;
		Assert.IsTrue( host.RestoreState( withoutPinned ) );
		Assert.IsFalse( host.IsOpen( "pinned" ), "a never-opened noncloseable item may remain omitted" );
		host.Dock( "pinned" );
		Frame();
		var tab = Tab( host, "pinned" );
		var optionalTab = Tab( host, "optional" );
		var before = host.State;
		int changes = 0;
		host.LayoutChanged += () => changes++;

		Assert.IsFalse( tab.Children.Any( x => x.Tooltip == "Close panel" ) );
		Assert.IsTrue( optionalTab.Children.Any( x => x.Tooltip == "Close panel" ) );
		Assert.IsFalse( host.Close( "pinned" ) );
		MoveTo( tab.Children.OfType<Label>().Single( x => x.HasClass( "dock-tab-title" ) ).Box.Rect.Center );
		Assert.AreSame( tab, surface.Hovered );
		MouseButton( true, MouseButtons.Middle );
		MouseButton( false, MouseButtons.Middle );
		Assert.IsFalse( host.RestoreState( withoutPinned ) );
		Assert.IsFalse( host.RestoreState( new DockLayout().Save() ) );
		Assert.AreEqual( before, host.State );
		Assert.AreEqual( 0, changes );
		Assert.IsTrue( host.IsOpen( "pinned" ) );
		AssertRetained( host, new[] { pinned, optional }, new[] { tab, optionalTab } );

		MoveTo( optionalTab.Children.OfType<Label>().Single( x => x.HasClass( "dock-tab-title" ) ).Box.Rect.Center );
		Assert.AreSame( optionalTab, surface.Hovered );
		MouseButton( true, MouseButtons.Middle );
		MouseButton( false, MouseButtons.Middle );
		Assert.IsFalse( host.IsOpen( "optional" ), "the same input closes a closeable tab" );
		Assert.IsTrue( host.IsOpen( "pinned" ) );
		host.Dock( "optional" );

		var pinnedOnly = new DockLayout();
		pinnedOnly.Dock( "pinned" );
		Assert.IsTrue( host.RestoreState( pinnedOnly.Save() ) );
		Frame();
		Assert.IsTrue( pinned.Content.IsVisible );
		Assert.IsFalse( host.IsOpen( "optional" ) );
		Assert.IsFalse( optional.Content.IsVisible );
		AssertRetained( host, new[] { pinned, optional }, new[] { tab, optionalTab } );
	}

	/// <summary>Dragging an inactive tab past its siblings reorders the same tab and content.</summary>
	[TestMethod]
	[DataRow( 0.5f )]
	[DataRow( 0.8f )]
	public void MouseDragReordersTabs( float tabHeightFraction )
	{
		var items = OpenTabs();
		var tabs = items.Select( x => Tab( host, x.Id ) ).ToArray();
		var rect = tabs[2].Box.Rect;
		BeginDrag( tabs[0], new Vector2( rect.Right + 8, rect.Top + rect.Height * tabHeightFraction ) );
		Assert.IsTrue( host.Descendants.Single( x => x.HasClass( "dock-preview" ) ).IsVisible );
		MouseButton( false );

		CollectionAssert.AreEqual( new[] { tabs[1], tabs[2], tabs[0] }, tabs[0].Parent.Children.Where( x => x.HasClass( "dock-tab" ) ).ToArray() );
		Assert.AreEqual( 1, host.Descendants.Count( x => x.HasClass( "dock-group" ) ) );
		Assert.IsTrue( tabs[0].HasClass( "selected" ) );
		Assert.IsFalse( tabs[0].HasClass( "dragging" ) );
		Assert.IsTrue( items[0].Content.IsVisible );
		AssertRetained( host, items, tabs );
	}

	/// <summary>Dropping on a root guide splits the workspace without replacing content or tabs.</summary>
	[TestMethod]
	[DataRow( DockPosition.Left )]
	[DataRow( DockPosition.Right )]
	[DataRow( DockPosition.Top )]
	[DataRow( DockPosition.Bottom )]
	public void MouseDragSplitsAtRootGuide( DockPosition position )
	{
		var items = OpenTabs();
		var tabs = items.Select( x => Tab( host, x.Id ) ).ToArray();
		BeginDrag( tabs[0], host.Box.Rect.Position + host.Box.Rect.Size * 0.25f );
		var guide = host.Descendants.Single( x => x.HasClass( $"dock-root-guide-{position.ToString().ToLowerInvariant()}" ) );
		Assert.IsTrue( guide.IsVisible );
		MoveTo( guide.Box.Rect.Center );
		Frame();
		Assert.IsTrue( guide.HasClass( "hovered" ) );
		Assert.IsTrue( host.Descendants.Single( x => x.HasClass( "dock-preview" ) ).IsVisible );
		Assert.AreEqual( new DockDropTarget( null, position ), host.UpdateDockTargets( guide.Box.Rect.Center, "a" ).Value );
		var expected = new DockLayout();
		Assert.IsTrue( expected.Restore( host.State, items.Select( x => x.Id ) ) );
		expected.Dock( "a", position: position );
		MouseButton( false );

		Assert.AreEqual( expected.Save(), host.State );
		Assert.AreEqual( 2, host.Descendants.Count( x => x.HasClass( "dock-group" ) ) );
		Assert.AreNotSame( tabs[0].Parent, tabs[1].Parent );
		Assert.AreSame( tabs[1].Parent, tabs[2].Parent );
		Assert.IsTrue( items[0].Content.IsVisible );
		AssertRetained( host, items, tabs );
	}

	/// <summary>Each group guide selects an explicit placement relative to the hovered group.</summary>
	[TestMethod]
	[DataRow( DockPosition.Left )]
	[DataRow( DockPosition.Right )]
	[DataRow( DockPosition.Top )]
	[DataRow( DockPosition.Bottom )]
	[DataRow( DockPosition.Center )]
	public void MouseDragDocksAtGroupGuide( DockPosition position )
	{
		var items = OpenTabs();
		host.Dock( "b", "a", DockPosition.Right );
		host.Dock( "c", "b" );
		Frame();
		var tabs = items.Select( x => Tab( host, x.Id ) ).ToArray();
		var group = tabs[1].Parent.Parent;
		BeginDrag( tabs[0], group.Box.Rect.Position + group.Box.Rect.Size * 0.25f );
		Assert.IsFalse( host.Descendants.Single( x => x.HasClass( "dock-preview" ) ).IsVisible );
		var guide = host.Descendants.Single( x => x.HasClass( $"dock-group-guide-{position.ToString().ToLowerInvariant()}" ) );
		Assert.IsTrue( guide.IsVisible );
		MoveTo( guide.Box.Rect.Center );
		Frame();
		Assert.IsTrue( guide.HasClass( "hovered" ) );
		Assert.IsTrue( host.Descendants.Single( x => x.HasClass( "dock-preview" ) ).IsVisible );
		Assert.AreEqual( new DockDropTarget( "c", position ), host.UpdateDockTargets( guide.Box.Rect.Center, "a" ).Value );
		var expected = new DockLayout();
		Assert.IsTrue( expected.Restore( host.State, items.Select( x => x.Id ) ) );
		expected.Dock( "a", "c", position );
		MouseButton( false );

		Assert.AreEqual( expected.Save(), host.State );
		Assert.IsFalse( tabs[0].HasClass( "dragging" ) );
		Assert.IsFalse( host.Descendants.Single( x => x.HasClass( "dock-targets" ) ).IsVisible );
		AssertRetained( host, items, tabs );
	}

	/// <summary>Body quadrants and edges outside guide rectangles do not select or commit a drop.</summary>
	[TestMethod]
	[DataRow( 0.25f, 0.25f )]
	[DataRow( 0.75f, 0.25f )]
	[DataRow( 0.25f, 0.75f )]
	[DataRow( 0.75f, 0.75f )]
	[DataRow( 0.1f, 0.5f )]
	[DataRow( 0.9f, 0.5f )]
	[DataRow( 0.5f, 0.1f )]
	[DataRow( 0.5f, 0.9f )]
	public void MouseDragOutsideGuidesDoesNotDock( float x, float y )
	{
		var items = OpenTabs();
		var tabs = items.Select( item => Tab( host, item.Id ) ).ToArray();
		var bounds = host.Box.Rect;
		var point = bounds.Position + bounds.Size * new Vector2( x, y );
		BeginDrag( tabs[0], point );
		var before = host.State;
		Assert.IsNull( host.UpdateDockTargets( point, "a" ) );
		Frame();
		Assert.IsFalse( host.Descendants.Single( panel => panel.HasClass( "dock-preview" ) ).IsVisible );
		Assert.IsFalse( host.Descendants.Any( panel => panel.HasClass( "dock-guide" ) && panel.HasClass( "hovered" ) ) );
		MouseButton( false );
		Assert.AreEqual( before, host.State );
		AssertRetained( host, items, tabs );
	}

	/// <summary>Every eligible section is outlined; only the hovered section shows a group cross.</summary>
	[TestMethod]
	[DataRow( "external", 3 )]
	[DataRow( "a", 2 )]
	public void DockTargetsOutlineEveryEligibleSection( string draggedId, int sectionCount )
	{
		OpenTabs();
		host.Dock( "b", "a", DockPosition.Right );
		host.Dock( "c", "b", DockPosition.Bottom );
		Frame();
		var groups = host.Descendants.Where( x => x.HasClass( "dock-group" ) ).ToArray();
		Assert.IsNull( host.UpdateDockTargets( null, draggedId ) );
		Frame();
		var sections = host.Descendants.Where( x => x.HasClass( "dock-section" ) && x.IsVisible ).ToArray();
		Assert.AreEqual( sectionCount, sections.Length );
		foreach ( var group in groups )
		{
			var eligible = draggedId != "a" || group != Tab( host, "a" ).Parent.Parent;
			Assert.AreEqual( eligible, sections.Any( x => x.Box.Rect == group.Box.Rect ) );
			if ( !eligible ) continue;
			Assert.IsNotNull( host.UpdateDockTargets( group.Box.Rect.Center, draggedId ) );
			Frame();
			Assert.AreEqual( group.Box.Rect, sections.Single( x => x.HasClass( "hovered" ) ).Box.Rect );
			Assert.AreEqual( 5, host.Descendants.Count( x => x.HasClass( "dock-guide" ) && x.IsVisible && x.Classes.Contains( "dock-group-guide-" ) ) );
			Assert.IsTrue( host.Descendants.Single( x => x.HasClass( "dock-preview" ) ).IsVisible );
		}

		Assert.IsNull( host.UpdateDockTargets( null, draggedId ) );
		Frame();
		Assert.IsTrue( sections.All( x => x.IsVisible && !x.HasClass( "hovered" ) ) );
		Assert.AreEqual( 4, host.Descendants.Count( x => x.HasClass( "dock-guide" ) && x.IsVisible ) );
		Assert.IsFalse( host.Descendants.Any( x => x.Classes.Contains( "dock-group-guide-" ) && x.IsVisible ) );
		Assert.IsFalse( host.Descendants.Any( x => x.HasClass( "dock-guide" ) && x.HasClass( "hovered" ) ) );
		Assert.IsFalse( host.Descendants.Single( x => x.HasClass( "dock-preview" ) ).IsVisible );
		host.CancelDrag();
		Frame();
		Assert.IsFalse( host.Descendants.Single( x => x.HasClass( "dock-targets" ) ).IsVisible );
		Assert.IsTrue( sections.All( x => !x.IsVisible ) );
	}

	/// <summary>An empty workspace accepts only its center guide, not its whole body.</summary>
	[TestMethod]
	public void EmptyHostOffersCenterGuide()
	{
		Frame();
		Assert.IsNull( host.UpdateDockTargets( null, "external" ) );
		Frame();
		Assert.AreEqual( 1, host.Descendants.Count( x => x.HasClass( "dock-section" ) && x.IsVisible ) );
		Assert.IsFalse( host.Descendants.Any( x => x.HasClass( "dock-guide" ) && x.IsVisible ) );
		Assert.IsFalse( host.Descendants.Single( x => x.HasClass( "dock-preview" ) ).IsVisible );
		Assert.IsNull( host.UpdateDockTargets( host.Box.Rect.Position + host.Box.Rect.Size * 0.25f, "external" ) );
		Frame();
		var guide = host.Descendants.Single( x => x.HasClass( "dock-guide" ) && x.IsVisible );
		Assert.IsTrue( guide.HasClass( "dock-group-guide-center" ) );
		Assert.AreEqual( new DockDropTarget( null, DockPosition.Center ), host.UpdateDockTargets( guide.Box.Rect.Center, "external" ).Value );
		Frame();
		Assert.IsTrue( host.Descendants.Single( x => x.HasClass( "dock-preview" ) ).IsVisible );
	}

	/// <summary>Section, guide and preview overlays leave underlying content and tabs hittable.</summary>
	[TestMethod]
	public void DockOverlaysPassPointerThrough()
	{
		var items = OpenTabs();
		var center = host.Box.Rect.Center;
		Assert.IsNotNull( host.UpdateDockTargets( center, "external" ) );
		Frame();
		MoveTo( center );
		Assert.AreSame( items[2].Content, surface.Hovered );
		var tab = Tab( host, "a" );
		MoveTo( tab.Children.OfType<Label>().Single( x => x.HasClass( "dock-tab-title" ) ).Box.Rect.Center );
		Assert.AreSame( tab, surface.Hovered );
		MouseButton( true );
		MouseButton( false );
		Assert.IsTrue( tab.HasClass( "selected" ) );
		Assert.IsTrue( items[0].Content.IsVisible );
	}

	/// <summary>Tab icons are optional metadata; close is the only tab action.</summary>
	[TestMethod]
	[DataRow( null, true )]
	[DataRow( "folder", true )]
	[DataRow( "tune", false )]
	public void TabIconsAndCloseActions( string icon, bool canClose )
	{
		var item = host.Register( "a", "Pane", new RecordingPanel(), canClose, icon );
		host.Dock( "a" );
		Frame();
		var tab = Tab( host, "Pane" );
		Assert.AreEqual( icon, item.Icon );
		var icons = tab.Children.OfType<IconPanel>().Where( x => x.HasClass( "dock-tab-icon" ) ).ToArray();
		Assert.AreEqual( icon is null ? 0 : 1, icons.Length );
		if ( icon is not null )
		{
			Assert.AreEqual( icon, icons[0].Text );
			MoveTo( icons[0].Box.Rect.Center );
			Assert.AreSame( tab, surface.Hovered );
		}
		var actions = tab.Children.Where( x => x.HasClass( "dock-tab-action" ) ).ToArray();
		Assert.AreEqual( canClose ? 1 : 0, actions.Length );
		if ( canClose )
		{
			Assert.AreEqual( "Close panel", actions[0].Tooltip );
			Assert.AreEqual( "close", actions[0].Children.OfType<IconPanel>().Single().Text );
		}
	}

	/// <summary>Escape cancels a pending drop through the surface's real keyboard route.</summary>
	[TestMethod]
	public void EscapeCancelsTabDrag()
	{
		OpenTabs();
		var tab = Tab( host, "a" );
		BeginDrag( tab, new Vector2( host.Box.Rect.Right - 5, host.Box.Rect.Center.y ) );
		MoveTo( host.Descendants.Single( x => x.HasClass( "dock-root-guide-right" ) ).Box.Rect.Center );
		Frame();
		Assert.IsTrue( host.Descendants.Single( x => x.HasClass( "dock-preview" ) ).IsVisible );
		var before = host.State;
		surface.SetKey( "escape", true );
		Frame();
		surface.SetKey( "escape", false );
		Frame();
		Assert.IsFalse( tab.HasClass( "dragging" ) );
		Assert.IsFalse( host.Descendants.Single( x => x.HasClass( "dock-preview" ) ).IsVisible );
		Assert.IsFalse( host.Descendants.Single( x => x.HasClass( "dock-targets" ) ).IsVisible );
		MouseButton( false );
		Assert.AreEqual( before, host.State );
	}

	/// <summary>Explicit cancellation and leaving the surface cannot commit a later mouse release.</summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void CancelledDragDoesNotDrop( bool leaveSurface )
	{
		OpenTabs();
		var tab = Tab( host, "a" );
		BeginDrag( tab, new Vector2( host.Box.Rect.Right - 5, host.Box.Rect.Center.y ) );
		MoveTo( host.Descendants.Single( x => x.HasClass( "dock-root-guide-right" ) ).Box.Rect.Center );
		Frame();
		Assert.IsTrue( host.Descendants.Single( x => x.HasClass( "dock-preview" ) ).IsVisible );
		var before = host.State;
		if ( leaveSurface ) surface.MouseInside = false;
		else host.CancelDrag();
		Frame();
		Assert.IsFalse( tab.HasClass( "dragging" ) );
		Assert.IsFalse( host.Descendants.Single( x => x.HasClass( "dock-preview" ) ).IsVisible );
		Assert.IsFalse( host.Descendants.Single( x => x.HasClass( "dock-targets" ) ).IsVisible );
		surface.MouseInside = true;
		MouseButton( false );
		Assert.AreEqual( before, host.State );
	}

	/// <summary>Splitter input respects recursive minimum sizes on both sides and stops on release.</summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void SplitterDragRespectsNestedMinimumSizes( bool vertical )
	{
		OpenTabs();
		var edge = vertical ? DockPosition.Bottom : DockPosition.Right;
		host.Dock( "b", "a", edge );
		host.Dock( "c", "a", edge );
		Frame();
		var split = host.Descendants.First( x => x.HasClass( "dock-split" ) );
		var handle = split.Children.Single( x => x.HasClass( "dock-splitter" ) );
		var branches = split.Children.Where( x => x.HasClass( "dock-branch" ) ).ToArray();
		var bounds = split.Box.Rect;
		float Size( Panel panel ) => vertical ? panel.Box.Rect.Height : panel.Box.Rect.Width;
		var minimum = vertical ? 80 : 120;

		MoveTo( handle.Box.Rect.Center );
		Assert.AreSame( handle, surface.Hovered );
		MouseButton( true );
		MoveTo( vertical ? new Vector2( bounds.Center.x, bounds.Top + 1 ) : new Vector2( bounds.Left + 1, bounds.Center.y ) );
		Assert.IsTrue( split.HasClass( "resizing" ) );
		Assert.AreEqual( minimum * 2 + 5, Size( branches[0] ), 1.0f, "the nested pair and its splitter fit" );
		Assert.IsTrue( Size( branches[1] ) >= minimum - 1 );

		MoveTo( vertical ? new Vector2( bounds.Center.x, bounds.Bottom - 1 ) : new Vector2( bounds.Right - 1, bounds.Center.y ) );
		Assert.AreEqual( minimum, Size( branches[1] ), 1.0f );
		Assert.IsTrue( Size( branches[0] ) >= minimum * 2 + 4 );
		Assert.AreEqual( Size( split ) - 5, Size( branches[0] ) + Size( branches[1] ), 1.0f );
		MouseButton( false );
		Assert.IsFalse( split.HasClass( "resizing" ) );
		var saved = host.State;
		MoveTo( bounds.Center );
		Assert.AreEqual( saved, host.State );
	}

	/// <summary>A guide clipped by an ancestor cannot accept a drop.</summary>
	[TestMethod]
	[DataRow( true )]
	[DataRow( false )]
	public void DockTargetsRespectAncestorClipping( bool clipped )
	{
		OpenTabs();
		var clip = new Panel { Parent = surface.Root };
		clip.Style.Position = PositionMode.Absolute;
		clip.Style.Width = 200;
		clip.Style.Height = 80;
		if ( clipped ) clip.Style.Overflow = OverflowMode.Hidden;
		host.Parent = clip;
		host.Style.Width = 200;
		host.Style.Height = 400;
		host.Style.FlexShrink = 0;
		Frame();
		Assert.IsTrue( host.Box.Rect.Center.y > clip.Box.Rect.Bottom );
		Assert.AreEqual( !clipped, host.UpdateDockTargets( host.Box.Rect.Center, "external" ).HasValue );
		var tabs = host.Descendants.Single( x => x.HasClass( "dock-tabs" ) );
		Assert.IsNotNull( host.UpdateDockTargets( tabs.Box.Rect.Center, "external" ) );
	}

	/// <summary>Insertion previews fit even when less than three UI pixels remain.</summary>
	[TestMethod]
	[DataRow( 1.0f )]
	[DataRow( 2.0f )]
	public void NarrowTabInsertionPreviewFits( float dpi )
	{
		OpenTabs();
		surface.DpiScale = dpi;
		surface.Size = new Vector2( 2, 600 );
		Frame();
		var tabs = host.Descendants.Single( x => x.HasClass( "dock-tabs" ) );
		Assert.IsNotNull( host.UpdateDockTargets( tabs.Box.Rect.Center, "external" ) );
		Frame();
		var preview = host.Descendants.Single( x => x.HasClass( "dock-preview" ) );
		Assert.IsTrue( preview.Box.Rect.Left >= tabs.Box.Rect.Left );
		Assert.IsTrue( preview.Box.Rect.Right <= tabs.Box.Rect.Right );
	}

	/// <summary>Escape restores the splitter's starting proportion and ends resize input.</summary>
	[TestMethod]
	public void EscapeCancelsSplitterResize()
	{
		OpenTabs();
		host.Dock( "b", "a", DockPosition.Right, 0.35f );
		Frame();
		var split = host.Descendants.Single( x => x.HasClass( "dock-split" ) );
		var handle = split.Children.Single( x => x.HasClass( "dock-splitter" ) );
		var before = host.State;
		var start = handle.Box.Rect.Center;
		MoveTo( start );
		MouseButton( true );
		Assert.AreSame( handle, surface.Focus );
		MoveTo( start - new Vector2( 100, 0 ) );
		Assert.AreNotEqual( before, host.State );
		surface.SetKey( "escape", true );
		Frame();
		surface.SetKey( "escape", false );
		Frame();
		Assert.IsFalse( split.HasClass( "resizing" ) );
		Assert.AreEqual( before, host.State );
		Assert.AreEqual( start.x, handle.Box.Rect.Center.x, 1.0f );
		MouseButton( false );
		MoveTo( start - new Vector2( 150, 0 ) );
		Assert.AreEqual( before, host.State );
	}
}
