using Native;
using System;
using System.Diagnostics;

namespace Editor.MapEditor;

/// <summary>
/// This is our CQHammerMainWnd
/// </summary>
public partial class HammerMainWindow : DockWindow
{
	Native.CQHammerMainWnd _nativeHammerWindow;
	internal HammerMainWindow( Native.CQHammerMainWnd ptr ) : base( ptr )
	{
		_nativeHammerWindow = ptr;
		Hammer.Window = this;
	}

	internal void WindowInit()
	{
		Size = new( 1580, 900 );

		DockAttribute.RegisterWindow( "Hammer", this );

		// All dock widgets
		_nativeHammerWindow.CreateEverything();

		// Main menu bar and populate it, then do our managed menu
		_nativeHammerWindow.CreateMenus();
		MenuBar.RegisterNamed( "Hammer", MenuBar );

		// Shows some tool bars... ?
		_nativeHammerWindow.SetupDefaultLayout();

		// Hammer's default layout is built by native code, so snapshot it to restore on reset
		_defaultLayout = DockManager.State;

		// Saves & loads layout and shit
		StateCookie = "SboxHammer";

		// Set the focus back to the main window so it isn't on some random widget which will eat key bindings.
		Focus();

		SetWindowIcon( "hammer/appicon.png" );
	}

	internal void CreateDynamicViewMenu( QMenu nativeMenu )
	{
		CreateDynamicViewMenu( new Menu( nativeMenu ) );
	}

	/// <summary>
	/// Lets Hammer register its dock widgets with our DockManager
	/// </summary>
	internal void AddNativeDock( string name, string icon, IntPtr sibling, IntPtr window, DockArea dockArea = DockArea.Left )
	{
		if ( window == default ) return;

		var widget = new Widget( window ) { WindowTitle = name, Name = name };
		widget.SetWindowIcon( icon );

		var relativeTo = sibling != default ? DockManager.FindDockWidget( new Widget( sibling ) ) : null;
		DockManager.AddDock( name, icon, widget, dockArea, relativeTo );
	}

	internal void ToggleAssetBrowser()
	{
		DockManager.SetDockState( "Asset Browser", !DockManager.IsDockOpen( "Asset Browser" ) );
	}

	internal void ToggleFullscreenLayout( bool fullscreen )
	{
		if ( fullscreen )
		{
			SaveLayout();

			foreach ( var dock in DockManager.DockTypes )
			{
				if ( dock.Title == "Map View" ) continue;
				DockManager.SetDockState( dock.Title, false );
			}
		}
		else
		{
			RestoreLayout();
		}
	}

	string _defaultLayout;

	protected override void BuildDefaultLayout()
	{
		if ( !string.IsNullOrEmpty( _defaultLayout ) )
			DockManager.State = _defaultLayout;
	}

	internal static uint InitHammerMainWindow( Native.CQHammerMainWnd ptr )
	{
		var win = new HammerMainWindow( ptr );
		return InteropSystem.GetAddress( win, true );
	}
}
