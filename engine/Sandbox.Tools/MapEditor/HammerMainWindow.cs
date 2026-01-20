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

		// Saves & loads layout and shit
		StateCookie = "SboxHammer";

		DockManager.Update();

		// Set the focus back to the main window so it isn't on some random widget which will eat key bindings.
		Focus();

		SetWindowIcon( "hammer/appicon.png" );
	}

	internal void CreateDynamicViewMenu( QMenu nativeMenu )
	{
		CreateDynamicViewMenu( new Menu( nativeMenu ) );
	}

	protected override void RestoreDefaultDockLayout()
	{
		var defaultLayout = "{\"floatingWindows\":[],\"mainWrapper\":{\"geometry\":\"AdnQywADAAAAAAAAAAAAAAAACcoAAAUHAAAAAAAAAAAAAAnKAAAFBwAAAAAAAAAACgAAAAAAAAAAAAAACcoAAAUH\",\"splitter\":{\"items\":[{\"items\":[{\"currentIndex\":0,\"objects\":[{\"data\":null,\"managedType\":\"\",\"name\":\"Tool Properties\"}],\"type\":\"area\"},{\"currentIndex\":0,\"objects\":[{\"data\":null,\"managedType\":\"\",\"name\":\"Active Material\"}],\"type\":\"area\"}],\"state\":\"AAAA/wAAAAEAAAACAAABaAAAAH8A/////wEAAAACAA==\",\"type\":\"splitter\"},{\"items\":[{\"currentIndex\":0,\"objects\":[{\"data\":null,\"managedType\":\"\",\"name\":\"Map View\"}],\"type\":\"area\"},{\"currentIndex\":0,\"objects\":[{\"data\":null,\"managedType\":\"HammerAssetBrowser\",\"name\":\"Asset Browser\"},{\"data\":null,\"managedType\":\"HammerCloudBrowser\",\"name\":\"Cloud Browser\"}],\"type\":\"area\"}],\"state\":\"AAAA/wAAAAEAAAACAAADsQAAAfYA/////wEAAAACAA==\",\"type\":\"splitter\"},{\"items\":[{\"currentIndex\":0,\"objects\":[{\"data\":null,\"managedType\":\"\",\"name\":\"Outliner\"},{\"data\":null,\"managedType\":\"\",\"name\":\"Selection Sets\"}],\"type\":\"area\"},{\"currentIndex\":0,\"objects\":[{\"data\":null,\"managedType\":\"\",\"name\":\"Object Properties\"}],\"type\":\"area\"}],\"state\":\"AAAA/wAAAAEAAAACAAAA8AAAAPAA/////wEAAAACAA==\",\"type\":\"splitter\"}],\"state\":\"AAAA/wAAAAEAAAADAAABYgAABFMAAAFqAP////8BAAAAAQA=\",\"type\":\"splitter\"}},\"toolWindowManagerStateFormat\":1}";
		DockManager.State = defaultLayout;
	}

	/// <summary>
	/// Lets Hammer register its dock widgets with our DockManager
	/// </summary>
	internal void AddNativeDock( string name, string icon, IntPtr sibling, IntPtr window, DockArea dockArea = DockArea.Left, DockManager.DockProperty properties = default, float split = 0.5f )
	{
		var siblingWidget = sibling != default ? new Widget( sibling ) : null;
		var windowWidget = window != default ? new Widget( window ) : null;

		// Never delete these, Hammer will go fucking mental
		properties |= DockManager.DockProperty.HideOnClose;

		windowWidget.WindowTitle = name;
		windowWidget.Name = name;
		windowWidget.SetWindowIcon( icon );

		DockManager.RegisterDockType( name, icon, () => throw new UnreachableException( $"{windowWidget} shouldn't have ever been deleted" ), false );
		DockManager.AddDock( siblingWidget, windowWidget, dockArea, properties, split );
	}

	internal void ToggleAssetBrowser()
	{
		DockManager.SetDockState( "Asset Browser", !DockManager.IsDockOpen( "Asset Browser" ) );
	}

	internal void ToggleFullscreenLayout( bool fullscreen )
	{
		if ( fullscreen )
		{
			SaveToStateCookie();

			foreach ( var dock in DockManager.DockTypes )
			{
				if ( dock.Title == "Map View" ) continue;
				DockManager.SetDockState( dock.Title, false );
			}
		}
		else
		{
			RestoreFromStateCookie();
		}
	}

	internal static uint InitHammerMainWindow( Native.CQHammerMainWnd ptr )
	{
		var win = new HammerMainWindow( ptr );
		return InteropSystem.GetAddress( win, true );
	}
}
