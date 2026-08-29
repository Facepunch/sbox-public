namespace Editor
{

	internal static class EngineTools
	{
		public record ToolDescription( string Name, string Description, string Library, string Icon );

		static readonly List<ToolDescription> AllTools = new()
		{
			new ToolDescription( "Hammer",                  "For editing maps",                 "hammer",               "handyman" ),
			new ToolDescription( "Material Editor",         "For editing materials",            "met",                  "insert_photo" ),
			new ToolDescription( "Model Editor",            "For editing models",               "modeldoc_editor",      "view_in_ar" ),
			new ToolDescription( "Animgraph Editor",        "For editing animation graphs",     "animgraph_editor",     "directions_run" ),
		};

		static readonly Dictionary<string, string> UnavailableTools = new( System.StringComparer.OrdinalIgnoreCase );
		static readonly IReadOnlyList<ToolDescription> AllToolsView = AllTools.AsReadOnly();

		/// <summary>
		/// All native tools registered on this machine, including unavailable tools.
		/// </summary>
		public static IReadOnlyList<ToolDescription> All => AllToolsView;

		internal static void SetAvailable( string library )
		{
			UnavailableTools.Remove( library );
		}

		internal static void SetUnavailable( string library, string reason )
		{
			UnavailableTools[library] = reason;
		}

		internal static bool IsAvailable( string library )
		{
			return !UnavailableTools.ContainsKey( library );
		}

		internal static bool EnsureAvailable( string library )
		{
			if ( !UnavailableTools.TryGetValue( library, out var reason ) )
				return true;

			var tool = AllTools.FirstOrDefault( x => x.Library.Equals( library, System.StringComparison.OrdinalIgnoreCase ) );
			EditorUtility.DisplayDialog(
				$"{tool?.Name ?? "Editor"} Unavailable",
				$"The native editor library couldn't be loaded.\n\n{reason}" );
			return false;
		}

		public static void ShowTool( string name )
		{
			var tool = AllTools.First( x => x.Name == name );
			if ( !EnsureAvailable( tool.Library ) )
				return;

			Native.ToolGlue.ShowTool( $"tools/{tool.Library}.dll" );
		}
	}
}
