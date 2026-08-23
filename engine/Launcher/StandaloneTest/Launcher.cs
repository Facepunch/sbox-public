global using Sandbox;
global using Sandbox.Utility;
global using System;
global using System.Collections.Generic;
global using System.Linq;
global using System.Threading.Tasks;
global using static Sandbox.Internal.GlobalToolsNamespace;
using System.Diagnostics;
using System.Threading;

namespace Sandbox;

public static class Launcher
{
	public static int Main()
	{
		if ( FindExisting() )
			return 0;

		var appSystem = new LauncherAppSystem();
		appSystem.Run();

		return 0;
	}

	/// <summary>
	/// Find an existing process with the same name. Bring it to front.
	/// </summary>
	static bool FindExisting()
	{
		var currentId = Process.GetCurrentProcess().Id;
		var currentName = Process.GetCurrentProcess().ProcessName;

		var existing = System.Diagnostics.Process.GetProcesses()
								.Where( x => x.Id != currentId )
								.Where( x => x.ProcessName == currentName )
								.ToList();
		if ( existing.Count > 0 )
		{
			// Bringing it to front is Windows only - MainWindowHandle is always zero elsewhere,
			// and User32 isn't there to call. We still want to bail out either way, so that we
			// don't end up running two launchers.
			if ( OperatingSystem.IsWindows() )
			{
				foreach ( var p in existing )
				{
					IntPtr handle = p.MainWindowHandle;
					if ( IsIconic( handle ) )
					{
						ShowWindow( handle, SW_RESTORE );
					}

					SetForegroundWindow( handle );
				}
			}

			return true;
		}

		return false;
	}

	const int SW_RESTORE = 9;

	[System.Runtime.InteropServices.DllImport( "User32.dll" )]
	private static extern bool SetForegroundWindow( IntPtr handle );

	[System.Runtime.InteropServices.DllImport( "User32.dll" )]
	private static extern bool ShowWindow( IntPtr handle, int nCmdShow );

	[System.Runtime.InteropServices.DllImport( "User32.dll" )]
	private static extern bool IsIconic( IntPtr handle );
}

public class LauncherAppSystem : QtAppSystem
{
	public override void Init()
	{
		base.Init();

		// We don't want to save editor cookies.
		EditorCookie.StopTimer();

		LauncherPreferences.Load();

		var window = new StartupWindow();
		ProcessEvents();
		window.WindowOpacity = 0.0f;
		window.Show();

		// this looks like I'm being a fancy arsehole, but this is all because
		// the window shows up white for some reason when first opened, and this
		// disguises it.

		var fromPos = window.Position + new Vector2( 0, 30 );
		var toPos = window.Position;

		for ( int i = 0; i <= 20; i++ )
		{
			float fEase = Easing.EaseOut( i / 20.0f );

			Thread.Sleep( 1 );
			ProcessEvents();

			if ( !window.IsValid() )
				return;

			window.WindowOpacity = fEase;
			window.Position = Vector2.Lerp( fromPos, toPos, fEase );
		}

		window.WindowOpacity = 1;

	}

	protected override void OnShutdown()
	{
		// We don't want to save editor cookies.
		EditorCookie = null;

		LauncherPreferences.Save();
	}
}
