using System.Diagnostics;

namespace Editor;

public static class DebuggingMenus
{
	[Menu( "Editor", "Debug/Show Physics Debug", "grid_3x3" )]
	public static bool ShowPhysicsDebug
	{
		get => ConsoleSystem.GetValueInt( "physics_debug_draw" ) == 1;
		set => ConsoleSystem.SetValue( "physics_debug_draw", value ? "1" : "0" );
	}

	[Menu( "Editor", "Debug/Show Interpolation Debug", "fiber_smart_record" )]
	public static bool ShowInterpolationDebug
	{
		get => ConsoleSystem.GetValueInt( "debug_interp" ) == 1;
		set => ConsoleSystem.SetValue( "debug_interp", value ? "1" : "0" );
	}

	[Menu( "Editor", "Debug/Show Hitbox Debug", "ads_click" )]
	public static bool ShowHitboxDebug
	{
		get => ConsoleSystem.GetValueInt( "debug_hitbox" ) == 1;
		set => ConsoleSystem.SetValue( "debug_hitbox", value ? "1" : "0" );
	}

	[Menu( "Editor", "Debug/Visualize Scene Objects", "grid_3x3" )]
	public static bool VisualizeSceneObjects
	{
		get => ConsoleSystem.GetValueInt( "sc_visualize_sceneobjects" ) == 1;
		set => ConsoleSystem.SetValue( "sc_visualize_sceneobjects", value ? "1" : "0" );
	}

	[Menu( "Editor", "Debug/Display Localization Keys", "translate" )]
	public static bool ShowLocalizationKeys
	{
		get => ConsoleSystem.GetValueInt( "lang.showkeys" ) == 1;
		set => ConsoleSystem.SetValue( "lang.showkeys", value ? "1" : "0" );
	}

	[Menu( "Editor", "Debug/Full Garbage Collection" )]
	public static void FullCollection()
	{
		var sw = Stopwatch.StartNew();
		GC.Collect();
		Log.Info( $"Collection took {sw.Elapsed.TotalMilliseconds:0.00}ms" );
	}
}
