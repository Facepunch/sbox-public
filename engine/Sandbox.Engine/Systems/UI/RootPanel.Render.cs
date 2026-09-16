using System.Diagnostics;

namespace Sandbox.UI;

public partial class RootPanel
{
	/// <summary>
	/// Called by the UI frame renderer to rebuild this root's command list on the main thread.
	/// Creates the Painter destination, records the panel tree, flushes its batches and captures frame statistics.
	/// </summary>
	internal void BuildCommandList( float opacity = 1.0f )
	{
		ThreadSafe.AssertIsMainThread();

		lock ( PanelCommandList.SyncRoot )
		{
			var started = Stopwatch.GetTimestamp();
			PanelCommandList.Reset();
			BeginRenderStatistics( PanelCommandList );
			using var painter = Painter.Begin( PanelCommandList, PanelBounds );
			painter.SetViewport( PanelBounds, this is WorldPanel world ? ScenePanelObject.BuildPanelToWorldMatrix( world.Transform ) : null );
			using var destination = painter.WithDestination( PanelBounds, ScaleToScreen, opacity, BlendMode.Normal, Matrix.Identity );
			var counters = new FrameStats();
			Render( destination.Painter, ref counters );
			painter.Flush();
			counters.DrawCalls = painter.DrawCount;
			counters.InstanceCount = painter.InstanceCount;
			counters.FrameGrabs = painter.FrameGrabCount;
			EndRenderStatistics( PanelCommandList, counters, Stopwatch.GetElapsedTime( started ) );
		}
	}

	/// <summary>
	/// Called by BuildCommandList to record the regular panel tree, followed by fixed-position overlays.
	/// Each overlay receives a root-relative destination and draws its outset shadows before its body.
	/// </summary>
	internal new void Render( Painter painter, ref FrameStats stats )
	{
		base.Render( painter, ref stats );
		foreach ( var overlay in FixedOverlays )
		{
			using var scope = painter.WithDestination( PanelBounds, ScaleToScreen,
				painter.InheritedOpacity * (overlay.Parent?.Opacity ?? 1), BlendMode.Normal, Matrix.Identity );
			overlay.RenderShadow( scope.Painter );
			overlay.Render( scope.Painter, ref stats );
		}
	}
}
