using Sandbox.UI;

namespace Editor.Mcp;

/// <summary>
/// What the UI cost to draw last frame, per root panel. The same numbers the overlay_ui overlay shows.
/// </summary>
public static partial class UiTools
{
	/// <summary>
	/// Each root panel's statistics from its last render: timings, panels, draw calls and instances.
	/// Instances per draw call is the number that matters: a batch breaks whenever the blend mode changes
	/// between boxes, so anything that draws with its own blend mode splits the run it sits in.
	/// </summary>
	[McpTool.ReadOnly( "ui_batch_stats" )]
	public static object BatchStats()
	{
		return RootPanels().OfType<RootPanel>().Select( root =>
		{
			var stats = root.Stats;
			return new
			{
				Root = root.GetType().Name,
				stats.RenderCount,
				LayoutMs = stats.LayoutTime.TotalMilliseconds,
				CommandBuildMs = stats.CommandBuildTime.TotalMilliseconds,
				SubmissionMs = stats.SubmissionTime.TotalMilliseconds,
				stats.PanelsRendered,
				stats.PanelsCulled,
				stats.DrawCalls,
				stats.Instances,
				stats.Layers,
				stats.FrameGrabs,
				InstancesPerDrawCall = stats.DrawCalls > 0 ? (float)stats.Instances / stats.DrawCalls : 0
			};
		} ).ToArray();
	}
}
