using Editor;
using Sandbox.UI;

namespace Sandbox;

public partial class StartupWindow : Window
{
	private Vector2 WindowSize => new Vector2( 600, 600 );

	private Layout Body { get; set; }

	private Toggle CloseOnLaunch { get; set; }
	
	private string _gameFolder;

	public StartupWindow()
	{
		_gameFolder = Environment.CurrentDirectory;
		Sandbox.Application.TryLoadVersionInfo( _gameFolder );
		
		Canvas = new Widget( this );

		Size = WindowSize;
		MaximumSize = WindowSize;
		MinimumSize = WindowSize;
		HasMaximizeButton = false;
		Visible = false;

		WindowTitle = "Welcome to the s&box editor";

		SetWindowIcon( Pixmap.FromFile( "logo_rounded.png" ) );

		CreateUI();

		StatusBar.Destroy();
	}

	public override void Show()
	{
		base.Show();

		RestoreGeometry( LauncherPreferences.Cookie.Get( "startscreen.geometry", "" ) );
	}

	protected override bool OnClose()
	{
		EditorCookie = null;

		LauncherPreferences.Cookie.Set( "startscreen.geometry", SaveGeometry() );

		return base.OnClose();
	}

	private void CreateUI()
	{
		Canvas.Layout = Layout.Row();

		//
		// Sidebar
		//
		{
			var sidebar = Canvas.Layout.Add( new SidebarWidget( Canvas ), 1 );

			{
				var heading = sidebar.Add( new Widget( Canvas ) { FixedHeight = 32 } );
				heading.Layout = Layout.Row();

				var headingRow = heading.Layout;
				headingRow.Add( new LogoWidget( Canvas ) );
				headingRow.AddSpacingCell( -95 ); // This is so dumb.
				headingRow.Add( new SidebarButton( Sandbox.Application.Version, "content_copy", OnVersionButtonPressed ) );
			}

			sidebar.AddSpacer();

			//
			// Links
			//
			{
				sidebar.Add( new SidebarButton( "Documentation", "school", "https://sbox.game/dev/doc/" ) );
				sidebar.Add( new SidebarButton( $"Open {Global.BackendTitle}", "celebration", Global.BackendUrl ) );
				sidebar.Add( new SidebarButton( "API Reference", "code", $"{Global.BackendUrl}/api" ) );
			}

			sidebar.AddSpacer();

			//
			// Development
			//
			{
				sidebar.Add( new SidebarButton( "Engine Folder", "folder", _gameFolder ) { IsExternal = false } );
				sidebar.Add( new SidebarButton( "Logs", "density_small", $"{_gameFolder}/logs" ) { IsExternal = false } );
			}

			sidebar.AddStretchCell();

			sidebar.AddSeparator();
			
			CloseOnLaunch = sidebar.Add( new Toggle( "Close On Launch" ) );
			CloseOnLaunch.Value = LauncherPreferences.CloseOnLaunch;
			CloseOnLaunch.ValueChanged += ( v ) =>
			{
				LauncherPreferences.CloseOnLaunch = v;
			};
		}

		//
		// Body
		//
		{
			Body = Canvas.Layout.AddColumn( 3 );
			Body.Add( new HomeWidget( Canvas ), 1 );
		}
	}

	private void OnVersionButtonPressed()
	{
		// Copy version info to the clipboard
		//TODO: Possibly implement a Popup confirming that it was copied...
		//Clipboard.SetText( _version );
		EditorUtility.Clipboard.Copy( Sandbox.Application.Version );
	}

	public void OnSuccessfulLaunch()
	{
		if ( !CloseOnLaunch.Value ) return;

		Destroy();
	}
}
