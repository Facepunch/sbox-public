using System.Diagnostics;

namespace Sandbox.UI;

public partial class RootPanel
{
	readonly PanelRecording _recording = new();

	/// <summary>Frames this root kept its last recording instead of recording again.</summary>
	internal int RecordingReplays => _recording.Replays;

	/// <summary>Subtrees replayed inside this root's recordings.</summary>
	internal int SubtreeReplays => _recording.SubtreeReplays;

	/// <summary>Frames a subtree kept in a list of its own was inserted without recording.</summary>
	internal int RetainedInserts => _recording.RetainedInserts;

	/// <summary>Recordings of subtrees kept in lists of their own.</summary>
	internal int RetainedRecordings => _recording.RetainedRecordings;

	/// <summary>
	/// Called by the UI frame renderer to rebuild this root's command list on the main thread.
	/// Creates the Painter destination, records the panel tree, flushes its batches and captures frame statistics.
	/// While nothing the last recording drew from has changed, keeps that recording for the render thread to replay.
	/// </summary>
	internal void BuildCommandList( float opacity = 1.0f )
	{
		ThreadSafe.AssertIsMainThread();

		var world = this is WorldPanel worldPanel ? ScenePanelObject.BuildPanelToWorldMatrix( worldPanel.Transform ) : (Matrix?)null;
		var key = new PanelRecording.RootKey( PanelBounds, ScaleToScreen, opacity, world, RenderedManually,
			TextBlock.ui_rendertext, PainterBatcher.DebugVisualizeBatches );

		lock ( PanelCommandList.SyncRoot )
		{
			if ( PanelCommandList.Enabled && _recording.CanReplay( this, key, PanelCommandList.FindResource<Painter.Context>()?.Batcher ) )
				return;

			// Changes made while recording (a callback restyling a panel) stamp the clock after this, so they record again
			var recordedAt = Panel.PaintClock++;
			var started = Stopwatch.GetTimestamp();
			PanelCommandList.Reset();
			using var recording = _recording.Begin( recordedAt );
			BeginRenderStatistics( PanelCommandList );
			using var painter = Painter.Begin( PanelCommandList, PanelBounds );
			painter.SetViewport( PanelBounds, world );
			using var destination = painter.WithDestination( PanelBounds, ScaleToScreen, opacity, BlendMode.Normal, Matrix.Identity );
			var counters = new FrameStats();
			Render( destination.Painter, ref counters );
			painter.Flush();
			counters.DrawCalls = painter.DrawCount;
			counters.InstanceCount = painter.InstanceCount;
			counters.FrameGrabs = painter.FrameGrabCount;
			EndRenderStatistics( PanelCommandList, counters, Stopwatch.GetElapsedTime( started ) );
			_recording.Recorded( recordedAt, key );
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
