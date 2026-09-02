using Sandbox.DataModel;
using Sandbox.UI;
using Sandbox.UI.Construct;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Sandbox.LauncherUI;

/// <summary>
/// New project screen - pick a template, name it, create it. The creation itself is the same as
/// the Qt creator's: copy the template with $ident/$title substitution, write the .sbproj.
/// </summary>
class ProjectCreatorPanel : Panel
{
	/// <summary>
	/// Done - with the new .sbproj path, or null if cancelled.
	/// </summary>
	public Action<string> OnDone { get; set; }

	record Template( string Path, ProjectConfig Config, string Icon, string Description, int Order );

	readonly List<Template> _templates = new();
	Template _selected;

	TextEntry _nameBox;
	Editor.FolderSelector _folderBox;
	Checkbox _createGitIgnore;
	Checkbox _setDefaultLocation;
	Label _description;
	Label _pathPreview;

	public ProjectCreatorPanel()
	{
		AddClass( "creator" );

		var container = this.Add.Panel( "creator-container" );
		container.Add.Label( "New Project", "heading" );

		var body = container.Add.Panel( "creator-body" );

		var templates = body.Add.Panel( "template-pane" );
		templates.Add.Label( "Choose a starting point", "section-heading" );

		FindTemplates();
		BuildTemplateGrid( templates );

		var settings = body.Add.Panel( "settings" );
		settings.Add.Label( "Project details", "section-heading" );

		var nameField = Field( settings, "Title" );
		_nameBox = nameField.AddChild<TextEntry>();
		_nameBox.Placeholder = "My Project";
		_nameBox.OnTextEdited = _ => UpdatePathPreview();

		var folderField = Field( settings, "Location" );
		_folderBox = folderField.AddChild<Editor.FolderSelector>();
		_folderBox.Text = LauncherPreferences.DefaultProjectLocation;
		_folderBox.ValueChanged = _ => UpdatePathPreview();

		var otherField = Field( settings, "Other" );

		_createGitIgnore = otherField.AddChild<Checkbox>();
		_createGitIgnore.LabelText = "Create .gitignore";
		_createGitIgnore.Checked = true;

		_setDefaultLocation = otherField.AddChild<Checkbox>();
		_setDefaultLocation.LabelText = "Set as Default Project Location";

		var buttons = container.Add.Panel( "buttons" );

		buttons.AddChild( new Button( "Back", null, "flatbutton", () => OnDone?.Invoke( null ) ) );

		buttons.Add.Panel( "grow" );

		_pathPreview = buttons.Add.Label( "", "path-preview" );

		buttons.AddChild( new Button( "Create", "add_box", "primarybutton", Create ) );

		UpdatePathPreview();
	}

	Panel Field( Panel parent, string title )
	{
		var field = parent.AddChild<Panel>();
		field.AddClass( "field" );
		field.Add.Label( title, "label" );
		return field;
	}

	/// <summary>
	/// The same template scan the Qt creator did - every folder in /templates with an
	/// $ident.sbproj in it. The ProjectTemplate meta block is what marks one as shown here,
	/// and carries its icon, description and sort order.
	/// </summary>
	void FindTemplates()
	{
		var root = EngineFileSystem.Root;

		if ( !root.DirectoryExists( "/templates" ) ) return;

		foreach ( var directory in root.FindDirectory( "/templates" ) )
		{
			var configPath = $"/templates/{directory}/$ident.sbproj";
			if ( !root.FileExists( configPath ) ) continue;

			try
			{
				var config = JsonSerializer.Deserialize<ProjectConfig>( root.ReadAllText( configPath ) );

				if ( !config.TryGetMeta( "ProjectTemplate", out JsonElement display ) ) continue;

				var icon = "question_mark";
				if ( display.TryGetProperty( "Icon", out var iconProperty ) )
					icon = iconProperty.GetString() ?? icon;

				var description = "No description provided.";
				if ( display.TryGetProperty( "Description", out var descriptionProperty ) )
					description = descriptionProperty.GetString() ?? description;

				var order = 0;
				if ( display.TryGetProperty( "Order", out var orderProperty ) && orderProperty.TryGetInt32( out var o ) )
					order = o;

				_templates.Add( new Template( root.GetFullPath( $"/templates/{directory}" ), config, icon, description, order ) );
			}
			catch
			{
				// A broken template doesn't take the creator down
			}
		}

		_templates.Sort( ( a, b ) => a.Order.CompareTo( b.Order ) );
	}

	void BuildTemplateGrid( Panel parent )
	{
		var grid = parent.AddChild<Panel>();
		grid.AddClass( "templates" );

		var cells = new Dictionary<Template, Panel>();

		foreach ( var template in _templates )
		{
			var current = template;

			var cell = grid.Add.Panel( "template" );
			cell.AddEventListener( "onclick", () =>
			{
				_selected = current;
				_description.Text = current.Description;

				foreach ( var (t, p) in cells )
					p.SetClass( "selected", t == current );
			} );

			cell.Add.Icon( current.Icon, "icon" );
			cell.Add.Label( current.Config.Title );

			cells[current] = cell;
		}

		_description = parent.Add.Label( "", "template-description" );

		_selected = _templates.FirstOrDefault();

		if ( _selected is not null && cells.TryGetValue( _selected, out var first ) )
		{
			first.SetClass( "selected", true );
			_description.Text = _selected.Description;
		}
	}

	static string MakeIdent( string title )
	{
		return System.Text.RegularExpressions.Regex.Replace( title.ToLower(), "[^A-Za-z0-9_]", "_" ).Trim( '_' );
	}

	string ProjectTitle => string.IsNullOrWhiteSpace( _nameBox.Text ) ? "My Project" : _nameBox.Text.Trim();

	string ProjectFolder => string.IsNullOrWhiteSpace( _folderBox.Text ) ? LauncherPreferences.DefaultProjectLocation : _folderBox.Text.Trim();

	string ProjectIdent
	{
		get
		{
			var ident = MakeIdent( ProjectTitle );
			return ident.Length == 0 ? "my_project" : ident;
		}
	}

	string ProjectPath => Path.Combine( ProjectFolder, ProjectIdent );

	/// <summary>
	/// The footer shows where the project will end up, and complains if it's already taken.
	/// </summary>
	void UpdatePathPreview()
	{
		var path = ProjectPath;
		var exists = Path.Exists( path );

		_pathPreview.Text = exists ? $"{path} already exists" : path;
		_pathPreview.SetClass( "error", exists );
	}

	/// <summary>
	/// Make the project - the same steps as the Qt creator.
	/// </summary>
	void Create()
	{
		var projectPath = ProjectPath;

		if ( Path.Exists( projectPath ) )
		{
			UpdatePathPreview();
			return;
		}

		Directory.CreateDirectory( projectPath );

		var config = new ProjectConfig
		{
			Ident = ProjectIdent,
			Title = ProjectTitle,
			Org = "local",
			Type = _selected?.Config.Type ?? "game",
			Schema = 1,
		};

		if ( _selected is not null )
		{
			CopyTemplate( _selected.Path, projectPath, config.Ident, config.Title );
		}

		var configPath = Path.Combine( projectPath, $"{config.Ident}.sbproj" );
		File.WriteAllText( configPath, config.ToJson() );

		if ( _createGitIgnore.Checked && !File.Exists( Path.Combine( projectPath, ".gitignore" ) ) )
		{
			File.Copy( EngineFileSystem.Root.GetFullPath( "/templates/template.gitignore" ), Path.Combine( projectPath, ".gitignore" ) );
		}

		if ( _setDefaultLocation.Checked )
		{
			LauncherPreferences.DefaultProjectLocation = ProjectFolder;
		}

		OnDone?.Invoke( configPath );
	}

	/// <summary>
	/// Copy a template into place, replacing $ident and $title the way the Qt creator does. The
	/// template's own $ident.sbproj is skipped - we write our own.
	/// </summary>
	static void CopyTemplate( string from, string to, string ident, string title )
	{
		foreach ( var file in Directory.EnumerateFiles( from ) )
		{
			if ( Path.GetFileName( file ) == "$ident.sbproj" ) continue;

			var target = Path.Combine( to, Path.GetFileName( file ).Replace( "$ident", ident ) );

			if ( file.EndsWith( ".cs" ) || file.EndsWith( ".json" ) )
			{
				var text = File.ReadAllText( file ).Replace( "$title", title ).Replace( "$ident", ident );
				File.WriteAllText( target, text );
			}
			else
			{
				File.Copy( file, target, true );
			}
		}

		foreach ( var directory in Directory.EnumerateDirectories( from ) )
		{
			var target = Path.Combine( to, Path.GetFileName( directory ) );
			Directory.CreateDirectory( target );
			CopyTemplate( directory, target, ident, title );
		}
	}
}
