using System.IO;

namespace Editor;

/// <summary>
/// A popup dialog for creating models from mesh files (.fbx, .obj, .dmx).
/// Lets you configure collision type and output path.
/// </summary>
public class CreateModelFromMeshDialog : Widget
{
	public enum CollisionMode
	{
		Hull,
		Mesh,
		None,
	}

	readonly List<Asset> _meshFiles;
	readonly ComboBox _collisionCombo;
	readonly LineEdit _fileEdit;
	readonly FolderEdit _folderEdit;
	readonly Widget _fileRow;
	readonly Widget _folderRow;

	public CreateModelFromMeshDialog( List<Asset> meshFiles ) : base( null )
	{
		_meshFiles = meshFiles;

		WindowFlags = WindowFlags.Dialog | WindowFlags.Customized | WindowFlags.WindowTitle | WindowFlags.CloseButton | WindowFlags.WindowSystemMenuHint;
		DeleteOnClose = true;
		WindowTitle = meshFiles.Count == 1
			? $"Create Model from {Path.GetFileName( meshFiles[0].AbsolutePath )}"
			: $"Create {meshFiles.Count} Models from Mesh Files";
		SetWindowIcon( "view_in_ar" );

		Layout = Layout.Column();
		Layout.Margin = 16;
		Layout.Spacing = 8;

		if ( meshFiles.Count <= 6 )
		{
			foreach ( var mesh in meshFiles )
			{
				var fileName = Path.GetFileName( mesh.AbsolutePath );
				Layout.Add( new Label( $"  📄 {fileName}" ) { Color = Theme.TextControl.WithAlpha( 0.7f ) } );
			}
		}
		else
		{
			Layout.Add( new Label( $"{meshFiles.Count} mesh files selected" ) { Color = Theme.TextControl.WithAlpha( 0.7f ) } );
		}

		Layout.AddSpacingCell( 4 );

		AddRow( "Collision", _collisionCombo = new ComboBox( this ) );
		_collisionCombo.AddItem( "Convex Hull", icon: "change_history" );
		_collisionCombo.AddItem( "Exact Mesh", icon: "grid_on" );
		_collisionCombo.AddItem( "None", icon: "block" );
		_collisionCombo.CurrentIndex = 0;

		var defaultDir = Path.GetDirectoryName( meshFiles[0].AbsolutePath );
		var defaultFile = Path.ChangeExtension( meshFiles[0].AbsolutePath, ".vmdl" );

		_fileEdit = new LineEdit( this );
		_fileEdit.Text = defaultFile;
		_fileEdit.AddOptionToEnd( new Option( "Browse", "folder", BrowseFile ) );

		_folderEdit = new FolderEdit( this );
		_folderEdit.Text = defaultDir;

		_fileRow = AddRow( "Save To", _fileEdit );
		_folderRow = AddRow( "Save To", _folderEdit );

		_fileRow.Visible = meshFiles.Count == 1;
		_folderRow.Visible = meshFiles.Count > 1;

		var footer = Layout.AddRow();
		footer.Margin = new Sandbox.UI.Margin( 0, 8, 0, 0 );
		footer.AddStretchCell();

		var cancelButton = new Button( "Cancel", "close" );
		cancelButton.Clicked = Close;
		footer.Add( cancelButton );

		footer.AddSpacingCell( 8 );

		var createButton = new Button.Primary( "Create", "add" );
		createButton.Clicked = OnCreate;
		footer.Add( createButton );

		FixedWidth = 420;
		AdjustSize();
		FixedHeight = Height;

		var geo = EditorCookie.GetString( "CreateModelFromMeshDialog.Geometry", null );
		if ( geo is not null )
		{
			RestoreGeometry( geo );
		}
		else
		{
			Position = Application.CursorPosition - new Vector2( Width * 0.5f, 3 );
			ConstrainToScreen();
		}

		Show();
		Focus();
	}

	protected override void OnClosed()
	{
		EditorCookie.SetString( "CreateModelFromMeshDialog.Geometry", SaveGeometry() );
		base.OnClosed();
	}

	Widget AddRow( string label, Widget control )
	{
		var row = new Widget( this );
		row.Layout = Layout.Row();
		row.Layout.Spacing = 8;
		row.Layout.Add( new Label( label ) { MinimumWidth = 90 } );
		row.Layout.Add( control, 1 );
		Layout.Add( row );
		return row;
	}

	void BrowseFile()
	{
		var result = EditorUtility.SaveFileDialog( "Save Model As..", "vmdl", _fileEdit.Text );
		if ( result is not null )
			_fileEdit.Text = result;
	}

	CollisionMode SelectedCollision => _collisionCombo.CurrentIndex switch
	{
		0 => CollisionMode.Hull,
		1 => CollisionMode.Mesh,
		_ => CollisionMode.None,
	};

	void OnCreate()
	{
		Close();

		if ( _meshFiles.Count == 1 )
		{
			var outputPath = _fileEdit.Text;
			if ( string.IsNullOrWhiteSpace( outputPath ) )
				return;

			CreateModel( _meshFiles[0], outputPath );
		}
		else
		{
			var outputFolder = _folderEdit.Text;
			if ( string.IsNullOrWhiteSpace( outputFolder ) )
				return;

			foreach ( var mesh in _meshFiles )
			{
				var outputPath = Path.Combine( outputFolder, Path.ChangeExtension( Path.GetFileName( mesh.AbsolutePath ), ".vmdl" ) );
				if ( File.Exists( outputPath ) )
				{
					Log.Warning( $"Skipping {Path.GetFileName( outputPath )} - already exists" );
					continue;
				}

				CreateModel( mesh, outputPath );
			}
		}
	}

	void CreateModel( Asset mesh, string outputPath )
	{
		if ( !g_pToolFramework2.InitEngineTool( "modeldoc_editor" ) )
			return;

		var document = CModelDoc.Create();

		g_pModelDocUtils.InitFromMesh( document, mesh.Path );

		switch ( SelectedCollision )
		{
			case CollisionMode.Hull:
				document.AddPhysicsHullFromRender();
				break;
			case CollisionMode.Mesh:
				document.AddPhysicsMeshFromRender();
				break;
		}

		document.SaveToFile( outputPath );
		document.DeleteThis();

		var asset = AssetSystem.RegisterFile( outputPath );
		asset?.Compile( true );
	}
}
