using Sandbox.Rendering;

namespace Sandbox;

internal static partial class DebugOverlay
{
	static CommandList _overlay = new( "Engine Overlay" );

	public static CommandList CommandList => _overlay;

	public static HudPainter Hud => new HudPainter( CommandList );

	public static void Reset()
	{
		_overlay.Reset();
	}

	public static void Render()
	{
		_overlay.ExecuteOnRenderThread();
	}

	[ConVar( "overlay_profile", Help = "Draws an overlay showing timings from the main profile categories" )]
	internal static int overlay_profile { get; set; } = 0;

	[ConVar( "overlay_alloc", Help = "Draws an overlay showing allocations and garbage collection" )]
	internal static int overlay_alloc { get; set; } = 0;

	[ConVar( "overlay_frame", Help = "Draws an overlay render frame stats" )]
	internal static int overlay_frame { get; set; } = 0;

	[ConVar( "overlay_network_graph", Help = "Draws an overlay showing a network usage summary" )]
	internal static int overlay_network_graph { get; set; } = 0;

	[ConVar( "overlay_network_calls", Help = "Draws an overlay showing most received network calls" )]
	internal static int overlay_network_calls { get; set; } = 0;

	[ConVar( "overlay_pp", Help = "Draws an overlay showing current post process stack" )]
	internal static int overlay_pp { get; set; } = 0;

	[ConVar( "overlay_gpu", Help = "Draws an overlay showing GPU timing for render passes" )]
	internal static int overlay_gpu { get; set; } = 0;

	public static void Draw()
	{
		Vector2 pos = new Vector2( 100, 130 );
		var activeScene = Application.GetActiveScene();

		if ( overlay_network_calls == 1 )
		{
			DebugOverlay.NetworkCalls.Draw( ref pos );
			pos.y += 20;
		}

		if ( overlay_network_graph == 1 )
		{
			DebugOverlay.NetworkGraph.Draw( ref pos );
			pos.y += 20;
		}

		if ( overlay_profile == 1 )
		{
			DebugOverlay.Profiler.Draw( ref pos );
			pos.y += 20;
		}

		if ( overlay_frame == 1 )
		{
			DebugOverlay.Frame.Draw( ref pos );
			pos.y += 20;
		}

		if ( overlay_pp == 1 )
		{
			activeScene?.Camera?.PrintPostProcessDebugOverlay( ref pos, Hud );
			pos.y += 20;
		}

		if ( overlay_alloc == 1 )
		{
			DebugOverlay.Allocations.Draw( ref pos );
			pos.y += 20;
		}
		else
		{
			DebugOverlay.Allocations.Disabled();
		}

		// GPU Profiler
		Diagnostics.GpuProfilerStats.Enabled = overlay_gpu == 1;
		Diagnostics.GpuProfilerStats.Update();

		if ( overlay_gpu == 1 )
		{
			DebugOverlay.GpuProfiler.Draw( ref pos );
			pos.y += 20;
		}
	}
}
