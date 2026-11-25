using NativeEngine;
using System.Threading;
using GpuDevice = Borealis.Gpu.Device;

namespace Sandbox;

/// <summary>
/// What the process does when its Borealis.Gpu device is lost (<see cref="GpuDevice.IsLost"/>), as native does when its
/// own device is: log what the GPU was doing, tell the player, and exit. Nothing drawn after a loss reaches the screen, and
/// every frame would fail the same way. <c>-borealisgpu-keeprunning</c> keeps the process going instead (for tools that
/// read the state afterwards).
/// </summary>
internal static class BorealisGpuDeviceLost
{
	static int handled;

	/// <summary>Whether a lost device was handled: said and, unless kept running, the process exiting.</summary>
	public static bool Handled => Volatile.Read( ref handled ) != 0;

	/// <summary>
	/// Saves what would otherwise be lost (the editor's unsaved scenes, as copies beside them) and says where each went.
	/// Set by the editor; nothing in the game.
	/// </summary>
	internal static Func<List<string>> SaveRecoveryCopies { get; set; }

	/// <summary>Handle the device's loss once, if it's lost. Called at the start of each frame and when a present fails.</summary>
	public static void Check( GpuDevice device )
	{
		if ( device is null || !device.IsLost || Interlocked.Exchange( ref handled, 1 ) != 0 ) return;

		Log.Error( $"Borealis.Gpu device lost. {device.LostReport}" );

		// Before the box: what's unsaved goes to copies (nothing in them needs the GPU)
		List<string> saved = null;
		try { saved = SaveRecoveryCopies?.Invoke(); }
		catch ( Exception e ) { Log.Error( e, "Couldn't save recovery copies" ); }

		if ( Utility.CommandLine.HasSwitch( "-borealisgpu-keeprunning" ) ) return;

		// The text native shows (CRenderThreadVulkan::GPUCrash); headless processes only log it
		EngineGlobal.Plat_MessageBox( "GPU Crash", "The graphics driver has crashed with an unspecified error.\n"
			+ "This is probably our fault but just to be sure, please try the following:\n"
			+ "- Make sure you have the latest graphics drivers installed.\n"
			+ "- Make sure Windows has all updates installed.\n"
			+ "- Disable any third party overlays, such as MSI Afterburner, Overwolf, Twitch Studio, etc.\n\n"
			+ "If the problem persists please make an issue with your Log.txt and vulkaninfo"
			+ (saved is { Count: > 0 } ? "\n\nUnsaved scenes were saved as copies beside them:\n" + string.Join( "\n", saved ) : "") );
		EngineGlobal.Plat_ExitProcess( 1 );
	}
}
