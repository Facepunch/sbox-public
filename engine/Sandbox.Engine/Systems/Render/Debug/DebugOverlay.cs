using Sandbox.Rendering;

namespace Sandbox;

internal static partial class DebugOverlay
{
	static CommandList _overlay = new( "Engine Overlay" );
	private const float OverlaySpacing = 16f;

	public static CommandList CommandList => _overlay;

	public static void Reset()
	{
		_overlay.Reset();
	}

	public static void Render()
	{
		_overlay.ExecuteOnRenderThread();
	}

	/// <summary>
	/// Draws what's left of the engine's own screen overlays. The performance stats that used to stack up here, one
	/// <c>overlay_*</c> convar each, live in the performance hub now (<see cref="Diagnostics.PerfHub"/>); its tabs call
	/// the drawers in this class directly.
	/// </summary>
	public static void Draw()
	{
		using var painter = Painter.Begin( CommandList );
		Vector2 pos = new Vector2( 64, 64 );

		// Show current render debug mode on screen when not default
		if ( ToolsVisualization.mat_toolsvis != SceneCameraDebugMode.Normal )
		{
			DebugOverlay.ToolsVisualization.Draw( painter, ref pos );
			pos.y += OverlaySpacing;
		}

		// Collectors that only run while a hub tab shows them
		// Allocation tracking counts GC pauses frame by frame, so it samples every game frame rather than the hub's
		if ( Diagnostics.PerfHub.IsCollecting( "memory" ) ) DebugOverlay.Allocations.Sample();
		else DebugOverlay.Allocations.Disabled();
		if ( !Diagnostics.PerfHub.IsShowing( "gpu" ) ) DebugOverlay.GpuTimeline.Clear();

		if ( ShadowMapper.DebugEnabled )
			ShadowMapper.Draw( ref pos, painter );
	}
}
