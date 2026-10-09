using Sandbox.Engine;
using Sandbox.UI;
using System;
using System.Diagnostics;
using System.IO;
using PanelLabel = Sandbox.UI.Label;

namespace Editor;

internal sealed class EditorSplashScreen : PanelWindow
{
	internal static EditorSplashScreen Singleton;

	const float SplashWidth = 700;
	const float InfoAreaHeight = 88;
	readonly Texture BackgroundImage;
	readonly Texture LogoImage;
	readonly Panel ProgressBar;
	readonly PanelLabel TitleLabel;
	readonly PanelLabel MessageLabel;
	long LastFrame;
	bool IsPumping;

	public EditorSplashScreen() : base( "Opening s&box Editor", new Vector2( SplashWidth, 438 ), EditorCookie.Get<Vector2?>( "splash.position", null ), borderless: true )
	{
		try
		{
			Resizable = false;
			CanMaximize = false;
			CanClose = false;
			ResizeBorder = 0;

			using var image = LoadCustomSplashImage();
			using var backdrop = image is null ? LoadImage( "/core/materials/startup_backdrop_color.png" ) : null;
			BackgroundImage = (image ?? backdrop).ToTexture();
			var artwork = image ?? backdrop;
			var imageHeight = SplashWidth * artwork.Height / artwork.Width;
			Size = new Vector2( SplashWidth, imageHeight + InfoAreaHeight );
			MoveToCenter();

			using var icon = LoadImage( "/core/tools/images/logo_rounded.png" );
			SetIcon( icon );

			Root.AddClass( "window-drag" );
			Root.Style.Position = PositionMode.Relative;
			Root.Style.FlexDirection = FlexDirection.Column;
			Root.Style.FontFamily = "Roboto";
			Root.Style.FontColor = Color.White;
			Root.Style.BackgroundColor = Color.Parse( "#191f2b" );

			var splash = Root.Add.Panel();
			splash.Style.Position = PositionMode.Relative;
			splash.Style.Height = imageHeight;
			splash.Style.FlexShrink = 0;
			splash.Style.BackgroundImage = BackgroundImage;
			// Keep the whole custom image visible, with all loading UI below it.
			splash.Style.Set( "background-size: contain; background-position: center; background-repeat: no-repeat;" );

			// The default keeps a little editor branding over the game's softer backdrop.
			// Custom artwork stands on its own.
			if ( image is null )
			{
				LogoImage = icon.ToTexture();
				var brand = splash.Add.Panel();
				brand.Style.Set( "position: absolute; width: 72px; height: 72px;" );
				brand.Style.Left = (SplashWidth - 72) / 2;
				brand.Style.Top = (imageHeight - 72) / 2;
				brand.Style.BackgroundImage = LogoImage;
				brand.Style.Set( "background-size: contain; background-position: center; background-repeat: no-repeat;" );
			}

			var info = Root.Add.Panel();
			info.Style.Position = PositionMode.Relative;
			info.Style.Height = InfoAreaHeight;
			info.Style.FlexShrink = 0;

			var version = info.AddChild<PanelLabel>();
			version.Text = $"Version {Sandbox.Application.Version ?? "dev"}";
			version.Selectable = false;
			version.Style.Set( "position: absolute; right: 20px; top: 20px; font-size: 11px;" );
			version.Style.FontColor = Color.White.WithAlpha( 0.35f );

			TitleLabel = info.AddChild<PanelLabel>();
			TitleLabel.Selectable = false;
			TitleLabel.Style.Set( "position: absolute; left: 20px; right: 170px; top: 16px; font-size: 15px; font-weight: 600; white-space: nowrap; text-overflow: ellipsis; overflow: hidden;" );

			var track = info.Add.Panel();
			track.Style.Set( "position: absolute; left: 20px; right: 20px; bottom: 16px; height: 3px;" );
			track.Style.BackgroundColor = Color.White.WithAlpha( 0.08f );
			ProgressBar = track.Add.Panel();
			ProgressBar.Style.Width = 0;
			ProgressBar.Style.Height = Length.Percent( 100 );
			ProgressBar.Style.BackgroundColor = Color.Parse( "#f01820" );

			MessageLabel = info.AddChild<PanelLabel>();
			MessageLabel.Text = "Starting editor…";
			MessageLabel.Selectable = false;
			MessageLabel.Style.Set( "position: absolute; left: 20px; right: 20px; top: 42px; font-size: 11px; white-space: nowrap; text-overflow: ellipsis; overflow: hidden;" );
			MessageLabel.Style.FontColor = Color.White.WithAlpha( 0.55f );
		}
		catch
		{
			Dispose();
			throw;
		}
	}

	/// <summary>
	/// Load the project's optional splash_screen.png. The project isn't mounted yet.
	/// </summary>
	static Bitmap LoadCustomSplashImage()
	{
		var projectPath = Sandbox.Utility.CommandLine.GetSwitch( "-project", "" ).TrimQuoted();
		if ( !string.IsNullOrEmpty( projectPath ) )
		{
			var projectDir = Path.GetDirectoryName( Path.GetFullPath( projectPath ) );
			var customSplash = Path.Combine( projectDir, "splash_screen.png" );
			if ( File.Exists( customSplash ) )
			{
				var bitmap = Bitmap.CreateFromBytes( File.ReadAllBytes( customSplash ) );
				if ( bitmap is not null ) return bitmap;
			}
		}

		return null;
	}

	static Bitmap LoadImage( string path ) => Bitmap.CreateFromBytes( FileSystem.Root.ReadAllBytes( path ).ToArray() );

	private protected override void OnClosing()
	{
		EditorCookie.Set( "splash.position", Position );
		if ( Singleton == this )
		{
			g_pToolFramework2.SetStallMonitorPanelWindow( IntPtr.Zero );
			Singleton = null;
		}
		BackgroundImage?.Dispose();
		LogoImage?.Dispose();
		base.OnClosing();
	}

	public static void StartupFinish()
	{
		Singleton?.Dispose();
	}

	/// <summary>
	/// Updates the progress bar.
	/// </summary>
	public static void SetProgress( float progress )
	{
		if ( Singleton is not { IsOpen: true } splash ) return;
		splash.ProgressBar.Style.Width = Length.Percent( progress.Clamp( 0f, 1f ) * 100 );
		Pump();
	}

	/// <summary>
	/// Set the current displayed message.
	/// </summary>
	public static void SetMessage( string message )
	{
		if ( Singleton is not { IsOpen: true } splash ) return;
		splash.MessageLabel.Text = message ?? "Starting editor…";
		Application.Spin();
		NativeEngine.EngineGlobal.ToolsStallMonitor_IndicateActivity();
	}

	internal static void RestoreStallMonitor()
	{
		if ( Singleton is { IsOpen: true } splash )
			g_pToolFramework2.SetStallMonitorPanelWindow( splash.Handle );
	}

	/// <summary>
	/// Startup blocks before the engine's first frame. Pump SDL and present the splash ourselves,
	/// at most once every 16ms, including while Qt is running a nested event loop.
	/// </summary>
	internal static void Pump()
	{
		if ( Singleton is not { IsOpen: true } splash || splash.IsPumping ) return;
		if ( Stopwatch.GetElapsedTime( splash.LastFrame ).TotalMilliseconds < 16 ) return;

		splash.IsPumping = true;
		try
		{
			SdlEvents.Poll();
			if ( !splash.IsOpen ) return;
			if ( !splash.IsVisible && splash.IsShown ) return;

			var projectTitle = Project.Current?.Config?.Title;
			splash.TitleLabel.Text = string.IsNullOrEmpty( projectTitle ) ? "s&box Editor" : $"s&box Editor — {projectTitle}";
			if ( splash.Frame() ) PanelWindows.FrameEnd();
			RestoreStallMonitor();
			splash.LastFrame = Stopwatch.GetTimestamp();
		}
		finally
		{
			splash.IsPumping = false;
		}
	}
}
