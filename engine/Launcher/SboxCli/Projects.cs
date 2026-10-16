using System.Text.Json.Nodes;

namespace Sandbox.Cli;

/// <summary>
/// New projects from the installation's templates, made the same way as the launcher's project
/// creator: the template's files via <see cref="ProjectTemplateFiles"/>, its .sbproj with the new
/// ident, title and a local org, and the standard .gitignore.
/// </summary>
internal static class Projects
{
	record Template( string Name, string Path, JsonObject Config, int Order );

	/// <summary>
	/// Print the available templates as JSON, in the order the launcher shows them.
	/// </summary>
	internal static int ListTemplates( Options options )
	{
		var templates = new JsonArray();

		foreach ( var template in Find( options.EngineDirectory ) )
		{
			var display = template.Config["Metadata"]?["ProjectTemplate"];
			templates.Add( new JsonObject
			{
				["name"] = template.Name,
				["title"] = template.Config["Title"]?.DeepClone(),
				["type"] = template.Config["Type"]?.DeepClone(),
				["description"] = display?["Description"]?.DeepClone()
			} );
		}

		using var output = Skills.Utf8( Console.OpenStandardOutput() );
		output.WriteLine( templates.ToJsonString( Program.Readable ) );
		return 0;
	}

	/// <summary>
	/// Create a project titled options.Target in a new folder, named after its ident, inside
	/// options.Folder or the current directory. Prints the new .sbproj path.
	/// </summary>
	internal static int Create( Options options )
	{
		var templates = Find( options.EngineDirectory );
		var template = options.TemplateName is null
			? templates.FirstOrDefault() ?? throw new DirectoryNotFoundException( $"No project templates in {TemplatesDirectory( options.EngineDirectory )}." )
			: templates.FirstOrDefault( x => x.Name.Equals( options.TemplateName, StringComparison.OrdinalIgnoreCase ) )
				?? throw new ArgumentException( $"No template named '{options.TemplateName}'. Use one of: {string.Join( ", ", templates.Select( x => x.Name ) )}." );

		var title = options.Target.Trim();
		var ident = ProjectTemplateFiles.MakeIdent( title );
		if ( ident.Length == 0 )
			throw new ArgumentException( $"'{title}' has no letters or digits to make a project ident from." );

		var path = Path.Combine( Path.GetFullPath( options.Folder ?? Directory.GetCurrentDirectory() ), ident );
		if ( Path.Exists( path ) )
			throw new IOException( $"{path} already exists." );

		ProjectTemplateFiles.Copy( template.Path, path, ident, title );

		var config = (JsonObject)template.Config.DeepClone();
		config["Ident"] = ident;
		config["Title"] = title;
		config["Org"] = "local";
		config["Type"] ??= "game";
		(config["Metadata"] as JsonObject)?.Remove( "ProjectTemplate" );

		var configPath = Path.Combine( path, $"{ident}.sbproj" );
		File.WriteAllText( configPath, config.ToJsonString( Program.Readable ) );

		var gitignore = Path.Combine( TemplatesDirectory( options.EngineDirectory ), "template.gitignore" );
		if ( File.Exists( gitignore ) && !File.Exists( Path.Combine( path, ".gitignore" ) ) )
			File.Copy( gitignore, Path.Combine( path, ".gitignore" ) );

		using ( var output = Skills.Utf8( Console.OpenStandardOutput() ) )
			output.WriteLine( configPath );

		Console.Error.WriteLine( $"sbox-cli: created {title} from the {template.Name} template. Open it with: sbox-cli --project \"{path}\"" );
		return 0;
	}

	static string TemplatesDirectory( string engine ) => Path.Combine( engine, "templates" );

	/// <summary>
	/// Template folders hold a $ident.sbproj with ProjectTemplate metadata, as the launcher requires.
	/// </summary>
	static List<Template> Find( string engine )
	{
		var templates = new List<Template>();
		var directory = TemplatesDirectory( engine );
		if ( !Directory.Exists( directory ) )
			return templates;

		foreach ( var folder in Directory.GetDirectories( directory ) )
		{
			var configPath = Path.Combine( folder, ProjectTemplateFiles.ConfigFileName );
			if ( !File.Exists( configPath ) )
				continue;

			try
			{
				if ( JsonNode.Parse( File.ReadAllText( configPath ) ) is not JsonObject config || config["Metadata"]?["ProjectTemplate"] is not JsonObject display )
					continue;

				var order = display["Order"] is JsonValue value && value.TryGetValue<int>( out var o ) ? o : 0;
				templates.Add( new Template( Path.GetFileName( folder ), folder, config, order ) );
			}
			catch ( System.Text.Json.JsonException )
			{
				// A broken template doesn't stop the others being listed, as in the launcher.
			}
		}

		return templates.OrderBy( x => x.Order ).ToList();
	}
}
