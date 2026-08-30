namespace Editor
{

	internal static class EngineTools
	{
		public record ToolDescription( string Name, string Description, string Library, string Icon );

		/// <summary>
		/// All native tools registered on this machine, including unavailable tools.
		/// </summary>
		internal static IReadOnlyList<ToolDescription> All { get; } = new List<ToolDescription>
		{
			new ToolDescription( "Hammer",                  "For editing maps",                 "hammer",               "handyman" ),
			new ToolDescription( "Material Editor",         "For editing materials",            "met",                  "insert_photo" ),
			new ToolDescription( "Model Editor",            "For editing models",               "modeldoc_editor",      "view_in_ar" ),
			new ToolDescription( "Animgraph Editor",        "For editing animation graphs",     "animgraph_editor",     "directions_run" ),
		}.AsReadOnly();

		static readonly HashSet<string> UnavailableTools = new( System.StringComparer.OrdinalIgnoreCase );

		internal static void SetAvailable( string library )
		{
			UnavailableTools.Remove( library );
		}

		internal static void SetUnavailable( string library )
		{
			UnavailableTools.Add( library );
		}

		internal static bool IsAvailable( string library )
		{
			return !UnavailableTools.Contains( library );
		}

		internal static bool EnsureAvailable( string library )
		{
			if ( !UnavailableTools.Contains( library ) )
			{
				return true;
			}

			var tool = All.FirstOrDefault( x => x.Library.Equals( library, System.StringComparison.OrdinalIgnoreCase ) );
			EditorUtility.DisplayDialog(
				$"{tool?.Name ?? "Editor"} Unavailable",
				GetUnavailableMessage() );
			return false;
		}

		internal static string GetUnavailableMessage()
		{
			if ( !System.OperatingSystem.IsWindows() )
			{
				return "Native tools aren't supported on non-Windows builds.";
			}

			return "The native editor library couldn't be loaded.";
		}

		internal static void ShowTool( ToolDescription tool )
		{
			if ( !EnsureAvailable( tool.Library ) )
			{
				return;
			}

			Native.ToolGlue.ShowTool( $"tools/{tool.Library}.dll" );
		}
	}
}
