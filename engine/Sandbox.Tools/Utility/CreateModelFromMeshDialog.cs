using System;
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
		/// Try generating materials for each material slot in the model. Textures must be named after
		/// the name of a material slot, otherwise it will be left empty and material won't be generated.
		/// Texture name suffix must match whatever name is expected by the shader (_color, _normal, etc..) 
		/// </summary>
		[Property, Title( "Try Generating Materials" )]
		public bool GenerateMaterials { get; set; } = false;

		/// <summary>
		/// Which shader should be used for generated materials
		/// </summary>
		[Property, Title( "Material Shader" ), ResourceType( "shader" )]
		public string MaterialShader { get; set; } = MaterialGenerator.DefaultShader;

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
		// generated materials write their own non-global material group
		if ( options.GenerateMaterials )
			return;

		if ( !options.GlobalMaterialOverride )
			return;

		document.AddDefaultMaterialGroup( options.GlobalMaterial );
	}

	/// <summary>
	/// The shader and suffix map to generate materials with, plus cached discovered texture sets
	/// </summary>
	internal sealed class MaterialGenerationPlan
	{
		public string ShaderPath { get; init; }
		public Dictionary<string, string> SuffixMap { get; init; }

		readonly Dictionary<string, List<MaterialGenerator.TextureSet>> _setsByRoot = new( StringComparer.OrdinalIgnoreCase );

		// returns null if material generation is disabled, or the shader has no usable texture inputs
		public static MaterialGenerationPlan Create( ImportOptions options )
		{
			if ( !options.GenerateMaterials )
				return null;

			var shaderPath = string.IsNullOrWhiteSpace( options.MaterialShader )
				? MaterialGenerator.DefaultShader
				: options.MaterialShader;

			var suffixMap = MaterialGenerator.GetSuffixMap( shaderPath );
			if ( suffixMap is null || suffixMap.Count == 0 )
				return null;

			return new MaterialGenerationPlan { ShaderPath = shaderPath, SuffixMap = suffixMap };
		}

		public List<MaterialGenerator.TextureSet> SetsFor( string searchRoot )
		{
			if ( _setsByRoot.TryGetValue( searchRoot, out var sets ) )
				return sets;

			sets = MaterialGenerator.DiscoverTextureSets( searchRoot, SuffixMap.Keys );
			_setsByRoot[searchRoot] = sets;
			return sets;
		}
	}

	/// <summary>
	/// Generate a material for each of the model's material slots that has matching textures, and
	/// remap the slot to it. Slots with no match are left empty, user should fix them themselves
	/// </summary>
	internal static void ApplyGeneratedMaterials( MaterialGenerationPlan plan, CModelDoc document, string outputPath )
	{
		if ( plan is null )
			return;

		var slots = document.GetInputMaterials()
			?.Split( '\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries );

		if ( slots is null || slots.Length == 0 )
			return;

		// attach the group upfront so the model always has one to autofill into
		var materialGroup = document.FindOrCreateDefaultMaterialGroup();
		if ( !materialGroup.IsValid )
			return;

		// explicit remaps and a global override are mutually exclusive
		materialGroup.SetHasGlobalDefault( false );

		var sets = plan.SetsFor( Path.GetDirectoryName( outputPath ) );

		foreach ( var slot in slots )
		{
			if ( materialGroup.FindReplacementIndex( slot ) != -1 )
				continue;

			var set = MaterialGenerator.FindSetForSlot( sets, slot );
			if ( set is null )
				continue;

			var materialPath = MaterialGenerator.GetOrCreateMaterial( set, plan.ShaderPath, plan.SuffixMap );
			if ( materialPath is null )
				continue;

			materialGroup.AddRemap( slot, materialPath );
		}
	}

	/// <summary>
	/// Measures the total height of conditional rows and shows/hides them so dialog window size
	/// stays correct regardless of which properties are visible
	/// </summary>
	internal sealed class ConditionalRowGroup
	{
		readonly Widget _dialog;
		readonly List<(Widget Row, Func<bool> IsVisible)> _rows = [];
		readonly Dictionary<int, float> _heightForMask = [];
		bool _measured;

		public ConditionalRowGroup( Widget dialog )
		{
			_dialog = dialog;
		}

		public void Add( Widget row, Func<bool> isVisible )
		{
			if ( row is not null )
				_rows.Add( (row, isVisible) );
		}

		public void Measure()
		{
			if ( _rows.Count == 0 )
			{
				_dialog.AdjustSize();
				_dialog.FixedHeight = _dialog.Height;
				return;
			}

			for ( var mask = 0; mask < 1 << _rows.Count; mask++ )
			{
				Apply( mask );
				_dialog.AdjustSize();
				_heightForMask[mask] = _dialog.Height;
			}

			_measured = true;
			Update();
		}

		public void Update()
		{
			var mask = 0;
			for ( var i = 0; i < _rows.Count; i++ )
			{
				if ( _rows[i].IsVisible() )
					mask |= 1 << i;
			}

			Apply( mask );

			if ( _measured && _heightForMask.TryGetValue( mask, out var height ) )
				_dialog.FixedHeight = height;
		}

		void Apply( int mask )
		{
			for ( var i = 0; i < _rows.Count; i++ )
			{
				_rows[i].Row.Hidden = (mask & (1 << i)) == 0;
			}
		}
	}

	const string OptionsCookie = "CreateModelFromMeshDialog.ImportOptions";

	readonly List<Asset> _meshFiles;
	readonly ImportOptions _options;
	readonly LineEdit _fileEdit;
	readonly FolderEdit _folderEdit;
	readonly Widget _fileRow;
	readonly Widget _folderRow;
	readonly ConditionalRowGroup _conditionalRows;
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

		_conditionalRows = new ConditionalRowGroup( this );

		foreach ( var prop in _serializedOptions )
		{
			var row = AddRow( prop.DisplayName, ControlWidget.Create( prop ) );

			switch ( prop.Name )
			{
				case nameof( ImportOptions.MaterialShader ):
					_conditionalRows.Add( row, () => _options.GenerateMaterials );
					break;

				case nameof( ImportOptions.GlobalMaterialOverride ):
					_conditionalRows.Add( row, () => !_options.GenerateMaterials );
					break;

				case nameof( ImportOptions.GlobalMaterial ):
					_conditionalRows.Add( row, () => !_options.GenerateMaterials && _options.GlobalMaterialOverride );
					break;
			}
		}

		_serializedOptions.OnPropertyChanged += _ => _conditionalRows.Update();

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

		_conditionalRows.Measure();

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

		// shared across every model in this batch so the asset scan and any materials we create is reused
		var plan = MaterialGenerationPlan.Create( _options );

		if ( _meshFiles.Count == 1 )
		{
			var outputPath = _fileEdit.Text;
			if ( string.IsNullOrWhiteSpace( outputPath ) )
				return;

			CreateModel( _meshFiles[0], outputPath, plan );
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

				CreateModel( mesh, outputPath, plan );
			}
		}
	}

	void CreateModel( Asset mesh, string outputPath, MaterialGenerationPlan plan )
	{
		if ( !g_pToolFramework2.InitEngineTool( "modeldoc_editor" ) )
			return;

		var document = CModelDoc.Create();

		g_pModelDocUtils.InitFromMesh( document, mesh.Path );

		ApplyMaterialOverride( _options, document );
		ApplyGeneratedMaterials( plan, document, outputPath );

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
