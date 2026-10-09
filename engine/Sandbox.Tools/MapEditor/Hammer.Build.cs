using System;
using System.IO;

namespace Editor.MapEditor;

/// <summary>
/// The Build Map dialog's presets. Matches BuildPresetList_t.
/// </summary>
public enum MapBuildPreset
{
	/// <summary>Standard compile of everything.</summary>
	Full = 0,

	/// <summary>World, physics and nav - no vis or lighting.</summary>
	Fast,

	/// <summary>Everything, with final quality lighting.</summary>
	Final,

	/// <summary>Only entities.</summary>
	EntitiesOnly,

	/// <summary>Whatever the dialog is currently set to.</summary>
	Custom
}

public static partial class Hammer
{
	/// <summary>
	/// Compile the active map, as if the user pressed Build in the Build Map dialog with this preset.
	/// The dialog shows the progress. Never prompts to save - save first, the build compiles what's
	/// on disk. Returns false if the build couldn't start; <see cref="BuildStatus"/> says why.
	/// </summary>
	/// <param name="preset">What to build.</param>
	/// <param name="loadInEngine">Load the map in engine when it's built, if the dialog is set to.</param>
	public static bool BuildMap( MapBuildPreset preset = MapBuildPreset.Full, bool loadInEngine = false )
	{
		AssertAppValid();
		return App.StartMapBuild( (int)preset, loadInEngine );
	}

	/// <summary>
	/// Whether a map compile is running.
	/// </summary>
	public static bool IsBuildingMap
	{
		get
		{
			AssertAppValid();
			return App.IsMapBuildRunning();
		}
	}

	/// <summary>
	/// The Build Map dialog's status line - "Done", "Failed - Exit Code 1" and so on. Empty if no build has run.
	/// </summary>
	public static string BuildStatus
	{
		get
		{
			AssertAppValid();
			return App.GetMapBuildStatus();
		}
	}

	/// <summary>
	/// Everything the last map compile printed.
	/// </summary>
	public static string BuildLog
	{
		get
		{
			AssertAppValid();
			return App.GetMapBuildLog();
		}
	}

	/// <summary>
	/// Where the active map compiles to - its .vpk next to the .vmap. Null for a map that has never been saved.
	/// </summary>
	public static string CompiledMapPath
	{
		get
		{
			var asset = MapAsset;
			return asset is null ? null : Path.ChangeExtension( asset.AbsolutePath, ".vpk" );
		}
	}
}
