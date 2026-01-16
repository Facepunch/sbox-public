using Sandbox.DataModel;
using System.IO;

namespace Editor;

public class ProjectCreator : Dialog
{
	public Action<string> OnProjectCreated { get; set; }

	private Button OkayButton;

	private LineEdit TitleEdit;
	private LineEdit IdentEdit;
	private Checkbox CreateGitIgnore;
	private Checkbox SetDefaultProjectLocation;
	private FolderEdit FolderEdit;
	private FieldSubtitle FolderFullPath;
	private ProjectTemplate ActiveTemplate;

	private ProjectTemplates Templates;

	private ErrorBox FolderError;

	private bool identEdited;

	public ProjectCreator( Widget parent = null ) : base( null )
	{
		Window.Size = new Vector2( 800, 500 );
		Window.MaximumSize = Window.Size;
		Window.MinimumSize = Window.Size;
		Window.SetModal( true, true );

		Window.Title = "Create New Project";
		Window.SetWindowIcon( "sports_esports" );

		Layout = Layout.Row();
		Layout.Margin = 4;

		// Template List
		{
			var column = Layout.AddColumn( 3 );
			column.Margin = 12;

			column.AddSpacingCell( 8.0f );
			column.Add( new Label.Subtitle( "Templates" ) );
			column.AddSpacingCell( 18.0f );

			var templates = column.Add( new ProjectTemplates( this ) );
			Templates = templates;
		}

		// Body
		{
			var body = Layout.AddColumn( 2 );
			body.Margin = 12;
			body.Spacing = 8;

			body.AddSpacingCell( 8.0f );
			body.Add( new Label.Subtitle( "Project Setup" ) );
			body.AddSpacingCell( 12.0f );

			body.Add( new FieldTitle( "Title" ) );
			TitleEdit = body.Add( new LineEdit( "" ) { PlaceholderText = "Garry's Project" } );
			TitleEdit.Text = DefaultProjectName();
			TitleEdit.TextEdited += ( x ) => Validate();

			body.AddSpacingCell( 8 );

			body.Add( new FieldTitle( "Ident" ) );
			body.Add( new FieldSubtitle( "Lowercase version of addon name, no special characters" ) );
			IdentEdit = body.Add( new LineEdit( "" ) { PlaceholderText = "garrysproject" } );
			IdentEdit.TextEdited += ( x ) => Validate();
			IdentEdit.TextEdited += ( x ) => identEdited = true;
			IdentEdit.SetValidator( "[a-z0-9_]{2,32}" );

			body.AddSpacingCell( 8 );

			body.Add( new FieldTitle( "Location" ) );
			FolderEdit = body.Add( new FolderEdit( null ) );
			FolderEdit.PlaceholderText = LauncherPreferences.DefaultProjectLocation.NormalizeFilename( false );
			FolderEdit.Text = LauncherPreferences.DefaultProjectLocation.NormalizeFilename( false );
			FolderEdit.TextEdited += ( x ) =>
			{
				Validate();
			};
			FolderEdit.FolderSelected += ( x ) =>
			{
				Validate();
			};

			FolderError = body.Add( new ErrorBox() );
			FolderError.Visible = false;
			FolderError.MinimumHeight = 34;
			FolderError.WordWrap = true;

			body.AddSpacingCell( 8 );

			body.Add( new FieldTitle( "Other" ) );
			CreateGitIgnore = body.Add( new Checkbox() );
			CreateGitIgnore.Value = true;
			CreateGitIgnore.Text = "Create .gitignore";

			SetDefaultProjectLocation = body.Add( new Checkbox() );
			SetDefaultProjectLocation.Value = false;
			SetDefaultProjectLocation.Text = "Set as Default Project Location";

			body.AddStretchCell( 1 );

			var footer = body.AddRow();
			footer.Spacing = 8;

			FolderFullPath = footer.Add( new FieldSubtitle( "" ) );
			footer.AddStretchCell();

			OkayButton = footer.Add( new Button.Primary( "Create", "add_box" ) { Clicked = CreateProject } );
		}

		Templates.ListView.ItemSelected += ( object item ) => { ActiveTemplate = item as ProjectTemplate; };
		ActiveTemplate = Templates.ListView.SelectedItems.First() as ProjectTemplate;

		Validate();
	}

	static string DefaultProjectName()
	{
		string name = "My Project";

		// If Location/my_project already exists, append 1, 2, etc.
		int i = 1;
		while ( Path.Exists( Path.Combine( LauncherPreferences.DefaultProjectLocation, ConvertToIdent( name ) ) ) )
		{
			name = $"My Project {i++}";
		}

		return name;
	}

	static string ConvertToIdent( string title )
	{
		return System.Text.RegularExpressions.Regex.Replace( title.ToLower(), "[^A-Za-z0-9_]", "_" ).Trim( '_' );
	}

	void Validate()
	{
		if ( !identEdited )
		{
			IdentEdit.Text = ConvertToIdent( TitleEdit.Text );
		}

		bool enabled = true;
		if ( string.IsNullOrWhiteSpace( FolderEdit.Text ) ) enabled = false;
		if ( string.IsNullOrWhiteSpace( TitleEdit.Text ) ) enabled = false;
		if ( string.IsNullOrWhiteSpace( IdentEdit.Text ) ) enabled = false;

		FolderError.Visible = false;
		string fullPath = Path.Combine( FolderEdit.Text, IdentEdit.Text );
		FolderFullPath.Text = fullPath.NormalizeFilename( false );
		if ( Path.Exists( fullPath ) )
		{
			FolderError.Text = $"{FolderFullPath.Text} already exists";
			FolderError.Visible = true;
			enabled = false;
		}

		// Max 32 characters
		if ( IdentEdit.Text.Length >= 32 )
			IdentEdit.Text = IdentEdit.Text[..Math.Min( IdentEdit.Text.Length, 32 )];

		OkayButton.Enabled = enabled;
	}

	void CreateProject()
	{
		var addonPath = Path.Combine( FolderEdit.Text, IdentEdit.Text );

		Directory.CreateDirectory( addonPath );

		var config = new ProjectConfig();
		config.Ident = IdentEdit.Text;
		config.Title = TitleEdit.Text;
		config.Org = "local";
		config.Type = "game";
		config.Schema = 1;

		var pt = Templates.ListView.ChosenTemplate;
		if ( pt != null )
			pt.Apply( addonPath, ref config );

		var configPath = System.IO.Path.Combine( addonPath, $"{config.Ident}.sbproj" );
		var txt = config.ToJson();

		System.IO.File.WriteAllText( configPath, txt );

		if ( CreateGitIgnore.Value )
		{
			if ( !File.Exists( Path.Combine( addonPath, ".gitignore" ) ) )
			{
				File.Copy( FileSystem.Root.GetFullPath( "/templates/template.gitignore" ), Path.Combine( addonPath, ".gitignore" ) );
			}
		}

		if ( SetDefaultProjectLocation.Value )
		{
			LauncherPreferences.DefaultProjectLocation = FolderEdit.Text;
		}

		Close();

		OnProjectCreated?.Invoke( configPath );
	}
}

// Don't use these form things

internal class ErrorBox : Label
{

}

internal class FieldTitle : Label
{
	public FieldTitle( string title ) : base( title )
	{
	}
}

internal class FieldSubtitle : Label
{
	public FieldSubtitle( string title ) : base( title )
	{
		WordWrap = true;
	}
}
