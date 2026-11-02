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
	/// <c>overlay_*</c> convar each, live in the dev tools now (<see cref="Diagnostics.DevTools"/>); its tabs call
	/// the drawers in this class directly.
	/// </summary>
	public static void Draw()
	{
		using var painter = Painter.Begin( CommandList );
		Vector2 pos = new Vector2( 64, 64 );

		// First, since its image covers the view
		if ( OcclusionDebug.View != OcclusionDebugView.Off )
		{
			DebugOverlay.OcclusionCulling.Draw( painter, ref pos );
			pos.y += OverlaySpacing;
		}

		// Show current render debug mode on screen when not default
		if ( ToolsVisualization.mat_toolsvis != SceneCameraDebugMode.Normal )
		{
			DebugOverlay.ToolsVisualization.Draw( painter, ref pos );
			pos.y += OverlaySpacing;
		}

		// Collectors that only run while a dev tools tab shows them
		// Allocation tracking counts GC pauses frame by frame, so it samples every game frame rather than the dev tools'
		if ( Diagnostics.DevTools.IsCollecting( "memory" ) ) DebugOverlay.Allocations.Sample();
		else DebugOverlay.Allocations.Disabled();
		if ( !Diagnostics.DevTools.IsShowing( "gpu" ) ) DebugOverlay.GpuTimeline.Clear();

		if ( ShadowMapper.DebugEnabled )
			ShadowMapper.Draw( ref pos, painter );

		// The panel the dev tools' UI inspector has picked
		if ( Diagnostics.DevTools.IsShowing( "ui" ) ) Diagnostics.DevToolsUiTab.DrawHighlight( painter );

		// Memory pressure warnings are visible even when the dev tools is closed.
		VideoMemoryWarning.Draw( painter );
	}
}
