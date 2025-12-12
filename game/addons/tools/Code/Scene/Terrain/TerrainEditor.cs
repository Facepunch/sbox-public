using Sandbox.UI;

namespace Editor.TerrainEditor;

/// <summary>
/// Modify terrains
/// </summary>
[EditorTool]
[Title( "Terrain" )]
[Icon( "landscape" )]
[Alias( "terrain" )]
[Group( "Scene" )]
public class TerrainEditorTool : EditorTool
{
	public override IEnumerable<EditorTool> GetSubtools()
	{
		yield return new RaiseLowerTool( this );
		yield return new PaintTextureTool( this );
		yield return new FlattenTool( this );
		yield return new SmoothTool( this );
		yield return new HoleTool( this );
		yield return new NoiseTool( this );
		// yield return new SetHeightTool();
	}

	public static BrushList BrushList { get; set; } = new();
	public static Brush Brush => BrushList.Selected;
	public BrushSettings BrushSettings { get; private set; } = new();

	BrushPreviewSceneObject _previewObject;

	public override void OnEnabled()
	{
		AllowGameObjectSelection = false;

		// if we don't have a terrain already selected.. just grab one
		var selectedTerrain = GetSelectedComponent<Terrain>();
		if ( !selectedTerrain.IsValid() )
		{
			Selection.Clear();
			var first = Scene.GetAllComponents<Terrain>().FirstOrDefault();
			if ( first.IsValid() ) Selection.Add( first.GameObject );
		}
	}

	public override void OnDisabled()
	{
		_previewObject?.Delete();
		_previewObject = null;
	}

	/// <summary>
	/// Create the sidebar widget for the terrain tool
	/// </summary>
	public override Widget CreateToolSidebar()
	{
		var sidebar = new ToolSidebarWidget();
		sidebar.AddTitle( "Brush Settings", "brush" );

		// Brush Type
		{
			var group = sidebar.AddGroup( "Brush Type" );

			group.Add( new BrushPreviewWidget( sidebar ) );
		}

		// Brush Properties
		{
			var group = sidebar.AddGroup( "Brush Properties" );

			var so = BrushSettings.GetSerialized();
			group.Add( ControlSheetRow.Create( so.GetProperty( nameof( BrushSettings.Size ) ) ) );
			group.Add( ControlSheetRow.Create( so.GetProperty( nameof( BrushSettings.Opacity ) ) ) );
		}

		// Active Layer
		{
			var group = sidebar.AddGroup( "Active Layer" );

			var tabs = new TabWidget( sidebar );
			tabs.MaximumHeight = 40;
			tabs.StateCookie = "TerrainEditorTool.Tabs";

			// Create named pages so we can hook into tab changes
			var basePage = new Widget();
			var overlayPage = new Widget();

			tabs.AddPage( "Base", "layers", basePage );
			tabs.AddPage( "Overlay", "landscape", overlayPage );

			// Hook into tab selection changes
			var tabBar = tabs.Children.OfType<SegmentedControl>().FirstOrDefault();
			tabBar.OnSelectedChanged += ( selectedName ) =>
			{
				PaintTextureTool.ActiveLayer = selectedName == "Base" ? TerrainLayer.Base : TerrainLayer.Overlay;
			};

			group.Add( tabs );
		}

		// Material selection
		var terrain = GetSelectedComponent<Terrain>();
		if ( terrain.IsValid() )
		{
			var group = sidebar.AddGroup( "Materials", SizeMode.Flexible );

			var materialList = new TerrainMaterialList( sidebar, terrain );
			materialList.ItemSize += 24;
			materialList.BuildItems();
			group.Add( materialList );

			var hlayout = group.AddRow();
			hlayout.Spacing = 8;
			hlayout.AddStretchCell();

			var newTerrainMat = new Button( "New Terrain Material" );
			newTerrainMat.Clicked += () => NewTerrainMaterial( terrain, materialList );

			var cloudMats = new Button( "Browse", "cloud" );
			cloudMats.Clicked += () =>
			{
				var picker = AssetPicker.Create( null, AssetType.FromExtension( "tmat" ) );
				picker.OnAssetPicked = x =>
				{
					var material = x.First().LoadResource<TerrainMaterial>();
					terrain.Storage.Materials.Add( material );
					terrain.UpdateMaterialsBuffer();
					materialList?.BuildItems();
				};
				picker.Show();
			};

			hlayout.Add( cloudMats );
			hlayout.Add( newTerrainMat, 1 );
		}
		else
		{
			sidebar.Layout.AddStretchCell();
		}

		return sidebar;
	}

	static void NewTerrainMaterial( Terrain terrain, TerrainMaterialList materialList )
	{
		var filepath = EditorUtility.SaveFileDialog( "Create Terrain Material", "tmat", $"{Project.Current.GetAssetsPath()}/" );
		if ( filepath is null ) return;

		var asset = AssetSystem.CreateResource( "tmat", filepath );

		if ( !asset.TryLoadResource<TerrainMaterial>( out var material ) )
			return;

		asset.Compile( true );
		MainAssetBrowser.Instance?.Local.UpdateAssetList();

		terrain.Storage.Materials.Add( material );
		terrain.UpdateMaterialsBuffer();
		materialList.BuildItems();

		asset.OpenInEditor();
	}

	public void DrawBrushPreview( Transform transform )
	{
		_previewObject ??= new BrushPreviewSceneObject( Gizmo.World ); // Not cached, FindOrCreate is internal :x

		var color = Color.FromBytes( 150, 150, 250 );

		if ( Application.KeyboardModifiers.HasFlag( Sandbox.KeyboardModifiers.Ctrl ) )
			color = color.AdjustHue( 90 );

		color.a = BrushSettings.Opacity;

		_previewObject.RenderLayer = SceneRenderLayer.OverlayWithDepth;
		_previewObject.Bounds = BBox.FromPositionAndSize( 0, float.MaxValue );
		_previewObject.Transform = transform;
		_previewObject.Radius = BrushSettings.Size;
		_previewObject.Texture = Brush.Texture;
		_previewObject.Color = color;
	}

	[Event( "scene.saved" )]
	static void OnSceneSaved( Scene scene )
	{
		foreach ( var terrain in scene.Components.GetAll<Terrain>( FindMode.EverythingInDescendants ) )
		{
			if ( terrain.Storage is null ) continue;

			var asset = AssetSystem.FindByPath( terrain.Storage.ResourcePath );
			asset?.SaveToDisk( terrain.Storage );
		}
	}
}

/// <summary>
/// Widget that shows the currently selected brush and opens a popup picker
/// </summary>
internal class BrushPreviewWidget : Widget
{
	public BrushPreviewWidget( Widget parent ) : base( parent )
	{
		MinimumSize = new( 32, 32 );
		MaximumSize = new( 48, 48 );

		Cursor = CursorShape.Finger;
	}

	protected override void OnPaint()
	{
		base.OnPaint();

		Paint.Antialiasing = true;
		Paint.ClearPen();
		Paint.DrawRect( LocalRect );

		var pixmap = TerrainEditorTool.Brush.Pixmap;
		if ( pixmap != null )
		{
			Paint.Draw( LocalRect.Contain( pixmap.Size ), pixmap );
		}
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		var popup = new PopupWidget( null );
		popup.Position = Application.CursorPosition;
		popup.Visible = true;
		popup.Layout = Layout.Column();
		popup.Layout.Margin = 10;
		popup.MaximumSize = new Vector2( 300, 150 );

		var list = new BrushListWidget();
		list.BrushSelected += () =>
		{
			popup.Close();
			Update();
		};
		popup.Layout.Add( list );
	}

	[EditorEvent.Frame]
	public void UpdatePreview()
	{
		Update();
	}
}

/// <summary>
/// Button that shows the currently selected material and opens a popup picker
/// </summary>
internal class TerrainMaterialButton : Widget
{
	Terrain Terrain;

	public TerrainMaterialButton( Widget parent, Terrain terrain ) : base( parent )
	{
		Terrain = terrain;
		MinimumSize = new( 200, 64 );
		Cursor = CursorShape.Finger;
	}

	protected override void OnPaint()
	{
		base.OnPaint();

		Paint.Antialiasing = true;
		Paint.ClearPen();
		Paint.SetBrush( Theme.ControlBackground );
		Paint.DrawRect( LocalRect, Theme.ControlRadius );

		var selectedIndex = PaintTextureTool.SplatChannel;
		if ( selectedIndex >= 0 && selectedIndex < Terrain.Storage?.Materials?.Count )
		{
			var material = Terrain.Storage.Materials[selectedIndex];
			var asset = AssetSystem.FindByPath( material.ResourcePath );

			if ( asset is not null )
			{
				// Draw thumbnail on left side
				var thumbRect = new Rect( LocalRect.Left + 4, LocalRect.Top + 4, 56, 56 );
				var pixmap = asset.GetAssetThumb();
				Paint.Draw( thumbRect, pixmap );

				// Draw index badge
				var badgeRect = new Rect( thumbRect.Left + 2, thumbRect.Top + 2, 20, 20 );
				Paint.SetBrush( Theme.ControlBackground.WithAlpha( 0.8f ) );
				Paint.DrawRect( badgeRect, 4 );

				Paint.SetPen( Theme.Primary );
				Paint.SetDefaultFont( 9, 600 );
				Paint.DrawText( badgeRect, $"{selectedIndex}", TextFlag.Center );

				// Draw material name on right side
				var textRect = new Rect( thumbRect.Right + 8, LocalRect.Top, LocalRect.Right - thumbRect.Right - 12, LocalRect.Height );
				Paint.SetPen( Theme.Text );
				Paint.SetDefaultFont( 11 );
				Paint.DrawText( textRect, material.ResourceName, TextFlag.LeftCenter );
			}
		}
		else
		{
			// No material selected
			Paint.SetPen( Theme.Text.WithAlpha( 0.5f ) );
			Paint.SetDefaultFont( 11 );
			Paint.DrawText( LocalRect, "Click to select material", TextFlag.Center );
		}
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		if ( Terrain?.Storage?.Materials is null )
			return;

		var popup = new PopupWidget( null );
		popup.Position = Application.CursorPosition;
		popup.Visible = true;
		popup.Layout = Layout.Column();
		popup.Layout.Margin = 10;
		popup.Layout.Spacing = 4;
		popup.MinimumSize = new Vector2( 300, 300 );
		popup.MaximumSize = new Vector2( 400, 600 );

		var materialGrid = new TerrainMaterialGridView( popup, Terrain );
		materialGrid.ItemSelected += ( obj ) =>
		{
			popup.Close();
			Update();
		};
		popup.Layout.Add( materialGrid, 1 );
	}

	[EditorEvent.Frame]
	public void UpdatePreview()
	{
		Update();
	}
}

/// <summary>
/// Grid view for terrain materials in the sidebar
/// </summary>
internal class TerrainMaterialGridView : ListView
{
	Terrain Terrain;

	public TerrainMaterialGridView( Widget parent, Terrain terrain ) : base( parent )
	{
		Terrain = terrain;

		ItemSelected = OnItemClicked;
		ItemActivated = OnItemDoubleClicked;
		ItemSpacing = 4;
		MinimumHeight = 200;

		ItemSize = new Vector2( 68, 68 + 16 );
		ItemAlign = Sandbox.UI.Align.FlexStart;

		BuildItems();
		UpdateSelection();
	}

	protected void OnItemClicked( object value )
	{
		if ( value is not TerrainMaterial material )
			return;

		PaintTextureTool.SplatChannel = Terrain.Storage.Materials.IndexOf( material );
	}

	protected void OnItemDoubleClicked( object obj )
	{
		if ( obj is not TerrainMaterial entry ) return;
		var asset = AssetSystem.FindByPath( entry.ResourcePath );
		asset?.OpenInEditor();
	}

	public void BuildItems()
	{
		if ( Terrain?.Storage?.Materials is null )
			return;

		SetItems( Terrain.Storage.Materials.Cast<object>().ToList() );
	}

	protected override void PaintItem( VirtualWidget item )
	{
		var rect = item.Rect.Shrink( 0, 0, 0, 16 );

		if ( item.Object is not TerrainMaterial material )
			return;

		var asset = AssetSystem.FindByPath( material.ResourcePath );

		if ( asset is null )
		{
			Paint.SetDefaultFont();
			Paint.SetPen( Color.Red );
			Paint.DrawText( item.Rect.Shrink( 2 ), "<ERROR>", TextFlag.Center );
			return;
		}

		// Draw selection/hover background
		if ( item.Selected || Paint.HasMouseOver )
		{
			Paint.SetBrush( Theme.Blue.WithAlpha( item.Selected ? 0.5f : 0.2f ) );
			Paint.ClearPen();
			Paint.DrawRect( item.Rect, 4 );
		}

		// Draw thumbnail
		var pixmap = asset.GetAssetThumb();
		Paint.Draw( rect.Shrink( 2 ), pixmap );

		// Draw index badge in top-left corner
		var itemIndex = Terrain.Storage.Materials.IndexOf( material );
		var badgeRect = new Rect( item.Rect.Left + 4, item.Rect.Top + 4, 24, 24 );
		Paint.SetBrush( Theme.ControlBackground.WithAlpha( 0.8f ) );
		Paint.DrawRect( badgeRect, 4 );

		Paint.SetPen( item.Selected ? Theme.Primary : Theme.Text );
		Paint.SetDefaultFont( 10, 600 );
		Paint.DrawText( badgeRect, $"{itemIndex}", TextFlag.Center );

		// Draw material name at bottom
		Paint.SetDefaultFont();
		Paint.SetPen( Theme.Text );
		Paint.DrawText( item.Rect.Shrink( 2 ), material.ResourceName, TextFlag.CenterBottom );
	}

	protected override void OnPaint()
	{
		Paint.ClearPen();
		Paint.SetBrush( Theme.ControlBackground );
		Paint.DrawRect( LocalRect, 4 );

		base.OnPaint();
	}

	// Update selection when PaintTextureTool.SplatChannel changes
	[EditorEvent.Frame]
	public void UpdateSelection()
	{
		if ( Terrain?.Storage?.Materials is null )
			return;

		var selectedIndex = PaintTextureTool.SplatChannel;
		if ( selectedIndex >= 0 && selectedIndex < Terrain.Storage.Materials.Count )
		{
			var material = Terrain.Storage.Materials[selectedIndex];
			SelectItem( material );
		}
	}
}
