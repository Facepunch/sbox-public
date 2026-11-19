using System.IO;
using System.Text;
using Sandbox.UI;
using Sandbox.UI.Construct;
using Tab = Sandbox.UI.Tab;
using TextEntry = Sandbox.UI.TextEntry;

namespace Editor.CodeEditors;

/// <summary>
/// A project's CSX files, edited in a panel window with persistent document tabs.
/// </summary>
[StyleSheet.Inline( "script-editor", Styles )]
public sealed partial class ScriptEditor : Panel
{
	readonly PanelWindow window;
	readonly string projectRoot;
	readonly TreeView<string> files;
	readonly Dictionary<string, List<string>> folders = new( StringComparer.OrdinalIgnoreCase );
	readonly TextEntry search;
	readonly TabPanel documents;
	readonly Sandbox.UI.Label message;
	readonly Dictionary<Tab, Document> openDocuments = new();
	Panel prompt;

	Document ActiveDocument => documents.TabBar.SelectedTab is { } tab ? openDocuments.GetValueOrDefault( tab ) : null;

	/// <summary>
	/// Open a script asset, reusing its project's window when it is already open.
	/// </summary>
	[EditorForAssetType( "csx" )]
	public static void OpenAsset( Asset asset )
	{
		var path = Path.GetFullPath( asset.AbsolutePath );
		var project = EditorUtility.Projects.GetAll()
			.Where( x => path.StartsWith( Path.TrimEndingDirectorySeparator( Path.GetFullPath( x.GetRootPath() ) ) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase ) )
			.OrderByDescending( x => x.GetRootPath().Length )
			.FirstOrDefault();
		var root = Path.TrimEndingDirectorySeparator( Path.GetFullPath( project?.GetRootPath() ?? Path.GetDirectoryName( path ) ) );
		var editor = PanelWindow.All.SelectMany( x => x.Root.Children ).OfType<ScriptEditor>()
			.FirstOrDefault( x => string.Equals( x.projectRoot, root, StringComparison.OrdinalIgnoreCase ) );

		if ( editor is null )
		{
			var window = new PanelWindow( "Script Editor", new Vector2( 1200, 800 ) ) { MinSize = new Vector2( 700, 400 ) };
			editor = new ScriptEditor( window, root );
			editor.Parent = window.Root;
		}

		editor.OpenDocument( path );
		editor.window.Focus();
	}

	ScriptEditor( PanelWindow window, string projectRoot )
	{
		this.window = window;
		this.projectRoot = projectRoot;
		AddClass( "script-editor" );

		var menu = AddChild( new Sandbox.UI.MenuBar() );
		var fileMenu = menu.AddMenu( "File" );
		fileMenu.AddOption( "Save", "save", () => Save( ActiveDocument ) );
		fileMenu.AddOption( "Save All", "library_books", () => SaveAll() );
		fileMenu.AddOption( "Close", "close", CloseActiveDocument );
		var editMenu = menu.AddMenu( "Edit" );
		editMenu.AddOption( "Undo", "undo", () => ActiveDocument?.Editor.Undo() );
		editMenu.AddOption( "Redo", "redo", () => ActiveDocument?.Editor.Redo() );

		var toolbar = AddChild( new Sandbox.UI.Toolbar() );
		toolbar.AddButton( "", "save", () => Save( ActiveDocument ) ).Tooltip = "Save (Ctrl+S)";
		toolbar.AddButton( "", "library_books", () => SaveAll() ).Tooltip = "Save All (Ctrl+Shift+S)";
		toolbar.AddSeparator();
		toolbar.AddButton( "", "undo", () => ActiveDocument?.Editor.Undo() ).Tooltip = "Undo (Ctrl+Z)";
		toolbar.AddButton( "", "redo", () => ActiveDocument?.Editor.Redo() ).Tooltip = "Redo (Ctrl+Y)";

		var split = AddChild( new Sandbox.UI.SplitContainer() );
		split.UpdateSplitFraction( 0.23f );
		split.MinimumFractionLeft = 0.15f;
		split.MinimumFractionRight = 0.35f;
		var explorerTabs = split.Left.AddChild( new TabPanel() );
		var explorer = new Panel();
		explorer.AddClass( "solution-explorer" );
		explorerTabs.AddTab( "Solution Explorer", explorer );
		var heading = explorer.AddChild( new Sandbox.UI.Toolbar() );
		search = heading.AddChild( new TextEntry { Placeholder = "Search scripts", Icon = "search" } );
		search.OnTextEdited = _ => RefreshFiles();
		heading.AddButton( "", "refresh", RefreshFiles ).Tooltip = "Refresh scripts";
		files = explorer.AddChild( new TreeView<string>
		{
			Comparer = StringComparer.OrdinalIgnoreCase,
			RowHeight = 26,
			GetChildren = path => folders.GetValueOrDefault( path ),
			GetParent = path => string.Equals( Path.GetDirectoryName( path ), projectRoot, StringComparison.OrdinalIgnoreCase ) ? null : Path.GetDirectoryName( path ),
			CanExpand = path => folders.ContainsKey( path ),
			OnRow = ( row, path ) =>
			{
				row.Text = Path.GetFileName( path );
				row.Icon.Text = folders.ContainsKey( path ) ? "folder" : "code";
				row.SetClass( "folder", folders.ContainsKey( path ) );
			},
			GetTooltip = path => path,
			OnSelect = path => { if ( !folders.ContainsKey( path ) ) OpenDocument( path ); }
		} );

		documents = split.Right.AddChild( new TabPanel() );
		documents.TabBar.AllowReorder = true;
		documents.TabBar.SelectionChanged += _ => UpdateTitle();
		documents.TabBar.CanCloseTab = CanCloseDocument;
		documents.TabBar.TabRemoved += tab => openDocuments.Remove( tab );
		message = split.Right.Add.Label( "", "editor-message" );
		message.Style.Display = DisplayMode.None;
		window.OnCloseRequested = CanCloseWindow;
		window.OnActivated = RefreshFiles;
		RefreshFiles();
	}

	void RefreshFiles()
	{
		var prefix = Path.TrimEndingDirectorySeparator( projectRoot ) + Path.DirectorySeparatorChar;
		var paths = AssetSystem.All
			.Where( x => string.Equals( Path.GetExtension( x.AbsolutePath ), ".csx", StringComparison.OrdinalIgnoreCase ) )
			.Select( x => Path.GetFullPath( x.AbsolutePath ) )
			.Where( x => x.StartsWith( prefix, StringComparison.OrdinalIgnoreCase ) )
			.Where( x => Path.GetRelativePath( projectRoot, x ).Contains( search.Text ?? "", StringComparison.OrdinalIgnoreCase ) )
			.Distinct( StringComparer.OrdinalIgnoreCase )
			.OrderBy( x => x, StringComparer.OrdinalIgnoreCase ).ToArray();

		folders.Clear();
		folders[projectRoot] = new();
		foreach ( var path in paths )
		{
			var child = path;
			var parent = Path.GetDirectoryName( child );
			while ( parent is not null && !folders.ContainsKey( parent ) )
			{
				folders[parent] = new() { child };
				child = parent;
				parent = Path.GetDirectoryName( parent );
			}
			if ( parent is not null ) folders[parent].Add( child );
		}

		foreach ( var children in folders.Values )
		{
			children.Sort( ( a, b ) =>
			{
				var folderOrder = folders.ContainsKey( b ).CompareTo( folders.ContainsKey( a ) );
				return folderOrder != 0 ? folderOrder : StringComparer.OrdinalIgnoreCase.Compare( Path.GetFileName( a ), Path.GetFileName( b ) );
			} );
		}
		files.Roots = folders[projectRoot];
		if ( !string.IsNullOrWhiteSpace( search.Text ) )
		{
			foreach ( var path in paths ) files.ExpandPathTo( path );
		}
	}

	void OpenDocument( string path )
	{
		if ( prompt is not null || string.IsNullOrEmpty( path ) ) return;
		var document = openDocuments.Values.FirstOrDefault( x => string.Equals( x.Path, path, StringComparison.OrdinalIgnoreCase ) );
		if ( document is null )
		{
			try
			{
				using var reader = new StreamReader( path, new UTF8Encoding( false ), true );
				var source = reader.ReadToEnd();
				var control = new ScriptControl { Source = source };
				var tab = documents.AddTab( Path.GetFileName( path ), control, "code", true );
				document = new Document( path, source, reader.CurrentEncoding, control, tab );
				openDocuments.Add( tab, document );
				tab.Tooltip = path;
				control.OnSourceChanged = _ => UpdateTitle();
			}
			catch ( Exception e )
			{
				ShowError( $"Could not open {Path.GetFileName( path )}: {e.Message}" );
				return;
			}
		}

		documents.TabBar.SelectTab( document.Tab );
		files.ExpandPathTo( path );
		UpdateTitle();
		document.Editor.FocusEditor();
	}

	void UpdateTitle()
	{
		foreach ( var document in openDocuments.Values )
		{
			document.Tab.Text = Path.GetFileName( document.Path ) + (document.IsDirty ? " *" : "");
		}
		window.Title = ActiveDocument?.Tab.Text ?? "Script Editor";
		message.Style.Display = DisplayMode.None;
		if ( ActiveDocument is { } active && !files.IsSelected( active.Path ) ) files.Select( active.Path );
	}

	void ShowError( string text )
	{
		message.Text = text;
		message.Style.Display = DisplayMode.Flex;
	}

	bool Save( Document document )
	{
		if ( document is null || !document.IsDirty ) return true;
		try
		{
			// Don't overwrite edits made in another editor while this tab was open.
			if ( File.ReadAllText( document.Path ) != document.SavedText )
			{
				ShowError( $"{Path.GetFileName( document.Path )} changed on disk. Copy your edits before closing and reopening this tab." );
				return false;
			}

			var source = document.Editor.Source.Replace( "\n", document.Newline );
			File.WriteAllText( document.Path, source, document.Encoding );
			document.SavedText = source;
			UpdateTitle();
			return true;
		}
		catch ( Exception e )
		{
			ShowError( $"Could not save {Path.GetFileName( document.Path )}: {e.Message}" );
			return false;
		}
	}

	bool SaveAll()
	{
		foreach ( var document in openDocuments.Values )
		{
			if ( !Save( document ) ) return false;
		}
		return true;
	}

	void CloseActiveDocument()
	{
		if ( documents.TabBar.SelectedTab is { } tab ) documents.TabBar.CloseTab( tab );
	}

	bool CanCloseDocument( Tab tab )
	{
		if ( !openDocuments.TryGetValue( tab, out var document ) || !document.IsDirty ) return true;
		ConfirmClose( $"Save changes to {Path.GetFileName( document.Path )}?", () => Save( document ), () => documents.TabBar.RemoveTab( tab ) );
		return false;
	}

	bool CanCloseWindow()
	{
		if ( !openDocuments.Values.Any( x => x.IsDirty ) ) return true;
		ConfirmClose( "Save changes to your scripts before closing?", SaveAll, window.Dispose );
		return false;
	}

	void ConfirmClose( string text, Func<bool> save, Action close )
	{
		if ( prompt is not null ) return;
		prompt = Add.Panel( "close-overlay" );
		var dialog = prompt.Add.Panel( "close-dialog" );
		dialog.Add.Label( text );
		var buttons = dialog.AddChild( new Sandbox.UI.Toolbar() );
		buttons.AddButton( "Save", "save", () =>
		{
			DismissPrompt();
			if ( save() ) close();
		} ).Focus();
		buttons.AddButton( "Don't Save", "delete", () => { DismissPrompt(); close(); } );
		buttons.AddButton( "Cancel", "close", DismissPrompt );
	}

	void DismissPrompt()
	{
		prompt?.Delete( true );
		prompt = null;
		ActiveDocument?.Editor.FocusEditor();
	}

	/// <summary>
	/// Handle document shortcuts while the text control keeps its own editing shortcuts.
	/// </summary>
	public override void OnButtonTyped( ButtonEvent e )
	{
		if ( e.Pressed && prompt is null && e.HasCtrl && !e.HasAlt && e.Button is "s" or "w" )
		{
			if ( e.Button == "w" )
			{
				CloseActiveDocument();
			}
			else if ( e.HasShift )
			{
				SaveAll();
			}
			else
			{
				Save( ActiveDocument );
			}
			e.StopPropagation = true;
			return;
		}
		base.OnButtonTyped( e );
	}

	sealed class Document( string path, string source, Encoding encoding, ScriptControl editor, Tab tab )
	{
		internal string Path { get; } = path;
		internal string SavedText { get; set; } = source;
		internal Encoding Encoding { get; } = encoding;
		internal string Newline { get; } = source.Contains( "\r\n" ) ? "\r\n" : source.Contains( '\r' ) ? "\r" : "\n";
		internal ScriptControl Editor { get; } = editor;
		internal Tab Tab { get; } = tab;
		internal bool IsDirty => Editor.Source != SavedText.Replace( "\r\n", "\n" ).Replace( '\r', '\n' );
	}
}
