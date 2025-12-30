using NativeEngine;
using Sandbox.Internal;
using System;

namespace Editor;


internal static class ManagedTools
{
	/// <summary>
	/// This gets set when the assemblies are changed, which means we may have new tools and menu options.
	/// This ultimately triggers a tools.refresh on the next frame.
	/// </summary>
	public static bool AssembliesDirty { get; internal set; }

	/// <summary>
	/// On startup we start logging in. We can continue to log in during startup, in the background.
	/// But we really want to have been logged in by the time we start accessing the API properly.
	/// </summary>
	internal static Task AccountLoginTask;

	public static void InitStart()
	{
		Log.Info( $"Editor Startup version {Sandbox.Application.Version}" );
		//
		// Init steam and log into the api
		//
		{
			Steamworks.SteamClient.Init( (int)Sandbox.Application.AppId );
			AccountLoginTask = AccountInformation.Update();
		}

		InitFilesystem();

		Logging.SetRule( "*", EditorCookie.Get<LogLevel>( "DefaultLoggingLevel", LogLevel.Info ) );

		ConVarSystem.AddAssembly( Global.Assembly, "editor" );

		BindSystem = new Sandbox.Bind.BindSystem( "Tools" );
		BindSystem.ThrottleUpdates = true;
		BindSystem.CatchExceptions = true;

		InitQt();

		// Makes sure we have a Filesystem.Content early, even if projects aren't added yet
		FileSystem.RebuildContentPath();
	}

	public static void InitFilesystem()
	{
		FileSystem.Root.CreateDirectory( "/.source2/http" );
		FileSystem.WebCache = new LocalFileSystem( FileSystem.Root.GetFullPath( "/.source2/http" ) );

		FileSystem.Temporary = new LocalFileSystem( FileSystem.Root.GetFullPath( "/.source2" ) );
	}

	public static void InitQt()
	{
		var root = Environment.CurrentDirectory;

		QDir.addSearchPath( "toolimages", $"{root}/core/tools/images" );
		QDir.addSearchPath( "toolimages", $"{root}/addons/tools/assets" );

		foreach ( var file in System.IO.Directory.EnumerateFiles( $"{root}/addons/base/assets/fonts/", "*.ttf" ) )
		{
			QFontDatabase.addApplicationFont( file );
		}
	}

	private static FileWatch stylesWatcher;

	public static void InitFinish()
	{
		stylesWatcher = FileSystem.Root.Watch( "/addons/tools/assets/styles/*" );
		stylesWatcher.OnChangedFile += ( x ) => Application.ReloadStyles();

		//
		// Wait for tools addons to finish compiling
		//
		Log.Info( "Compiling Tools" );
		RunFrame();

		while ( !AccountLoginTask.IsCompleted )
		{
			Sandbox.Engine.IToolsDll.Current?.Spin();
			System.Threading.Thread.Sleep( 16 );
		}

		// If the avatar was found on the backend, replace the cookie one
		if ( !string.IsNullOrWhiteSpace( AccountInformation.AvatarJson ) )
		{
			Avatar.AvatarJson = AccountInformation.AvatarJson;
		}
	}

	public static void RunFrame()
	{
		Application.StartFrame();

		if ( AssembliesDirty )
		{
			AssembliesDirty = false;
			EditorEvent.Run( "refresh" );
		}

		EngineLoop.RunAsyncTasks();

		using ( Performance.Scope( "Tool Binds" ) )
		{
			BindSystem.Tick();
		}

		EditorEvent.Run( "tool.frame" );
		AssetThumbnail.Frame();
	}

	public static void Shutdown()
	{
		stylesWatcher?.Dispose();

		AssetSystem.Shutdown();
	}

	internal static void NativeHook( string name, IntPtr arg0, IntPtr arg1, IntPtr arg2 )
	{
		var method = typeof( ManagedTools ).GetMethod( name, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic );
		if ( method == null )
		{
			Log.Warning( $"Unknown tool hook: {name}" );
			return;
		}

		var parameters = method.GetParameters();

		if ( parameters.Length == 0 )
		{
			method?.Invoke( null, null );
			return;
		}

		if ( parameters.Length == 1 )
		{
			method?.Invoke( null, new object[] { (Native.QWidget)arg0 } );
			return;
		}
	}

	internal static void GlobalMousePressed( int button )
	{
		EditorEvent.Run( "qt.mousepressed" );
	}

	internal static void GlobalMouseReleased( int button )
	{
		if ( button == 1 )
		{
			Widget.CurrentlyPressedWidget = null;
		}
	}

	internal static void GlobalMouseWheel( int x, int y )
	{
		Application.accumulatedCursorDelta += new Vector2( x / 120, y / 120 );
	}

	internal static bool GlobalKeyPressed( bool press, QKeyEvent keyEvent )
	{
		var ev = new KeyEvent( keyEvent );
		KeyCode key = ev.Key;

		if ( key == KeyCode.AsciiTilde || key == KeyCode.QuoteLeft )
		{
			EditorWindow.Focus();
			return true;
		}
		else
		{
			if ( press )
			{
				var modifiers = ev.KeyboardModifiers;
				var baseKey = ev.Name.ToUpperInvariant();
				var modifiedKey = baseKey;

				if ( modifiers != KeyboardModifiers.None )
				{
					EditorShortcuts.Press( baseKey );
					if ( modifiers.HasFlag( KeyboardModifiers.Shift ) && ev.Key != KeyCode.Shift ) modifiedKey = "SHIFT+" + modifiedKey;
					if ( modifiers.HasFlag( KeyboardModifiers.Alt ) && ev.Key != KeyCode.Alt ) modifiedKey = "ALT+" + modifiedKey;
					if ( modifiers.HasFlag( KeyboardModifiers.Ctrl ) && ev.Key != KeyCode.Control ) modifiedKey = "CTRL+" + modifiedKey;
				}

				// If we're "in game" these will be passed by the InputRouter whilst we're not focused
				if ( EditorShortcuts._timeSinceGlobalShortcut <= 0.05f && ev.Key >= KeyCode.F1 && ev.Key <= KeyCode.F12 )
					return false;

				// Try with modifier first.
				if ( modifiers != KeyboardModifiers.None && EditorShortcuts.Invoke( modifiedKey ) )
					return true;

				// If no shortcut was invoked with modifier, try without.
				if ( EditorShortcuts.Invoke( baseKey ) )
					return true;
			}
			else
			{
				EditorShortcuts.Release( ev.Name.ToUpperInvariant() );
			}
		}

		return false;
	}

	internal static bool GlobalShortcutPressed()
	{
		return !EditorShortcuts.AllowShortcuts;
	}

	internal static void OnToolCommand( string v )
	{
		var parts = v.SplitQuotesStrings();
		if ( parts.Length == 0 )
			return;

		EditorEvent.Run( $"command {parts[0]}" );
	}

	internal static void StartSplashScreen()
	{
		new EditorSplashScreen();

		g_pToolFramework2.Spin();
	}
	/// <summary>
	/// Attempted to be called every 16ms, even when Qt is blocking
	/// </summary>
	public static void OnQtHeartbeat()
	{
		BlockingLoopPumper.Pump();
	}
}
