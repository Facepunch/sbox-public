global using Sandbox;
global using Sandbox.Utility;
global using System;
global using System.Collections.Generic;
global using System.Linq;
global using System.Threading.Tasks;
global using static Sandbox.Internal.GlobalToolsNamespace;
global using static Sandbox.Internal.GlobalSystemNamespace;
using System.Diagnostics;
using System.Threading;

namespace Sandbox;

public static class Launcher
{
	public static int Main()
	{
		if ( FindExisting() )
			return 0;

		var appSystem = new PanelLauncherAppSystem();
		appSystem.Run();

		return 0;
	}

	/// <summary>
	/// Find an existing process with the same name. Bring it to front.
	/// </summary>
	static bool FindExisting()
	{
		// Enumerating every process on the machine is the slowest thing the launcher does
		// before it starts booting - 50ms on a Mac. Only Windows needs the process, to bring
		// its window to the front; everywhere else a named mutex answers the same question
		// for nothing. The mutex is held for the life of the process. A launcher that crashed
		// leaves it behind, so whether it already existed says nothing - whether it can be
		// taken does, and taking one that was abandoned throws, which is the same answer as
		// taking it clean
		if ( !OperatingSystem.IsWindows() )
		{
			instanceLock = new Mutex( false, "sbox-launcher-instance" );

			try
			{
				return !instanceLock.WaitOne( 0 );
			}
			catch ( AbandonedMutexException )
			{
				return false;
			}
		}

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

	static Mutex instanceLock;

	const int SW_RESTORE = 9;

	[System.Runtime.InteropServices.DllImport( "User32.dll" )]
	private static extern bool SetForegroundWindow( IntPtr handle );

	[System.Runtime.InteropServices.DllImport( "User32.dll" )]
	private static extern bool ShowWindow( IntPtr handle, int nCmdShow );

	[System.Runtime.InteropServices.DllImport( "User32.dll" )]
	private static extern bool IsIconic( IntPtr handle );
}

/// <summary>
/// The launcher as a panel UI app - the least engine that can draw panels, one window, no Qt.
/// </summary>
public class PanelLauncherAppSystem : PanelAppSystem
{
	Editor.PanelWindow window;

	protected override void OnInitialized()
	{
		LauncherPreferences.Load();

		window = new Editor.PanelWindow( "Welcome to the s&box editor", new Vector2( 1100, 660 ), new Vector2( -1, -1 ), borderless: true, vsync: true );
		window.MinSize = new Vector2( 880, 540 );
		window.CanMaximize = false;
		window.Root.AddChild( new Sandbox.LauncherUI.LauncherWindow( window ) );
	}

	public override void Shutdown()
	{
		LauncherPreferences.Save();

		base.Shutdown();
	}
}
