using Sandbox.DataModel;
using System.IO;
using System.Text.Json;
namespace Editor;

internal class ProjectTemplate
{
	ProjectConfig Config { get; init; }
	string TemplatePath { get; init; }

	public string AddonType => Config.Type.ToLower();
	public string Title => Config.Title;
	public string ParentPackage => Config.TryGetMeta<string>( "ParentPackage", out var v ) ? v : null;

	public string Icon { get; set; } = "question_mark";
	public int Order { get; set; } = 0;
	public string Description { get; set; } = "No description provided.";

	public ProjectTemplate( ProjectConfig templateConfig, string path )
	{
		Config = templateConfig;
		TemplatePath = path;

		if ( Config.TryGetMeta( "ProjectTemplate", out DisplayData display ) )
		{
			Icon = display.Icon;
			Order = display.Order ?? 0;
			Description = display.Description ?? "No description provided.";
		}
	}

	public void Apply( string targetDir, ref ProjectConfig config )
	{
		var sourceDir = Editor.FileSystem.Root.GetFullPath( TemplatePath );

		if ( !Directory.Exists( sourceDir ) )
			return;

		//Log.Info( $"Copying {Title} template to destination folder: {targetDir}" );

		ProjectTemplateFiles.Copy( sourceDir, targetDir, config.Ident, config.Title );

		// Grab the .sbproj from the template, and apply what we can.
		// We'll store the old ident and title and restore it later
		var ident = config.Ident;
		var title = config.Title;

		var fullPath = Path.Combine( sourceDir, "$ident.sbproj" );

		if ( File.Exists( fullPath ) )
		{
			// Grab everything we can from the template
			config = JsonSerializer.Deserialize<ProjectConfig>( File.ReadAllText( fullPath ) );

			// Restore ident & title from our addon's preferences
			config.Ident = ident;
			config.Title = title;

			// Clear out ProjectTemplate from our new addon. It's not needed for end users.
			config.SetMeta( "ProjectTemplate", null );

			//Log.Info( $"OK" );
		}
		else
		{
			//Log.Warning( $"Something went wrong while applying template to new project" );
		}
	}

	class DisplayData
	{
		public string Icon { get; set; }
		public int? Order { get; set; }
		public string Description { get; set; }
	}
}
