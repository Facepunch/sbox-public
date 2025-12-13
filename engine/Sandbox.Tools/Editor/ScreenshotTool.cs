using Sandbox;
using System;
using System.IO;
using System.Diagnostics;
using Editor;

namespace Editor;

[Dock( "Editor", "Screenshot", "camera_enhance" )]
public class ScreenshotTool : Widget
{
	public ScreenshotTool( Widget parent ) : base( parent )
	{
		WindowTitle = "Screenshot";
		SetWindowIcon( "camera_enhance" );
		Name = "ScreenshotTool";
		Size = new Vector2( 300, 450 );
		MinimumSize = new Vector2( 250, 300 );

		Layout = Layout.Column();
		Layout.Margin = 8;
		Layout.Spacing = 8;

		BuildUI();
	}

	private void BuildUI()
	{
		Layout.Clear( true );

		// Normal Capture
		AddHeader( "Capture" );
		{
			var btn = new Button( "Take Screenshot", "camera_enhance", this );
			btn.Clicked = () => ScreenshotService.RequestCapture();
			btn.FixedHeight = 40;
			Layout.Add( btn );
		}
		Layout.AddSpacingCell( 8 );

		// High Res Capture
		AddHeader( "High Resolution" );
		{
			var presetsRow = Layout.AddRow();
			presetsRow.Spacing = 4;

			presetsRow.Add( new Button( "4K", this ) { Clicked = () => TakeHighRes( 3840, 2160 ) } );
			presetsRow.Add( new Button( "8K", this ) { Clicked = () => TakeHighRes( 7680, 4320 ) } );
			presetsRow.Add( new Button( "Square", this ) { Clicked = () => TakeHighRes( 2048, 2048 ) } );

			var customRow = Layout.AddRow();
			customRow.Spacing = 4;

			var widthEdit = new LineEdit( "3840", this );
			var heightEdit = new LineEdit( "2160", this );
			customRow.Add( new Label( "Size:", this ) );
			customRow.Add( widthEdit );
			customRow.Add( new Label( "x", this ) );
			customRow.Add( heightEdit );



			var customBtn = new Button( "Capture Custom", "photo_camera", this );
			customBtn.Clicked = () =>
			{
				if ( int.TryParse( widthEdit.Text, out int w ) && int.TryParse( heightEdit.Text, out int h ) )
				{
					TakeHighRes( w, h );
				}
			};
			Layout.Add( customBtn );
		}
		Layout.AddSpacingCell( 8 );

		// Settings
		AddHeader( "Settings" );
		{




			var steamCheck = new Checkbox( "Add to Steam Library", this );
			steamCheck.Value = ScreenshotService.AddToSteamLibrary;
			steamCheck.StateChanged += ( s ) => ScreenshotService.AddToSteamLibrary = s == CheckState.On;
			Layout.Add( steamCheck );
		}

		Layout.AddStretchCell();

		var openBtn = new Button( "Open Screenshot Folder", "folder_open", this );
		openBtn.Clicked = () =>
		{
			var path = Path.GetFullPath( "screenshots" );
			Directory.CreateDirectory( path );
			Process.Start( new ProcessStartInfo { FileName = path, UseShellExecute = true } );
		};
		Layout.Add( openBtn );
	}

	private void AddHeader( string text )
	{
		var l = new Label( text, this );
		l.SetStyles( "font-weight: bold; padding-bottom: 4px;" );
		Layout.Add( l );
	}

	private void TakeHighRes( int width, int height )
	{
		var scene = Sandbox.Game.ActiveScene;
		if ( scene == null )
		{
			Log.Warning( "No active scene to capture!" );
			return;
		}

		ScreenshotService.TakeHighResScreenshot( scene, width, height );
	}
}
