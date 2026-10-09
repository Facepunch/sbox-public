using Sandbox.Rendering;
using System.Diagnostics;

namespace Sandbox.UI;

public partial class RootPanel
{
	/// <summary>
	/// Statistics for this root's last completed render, including its most recent command preparation.
	/// Returns an immutable snapshot. All values are zero until the root's command list has executed.
	/// </summary>
	public RenderStatistics Stats
	{
		get
		{
			lock ( _statisticsLock )
			{
				return _renderStatistics;
			}
		}
	}

	/// <summary>
	/// Rendering work for one root and its descendants, including fixed overlays.
	/// Timings measure CPU work, not GPU execution or window presentation.
	/// Preparation values describe the commands being rendered and remain unchanged when those commands are replayed.
	/// </summary>
	public readonly record struct RenderStatistics
	{
		/// <summary>
		/// Number of completed executions of this root's command list over its lifetime.
		/// Includes manual, world-panel and offscreen rendering. Rebuilding commands does not increment this.
		/// </summary>
		public long RenderCount { get; internal init; }

		/// <summary>
		/// Engine frame number of the last completed render. Use RenderCount to distinguish an unrendered root.
		/// </summary>
		public ulong FrameNumber { get; internal init; }

		/// <summary>
		/// CPU time spent evaluating styles, calculating layout and applying the resulting layout.
		/// </summary>
		public TimeSpan LayoutTime { get; internal init; }

		/// <summary>
		/// CPU time spent drawing panels, batching instances and preparing the render command list.
		/// </summary>
		public TimeSpan CommandBuildTime { get; internal init; }

		/// <summary>
		/// CPU time spent executing this root's render command list, including nested lists and custom draws.
		/// </summary>
		public TimeSpan SubmissionTime { get; internal init; }

		/// <summary>
		/// Sum of the latest preparation timings and the last render's submission time.
		/// Excludes panel ticks, input handling, presentation and time waiting between stages.
		/// </summary>
		public TimeSpan CpuTime => LayoutTime + CommandBuildTime + SubmissionTime;

		/// <summary>
		/// Visible panels visited while preparing the rendered command list, including containers without drawing commands.
		/// </summary>
		public int PanelsRendered { get; internal init; }

		/// <summary>
		/// Invisible or unlaid-out subtree roots skipped while preparing the rendered command list.
		/// Descendants of a skipped subtree are not traversed or counted. GPU clipping is not included.
		/// </summary>
		public int PanelsCulled { get; internal init; }

		/// <summary>
		/// Direct UI batch, backdrop and CSS layer composite draw calls in the rendered command list.
		/// Excludes custom scene draws and passes used to prepare Painter layers or framebuffer grabs.
		/// </summary>
		public int DrawCalls { get; internal init; }

		/// <summary>
		/// Box and shape instances gathered into UI batches. A panel may contribute multiple instances.
		/// </summary>
		public int Instances { get; internal init; }

		/// <summary>
		/// CSS panel layers composited into this root. Excludes layers inside individual Painter recordings.
		/// </summary>
		public int Layers { get; internal init; }

		/// <summary>
		/// Framebuffer grabs recorded for CSS and Painter backdrops, counting a shared grab only once.
		/// </summary>
		public int FrameGrabs { get; internal init; }
	}

	internal struct FrameStats
	{
		internal int Panels;
		internal int PanelsCulled;
		internal int LayerPanels;
		internal int DrawCalls;
		internal int InstanceCount;
		internal int FrameGrabs;
	}

	readonly object _statisticsLock = new();
	readonly Action _onRenderStarted;
	readonly Action _onRenderCompleted;
	RenderStatistics _renderStatistics;
	RenderStatistics _preparedStatistics;
	RenderStatistics _activeStatistics;
	TimeSpan _layoutTime;
	long _renderStarted;

	internal void BeginRenderStatistics( CommandList commands )
	{
		// These delegates are created once per root. Recording and replaying them does not allocate.
		commands.AddAction( _onRenderStarted );
	}

	internal void EndRenderStatistics( CommandList commands, in FrameStats counters, TimeSpan elapsed )
	{
		lock ( _statisticsLock )
		{
			_preparedStatistics = new RenderStatistics
			{
				LayoutTime = _layoutTime,
				CommandBuildTime = elapsed,
				PanelsRendered = counters.Panels,
				PanelsCulled = counters.PanelsCulled,
				DrawCalls = counters.DrawCalls,
				Instances = counters.InstanceCount,
				Layers = counters.LayerPanels,
				FrameGrabs = counters.FrameGrabs
			};
		}

		commands.AddAction( _onRenderCompleted );
	}

	void RenderStarted()
	{
		lock ( _statisticsLock )
		{
			_activeStatistics = _preparedStatistics with
			{
				RenderCount = _renderStatistics.RenderCount + 1,
				FrameNumber = Application.FrameCount
			};
		}

		_renderStarted = Stopwatch.GetTimestamp();
	}

	void RenderCompleted()
	{
		var elapsed = Stopwatch.GetElapsedTime( _renderStarted );
		lock ( _statisticsLock )
		{
			_renderStatistics = _activeStatistics with { SubmissionTime = elapsed };
		}
	}
}
