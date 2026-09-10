using System.Reflection;

namespace Editor.Mcp;

/// <summary>
/// What the UI cost to draw last frame. The counters live on the renderer's internal stats, the same
/// ones the overlay_ui overlay shows.
/// </summary>
public static partial class UiTools
{
	/// <summary>
	/// Batching counters for the last frame the UI drew - panels, draw calls, batch flushes and
	/// instances. Instances per flush is the number that matters: a batch breaks whenever the blend
	/// mode changes between boxes, so anything that draws with its own blend mode splits the run it
	/// sits in. Play mode has to be running.
	/// </summary>
	[McpTool.ReadOnly( "ui_batch_stats" )]
	public static object BatchStats()
	{
		var type = typeof( Sandbox.UI.Panel ).Assembly.GetType( "Sandbox.UI.PanelRenderer" );
		if ( type is null ) return new { Error = "Couldn't find PanelRenderer" };

		var field = type.GetField( "Stats", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public );
		if ( field is null ) return new { Error = "Couldn't find PanelRenderer.Stats" };

		var stats = field.GetValue( null );
		if ( stats is null ) return new { Error = "Stats was null" };

		var result = new Dictionary<string, object>();

		foreach ( var f in stats.GetType().GetFields( BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic ) )
			result[f.Name] = f.GetValue( stats );

		if ( result.TryGetValue( "FlushCount", out var flushes ) && result.TryGetValue( "InstanceCount", out var instances )
			&& flushes is int f2 && instances is int i2 && f2 > 0 )
		{
			result["InstancesPerFlush"] = (float)i2 / f2;
		}

		return result;
	}
}
