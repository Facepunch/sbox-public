using System.IO;
using System.Text.Json.Serialization;

namespace Editor;

/// <summary>
/// A popup dialog for creating models from mesh files (.fbx, .obj, .dmx).
/// Lets you configure collision type and output path.
/// </summary>
public class CreateModelFromMeshDialog : Widget
{
	public enum CollisionMode
	{
		[Icon( "change_history" ), Title( "Convex Hull" )]
		Hull,
		[Icon( "grid_on" ), Title( "Exact Mesh" )]
		Mesh,
		[Icon( "block" )]
		None,
	}

	public enum ScaleUnit
	{
		Inches,
		Feet,
		Meters,
		Centimeters,
		Millimeters,
		Custom,
	}

	/// <summary>
	/// Import options, remembered between uses. Add a property here to add a setting to the dialog.
	/// </summary>
	public class ImportOptions
	{
		[Property, Title( "Collision" )]
		public CollisionMode Collision { get; set; } = CollisionMode.Hull;

		/// <summary>
		/// Unit presets, derived from <see cref="ImportScale"/> - same conversions ModelDoc uses.
		/// </summary>
		[Property, Title( "Units" ), JsonIgnore]
		public ScaleUnit Units
		{
			get => ImportScale switch
			{
				1.0f => ScaleUnit.Inches,
				12.0f => ScaleUnit.Feet,
				39.37f => ScaleUnit.Meters,
				0.3937f => ScaleUnit.Centimeters,
				0.03937f => ScaleUnit.Millimeters,
				_ => ScaleUnit.Custom,
			};
			set => ImportScale = value switch
			{
				ScaleUnit.Inches => 1.0f,
				ScaleUnit.Feet => 12.0f,
				ScaleUnit.Meters => 39.37f,
				ScaleUnit.Centimeters => 0.3937f,
				ScaleUnit.Millimeters => 0.03937f,
				_ => ImportScale,
			};
		}

		[Property, Title( "Import Scale" ), Range( 0.001f, 1000.0f, slider: false )]
		public float ImportScale { get; set; } = 1.0f;

		/// <summary>
		/// Add a material group that overrides every material on the model
		/// </summary>
		[Property, Title( "Globally Override Materials" )]
		public bool GlobalMaterialOverride { get; set; } = true;

		/// <summary>
		/// Material to override every material on the model with. Leave empty to use a default placeholder white material
		/// </summary>
		[Property, Title( "Override Material" ), ResourceType( "vmat" )]
		public string GlobalMaterial { get; set; }
	}

	internal static void ApplyMaterialOverride( ImportOptions options, CModelDoc document )
	{
		if ( !options.GlobalMaterialOverride )
			return;

		document.AddDefaultMaterialGroup( options.GlobalMaterial );
	}

	internal class MaterialOverrideRow
	{
		readonly Widget _dialog;
		readonly Widget _row;
		float _expandedHeight;
		float _collapsedHeight;
		bool _visible = true;

		public MaterialOverrideRow( Widget dialog, Widget row )
		{
			_dialog = dialog;
			_row = row;
		}

		/// <summary>
		/// Recalculate window height after material override row is shown or hidden
		/// </summary>
		public void Measure( bool visible )
		{
			_dialog.AdjustSize();
			_expandedHeight = _dialog.Height;

			_row.Hidden = true;
			_dialog.AdjustSize();
			_collapsedHeight = _dialog.Height;

			_visible = visible;
			_row.Hidden = !visible;
			_dialog.FixedHeight = visible ? _expandedHeight : _collapsedHeight;
		}

		public void Update( bool visible )
		{
			if ( _visible == visible )
				return;

			_visible = visible;
			_row.Hidden = !visible;
			_dialog.FixedHeight = visible ? _expandedHeight : _collapsedHeight;
		}
	}

	const string OptionsCookie = "CreateModelFromMeshDialog.ImportOptions";

	readonly List<Asset> _meshFiles;
	readonly ImportOptions _options;
	readonly LineEdit _fileEdit;
	readonly FolderEdit _folderEdit;
	readonly Widget _fileRow;
	readonly Widget _folderRow;
	readonly MaterialOverrideRow _materialRow;
	readonly SerializedObject _serializedOptions;

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

		_options = EditorCookie.Get( OptionsCookie, new ImportOptions() );

		_serializedOptions = _options.GetSerialized();

		foreach ( var prop in _serializedOptions )
		{
			var row = AddRow( prop.DisplayName, ControlWidget.Create( prop ) );

			if ( prop.Name == nameof( ImportOptions.GlobalMaterial ) )
				_materialRow = new MaterialOverrideRow( this, row );
		}

		_serializedOptions.OnPropertyChanged += _ => _materialRow?.Update( _options.GlobalMaterialOverride );

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

		if ( _materialRow is not null )
		{
			_materialRow.Measure( _options.GlobalMaterialOverride );
		}
		else
		{
			AdjustSize();
			FixedHeight = Height;
		}

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

	void OnCreate()
	{
		EditorCookie.Set( OptionsCookie, _options );

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

		ApplyMaterialOverride( _options, document );

		if ( _options.ImportScale > 0.0f && !_options.ImportScale.AlmostEqual( 1.0f ) )
		{
			document.SetImportScale( _options.ImportScale );
		}

		switch ( _options.Collision )
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
