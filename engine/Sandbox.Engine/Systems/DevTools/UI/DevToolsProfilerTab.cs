namespace Sandbox.Diagnostics;

internal sealed class DevToolsProfilerTab : DevToolsTab
{
	public override string TabId => "profiler";
	public override string Title => "Profiler";

	public DevToolsProfilerTab()
	{
		AddClass( "profiler-page" );
		var capture = AddChild( new DevToolsSection( "Timeline" ) );
		var analysis = AddChild( new DevToolsSection( "Scope analysis" ) );
		analysis.Head.Style.Display = Sandbox.UI.DisplayMode.None;
		var memory = AddChild( new DevToolsSection( "Memory" ) );
		_ = new DevToolsProfilerTimeline( capture, analysis, memory );
	}

	public override void Refresh( DevTools.Summary summary ) { }
}
