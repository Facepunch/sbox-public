using Sandbox.UI;
using System;

namespace UITests.Controls;

public partial class DockHostTests
{
	/// <summary>Closed registrations stay lazy through saving and ticking, and reuse content on reopen.</summary>
	[TestMethod]
	public void WorkspaceFactoryCreatesContentOnlyWhenOpened()
	{
		int calls = 0;
		var item = host.Register( "lazy", "Lazy", () => { calls++; return new Panel(); } );
		using var windows = new Editor.PanelDockWindows( host );
		var closed = windows.State;
		Frame();
		Assert.AreEqual( 0, calls );
		Assert.IsNull( item.Content );
		Assert.IsTrue( windows.RestoreState( closed ) );
		Assert.AreEqual( 0, calls );
		host.Dock( "lazy" );
		var content = item.Content;
		var opened = host.State;
		host.Close( "lazy" );
		Assert.IsTrue( host.RestoreState( opened ) );
		Assert.AreEqual( 1, calls );
		Assert.AreSame( content, item.Content );
	}

	/// <summary>Game tabs offer Close but not Float, and their temporary menus can be dismissed safely.</summary>
	[TestMethod]
	public void SurfaceTabContextMenuClosesWithoutFloating()
	{
		using var windows = new Editor.PanelDockWindows( host );
		host.Register( "a", "Inspector", new RecordingPanel() );
		host.Dock( "a" );
		Frame();
		var tab = Tab( host, "Inspector" );
		tab.CreateEvent( new MousePanelEvent( "onrightclick", tab, "mouseright" ) );
		Frame();
		var close = surface.Root.Descendants.OfType<Menu>().Single( x => x.Text == "Close" );
		var menu = close.RootMenu;
		Assert.IsFalse( menu.Options.Any( x => x.Text == "Float" ) );
		Assert.IsFalse( windows.Float( "a" ) );
		menu.Close();
		Assert.IsFalse( menu.IsValid );
		Assert.IsTrue( host.IsOpen( "a" ) );
	}

	/// <summary>Desktop adapters cannot take over dragging in a game or standalone UI surface.</summary>
	[TestMethod]
	public void DesktopAdapterKeepsSurfaceDraggingWithoutAnEditorWindow()
	{
		using var windows = new Editor.PanelDockWindows( host );
		Assert.IsFalse( windows.Float( "a" ) );
		Assert.IsFalse( host.UsesWindowDragging );
		MouseDragReordersTabs( 0.5f );
	}

	/// <summary>Workspace restores notify observers only after all layout edits are finished.</summary>
	[TestMethod]
	public void WorkspaceRestoreRetainsContentAndClosedRegistrations()
	{
		var items = OpenTabs();
		using var windows = new Editor.PanelDockWindows( host );
		host.Close( "b" );
		var saved = windows.State;
		host.Dock( "b", "a", DockPosition.Right );
		int notifications = 0;
		string observed = null;
		host.LayoutChanged += () =>
		{
			notifications++;
			Assert.IsFalse( host.IsOpen( "b" ) );
			observed = windows.State;
		};
		Assert.IsTrue( windows.RestoreState( saved ) );
		Assert.AreEqual( 1, notifications );
		Assert.AreEqual( saved, observed );
		Assert.AreEqual( saved, windows.State );
		Assert.IsTrue( items.All( x => x.Content.IsValid && host.Find( x.Id ) == x ) );
		Assert.IsFalse( windows.RestoreState( "{}" ) );
		Assert.AreEqual( saved, windows.State );
	}

}
