namespace Facepunch;

internal static class Constants
{
	internal enum Solutions
	{
		Engine,
		Toolbase,
		Menu,
		BuildTools,
		All,
	}

	/// <summary>
	/// Specifies the target environment for a build
	/// </summary>
	internal enum BuildTarget
	{
		/// <summary>
		/// Staging environment for testing
		/// </summary>
		Staging,

		/// <summary>
		/// Production release environment
		/// </summary>
		Release
	}

	internal enum ExitCode
	{
		Success = 0,
		Failure = 1,
	}

	internal static string GetSolutionDir( Solutions solution )
	{
		switch ( solution )
		{
			case Solutions.Engine:
				return "engine/";
			case Solutions.Toolbase:
				return "game/addons/tools/";
			case Solutions.Menu:
				return "game/addons/menu/";
			case Solutions.BuildTools:
				return "engine/Tools/";
			default:
				throw new ArgumentOutOfRangeException( nameof( solution ), solution, null );
		}
	}

	internal static string BuildTargetToSteamBranch( BuildTarget target )
	{
		return target switch
		{
			BuildTarget.Staging => "staging",
			BuildTarget.Release => "release",
			_ => throw new ArgumentOutOfRangeException( nameof( target ), target, null )
		};
	}
}
