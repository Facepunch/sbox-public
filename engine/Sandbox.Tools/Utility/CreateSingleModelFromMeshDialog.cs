using System;
using System.IO;

namespace Editor;

/// <summary>
/// Popup dialog for combining multiple mesh files into a single .vmdl. 
/// Mesh files are grouped by their suffix: _lod/_hull/_col
/// </summary>
public class CreateSingleModelFromMeshDialog : Widget
{
	const int MaxLodLevels = 8;

	enum NodeRole
	{
		RenderMesh,
		Lod,
		Collision,
		Hull,
	}

	class ParsedMesh
	{
		public Asset Asset;
		public NodeRole Role;
		public int LodIndex = -1;
		public string BaseName;
		public string RoleLabel;
		public bool Skipped;
		public string SkipReason;
	}

	// parse mesh names by their suffixes and assign their roles (render mesh/LOD/collision mesh or hull)
	static ParsedMesh Parse( Asset asset )
	{
		var meshFilename = Path.GetFileNameWithoutExtension( asset.AbsolutePath );

		var lodIndex = meshFilename.LastIndexOf( "_lod", StringComparison.OrdinalIgnoreCase );
		if ( lodIndex > 0 )
		{
			var digits = meshFilename.Substring( lodIndex + 4 );
			if ( int.TryParse( digits, out var lod ) && lod >= 0 && lod < MaxLodLevels )
			{
				var baseName = meshFilename.Substring( 0, lodIndex );
				return new ParsedMesh { Asset = asset, Role = NodeRole.Lod, LodIndex = lod, BaseName = baseName, RoleLabel = $"LOD {lod}" };
			}
		}

		if ( TryStripSuffix( meshFilename, "_col", out var colBase ) )
			return new ParsedMesh { Asset = asset, Role = NodeRole.Collision, BaseName = colBase, RoleLabel = "Collision Mesh" };

		if ( TryStripSuffix( meshFilename, "_hull", out var hullBase ) )
			return new ParsedMesh { Asset = asset, Role = NodeRole.Hull, BaseName = hullBase, RoleLabel = "Collision Hull" };

		return new ParsedMesh { Asset = asset, Role = NodeRole.RenderMesh, BaseName = meshFilename, RoleLabel = "Render Mesh" };
	}

	static bool TryStripSuffix( string stem, string suffix, out string baseName )
	{
		if ( stem.Length > suffix.Length && stem.EndsWith( suffix, StringComparison.OrdinalIgnoreCase ) )
		{
			baseName = stem.Substring( 0, stem.Length - suffix.Length );
			return true;
		}

		baseName = null;
		return false;
	}

	const string OptionsCookie = "CreateSingleModelFromMeshDialog.ImportOptions";

	readonly List<ParsedMesh> _parsed;
	readonly CreateModelFromMeshDialog.ImportOptions _options;
	readonly CreateModelFromMeshDialog.MaterialOverrideRow _materialRow;
	readonly SerializedObject _serializedOptions;
	readonly LineEdit _fileEdit;

	public CreateSingleModelFromMeshDialog( List<Asset> meshFiles ) : base( null )
	{
		_parsed = meshFiles.Select( Parse ).ToList();

		// make sure that only one file can claim each LOD level, first one takes place, others are skipped
		var seenLods = new HashSet<int>();
		foreach ( var mesh in _parsed )
		{
			if ( mesh.Role != NodeRole.Lod )
				continue;

			if ( !seenLods.Add( mesh.LodIndex ) )
			{
				mesh.Skipped = true;
				mesh.SkipReason = $"LOD {mesh.LodIndex} is already assigned";
			}
		}

		WindowFlags = WindowFlags.Dialog | WindowFlags.Customized | WindowFlags.WindowTitle | WindowFlags.CloseButton | WindowFlags.WindowSystemMenuHint;
		DeleteOnClose = true;
		WindowTitle = $"Create Single Model from {meshFiles.Count} Files";
		SetWindowIcon( "view_in_ar" );

		Layout = Layout.Column();
		Layout.Margin = 16;
		Layout.Spacing = 8;

		// list for all selected meshes
		var scroller = new ScrollArea( this );
		scroller.FixedHeight = 86;
		scroller.HorizontalScrollbarMode = ScrollbarMode.Off;
		scroller.SetStyles( $"background-color: {Theme.TabBarBackground};" );
		scroller.Canvas = new Widget( this ) { Layout = Layout.Column() };
		scroller.Canvas.Layout.Spacing = 4;
		scroller.Canvas.SetStyles( $"background-color: {Theme.TabBarBackground};" );

		foreach ( var mesh in _parsed )
		{
			var fileName = Path.GetFileName( mesh.Asset.AbsolutePath );
			var label = mesh.Skipped
				? $"  📄 {fileName} - skipped ({mesh.SkipReason})"
				: $"  📄 {fileName} → {mesh.RoleLabel}";
			scroller.Canvas.Layout.Add( new Label( label ) { Color = mesh.Skipped ? Theme.Yellow : Theme.TextControl.WithAlpha( 0.7f ) } );
		}

		scroller.Canvas.Layout.AddStretchCell();
		Layout.Add( scroller );

		Layout.AddSpacingCell( 4 );

		if ( _parsed.Any( m => m.Role is NodeRole.Collision or NodeRole.Hull ) )
		{
			Layout.Add( new Label( "Collision mesh found - collision option below is ignored" ) { Color = Theme.Green.WithAlpha( 0.5f ) } );
		}

		_options = EditorCookie.Get( OptionsCookie, new CreateModelFromMeshDialog.ImportOptions() );

		_serializedOptions = _options.GetSerialized();

		foreach ( var prop in _serializedOptions )
		{
			var row = AddRow( prop.DisplayName, ControlWidget.Create( prop ) );

			if ( prop.Name == nameof( CreateModelFromMeshDialog.ImportOptions.GlobalMaterial ) )
				_materialRow = new CreateModelFromMeshDialog.MaterialOverrideRow( this, row );
		}

		_serializedOptions.OnPropertyChanged += _ => _materialRow?.Update( _options.GlobalMaterialOverride );

		var defaultBase = _parsed.FirstOrDefault( m => !m.Skipped )?.BaseName ?? Path.GetFileNameWithoutExtension( meshFiles[0].AbsolutePath );
		var defaultDir = Path.GetDirectoryName( meshFiles[0].AbsolutePath );
		var defaultFile = Path.Combine( defaultDir, defaultBase + ".vmdl" );

		_fileEdit = new LineEdit( this );
		_fileEdit.Text = defaultFile;
		_fileEdit.AddOptionToEnd( new Option( "Browse", "folder", BrowseFile ) );

		AddRow( "Save To", _fileEdit );

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

		var geo = EditorCookie.GetString( "CreateSingleModelFromMeshDialog.Geometry", null );
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
		EditorCookie.SetString( "CreateSingleModelFromMeshDialog.Geometry", SaveGeometry() );
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

		var outputPath = _fileEdit.Text;
		if ( string.IsNullOrWhiteSpace( outputPath ) )
			return;

		if ( !g_pToolFramework2.InitEngineTool( "modeldoc_editor" ) )
			return;

		var document = CModelDoc.Create();

		bool hasExplicitCollision = false;

		foreach ( var mesh in _parsed )
		{
			if ( mesh.Skipped )
				continue;

			switch ( mesh.Role )
			{
				case NodeRole.Lod:
					document.AddRenderMeshFile( mesh.Asset.Path, mesh.LodIndex );
					break;

				case NodeRole.RenderMesh:
					document.AddRenderMeshFile( mesh.Asset.Path, -1 );
					break;

				case NodeRole.Collision:
					document.AddPhysicsMeshFile( mesh.Asset.Path );
					hasExplicitCollision = true;
					break;

				case NodeRole.Hull:
					document.AddPhysicsHullFile( mesh.Asset.Path );
					hasExplicitCollision = true;
					break;
			}
		}

		CreateModelFromMeshDialog.ApplyMaterialOverride( _options, document );

		if ( _options.ImportScale > 0.0f && !_options.ImportScale.AlmostEqual( 1.0f ) )
		{
			document.SetImportScale( _options.ImportScale );
		}

		// generate collider from render according to selected options only if no explicit collision meshes were found
		if ( !hasExplicitCollision )
		{
			switch ( _options.Collision )
			{
				case CreateModelFromMeshDialog.CollisionMode.Hull:
					document.AddPhysicsHullFromRender();
					break;
				case CreateModelFromMeshDialog.CollisionMode.Mesh:
					document.AddPhysicsMeshFromRender();
					break;
			}
		}

		document.SaveToFile( outputPath );
		document.DeleteThis();

		var asset = AssetSystem.RegisterFile( outputPath );
		asset?.Compile( true );
	}
}
