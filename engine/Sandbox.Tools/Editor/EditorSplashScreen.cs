using System;
using System.IO;

namespace Editor
{
	internal class EditorSplashScreen : Widget
	{
		internal static EditorSplashScreen Singleton;

		Pixmap BackgroundImage;

		const float InfoAreaHeight = 64;

		public EditorSplashScreen() : base( null, true )
		{
			WindowFlags = WindowFlags.Window | WindowFlags.Customized | WindowFlags.FramelessWindowHint | WindowFlags.MSWindowsFixedSizeDialogHint;
			Singleton = this;
			DeleteOnClose = true;

			WindowTitle = "Opening s&box Editor";
			SetWindowIcon( Pixmap.FromFile( "window_icon.png" ) );
			BackgroundImage = LoadSplashImage();

			// load any saved geometry
			string geometryCookie = EditorCookie.GetString( "splash.geometry", null );
			RestoreGeometry( geometryCookie );

			var aspect = (float)BackgroundImage.Height / BackgroundImage.Width;
			Size = new( 700, (700 * aspect).FloorToInt() + InfoAreaHeight );

			if ( geometryCookie is null )
			{
				// fallback to screen centre if there's no saved geo
				Position = ScreenGeometry.Contain( Size ).Position;
			}

			Show();

			//
			// Resample background image if dpi scale is gonna make us draw it bigger
			//
			if ( DpiScale != 1.0f )
			{
				BackgroundImage = BackgroundImage.Resize( BackgroundImage.Size * DpiScale );
			}

			WidgetUtil.MakeWindowDraggable( _widget );

			ConstrainToScreen();

			g_pToolFramework2.SetStallMonitorMainThreadWindow( _widget );
		}

		/// <summary>
		/// Try to load project's splash_screen.png from its root,
		/// Falls back to the default built-in screen
		/// </summary>
		static Pixmap LoadSplashImage()
		{
			var projectPath = Sandbox.Utility.CommandLine.GetSwitch( "-project", "" ).TrimQuoted();

			if ( !string.IsNullOrEmpty( projectPath ) )
			{
				var projectDir = Path.GetDirectoryName( Path.GetFullPath( projectPath ) );
				var customSplash = Path.Combine( projectDir, "splash_screen.png" );

				if ( File.Exists( customSplash ) )
				{
					var pixmap = Pixmap.FromFile( customSplash );
					if ( pixmap is not null )
						return pixmap;
				}
			}

			return Pixmap.FromFile( "splash_screen.png" );
		}

		public override void OnDestroyed()
		{
			base.OnDestroyed();
			Singleton = null;
		}

		public static void StartupFinish()
		{
			if ( Singleton.IsValid() )
			{
				EditorCookie.Set( "splash.geometry", Singleton.SaveGeometry() );
				Singleton.Destroy();
			}

			Singleton = null;
		}

		string LatestMessage;
		float Progress;

		/// <summary>
		/// Updates the progress bar
		/// </summary>
		public static void SetProgress( float progress )
		{
			if ( !Singleton.IsValid() ) return;

			Singleton.Progress = progress.Clamp( 0f, 1f );
			Singleton.Update();
		}

		/// <summary>
		/// Set the current displayed message
		/// </summary>
		public static void SetMessage( string message )
		{
			if ( !Singleton.IsValid() ) return;

			Singleton.LatestMessage = message;
			Singleton.Update();

			g_pToolFramework2.Spin();
			NativeEngine.EngineGlobal.ToolsStallMonitor_IndicateActivity();
		}

		protected override bool OnClose()
		{
			return false;
		}

		protected override void OnPaint()
		{
			// Draw the splash image in the top portion only
			var imageRect = LocalRect;
			imageRect.Bottom -= InfoAreaHeight;
			Paint.Draw( imageRect, BackgroundImage );

			//
			// Progress bar sits at the bottom of the splash image
			//
			var barRect = imageRect;
			barRect.Top = barRect.Bottom - 4;

			Paint.ClearPen();

			if ( Progress > 0f )
			{
				var fillRect = barRect.Shrink( 1 );
				fillRect.Width *= Progress;

				Paint.SetBrush( Color.White );
				Paint.DrawRect( fillRect, 3.0f );
			}

			//
			// Info area below the image
			//
			var infoRect = LocalRect;
			infoRect.Top = imageRect.Bottom;

			Paint.ClearPen();
			Paint.SetBrush( Color.Black );
			Paint.DrawRect( infoRect );

			var textArea = infoRect.Shrink( 12, 8 );

			var projectTitle = Project.Current?.Config?.Title;
			var title = string.IsNullOrEmpty( projectTitle )
				? "s&box Editor"
				: $"s&box Editor - {projectTitle}";

			Paint.SetPen( Color.White );
			Paint.SetDefaultFont( 10, 600 );
			Paint.DrawText( textArea, title, TextFlag.LeftTop );

			// Version below the title
			Paint.SetPen( Color.White.WithAlpha( 0.4f ) );
			Paint.SetDefaultFont( 8, 400 );
			Paint.DrawText( textArea.Shrink( 0, 18, 0, 0 ), $"Version {Sandbox.Application.Version ?? "dev"}", TextFlag.LeftTop );

			// Progress text at the bottom
			Paint.SetPen( Color.White.WithAlpha( 0.6f ) );
			Paint.SetDefaultFont( 8, 400 );
			Paint.DrawText( textArea, LatestMessage ?? "Bootstrapping..", TextFlag.LeftBottom );
		}

	}
}
