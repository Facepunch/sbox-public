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
public partial class TerrainEditorTool : EditorTool
{
	public override IEnumerable<EditorTool> GetSubtools()
	{
		yield return new RaiseLowerTool( this );
		yield return new SmoothTool( this );
		yield return new FlattenTool( this );
		yield return new NoiseTool( this );
		yield return new PaintTextureTool( this );
		yield return new HoleTool( this );
		// yield return new SetHeightTool();
	}

	public static BrushList BrushList { get; set; } = new();
	public static Brush Brush => BrushList.Selected;
	public BrushSettings BrushSettings { get; private set; } = new();

	BrushPreviewSceneObject _previewObject;
	public override Widget CreateShortcutsWidget() => new TerrainToolShortcutsWidget();

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

		LoadToolbarCookies();
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

		var info = CurrentTool is not null ? DisplayInfo.For( CurrentTool ) : default;
		var title = string.IsNullOrEmpty( info.Name ) ? "Brush Settings" : info.Name;
		var icon = string.IsNullOrEmpty( info.Icon ) ? "brush" : info.Icon;
		sidebar.AddTitle( title, icon );

		// Brush Type
		{
			var group = sidebar.AddGroup( "Brush Type" );
			group.Add( new BrushPreviewWidget( sidebar ) );
		}

		// Brush Properties
		ControlSheetRow opacityRow;
		{
			var group = sidebar.AddGroup( "Brush Properties" );

			var so = BrushSettings.GetSerialized();
			group.Add( ControlSheetRow.Create( so.GetProperty( nameof( BrushSettings.Size ) ) ) );

			opacityRow = ControlSheetRow.Create( so.GetProperty( nameof( BrushSettings.Opacity ) ) );
			group.Add( opacityRow );
			var rot = group.Add( ControlSheetRow.Create( so.GetProperty( nameof( BrushSettings.Rotation ) ) ) );
			rot.Enabled = !BrushSettings.RandomRotation;
			var rndRot = group.Add( ControlSheetRow.Create( so.GetProperty( nameof( BrushSettings.RandomRotation ) ) ) );

			so.OnPropertyChanged += ( prop ) =>
			{
				if ( prop?.Name == nameof( BrushSettings.RandomRotation ) && rot.IsValid() )
					rot.Enabled = !BrushSettings.RandomRotation;
			};
		}

		// Paint-only sections — hidden unless PaintTextureTool is active
		var paintOnlySection = new PaintOnlySectionWidget( this, opacityRow );
		{
			// Material selection
			var terrain = GetSelectedComponent<Terrain>() ?? Scene.Get<Terrain>();

			if ( terrain.IsValid() )
			{
				var group = ToolSidebarWidget.CreateGroupWidget( "Materials", SizeMode.Flexible );
				paintOnlySection.Layout.Add( group );

				var materialList = new TerrainMaterialList( paintOnlySection, terrain );
				materialList.ItemSize += 24;
				materialList.BuildItems();
				group.ContentLayout.Add( materialList );

				var hlayout = group.ContentLayout.AddRow();
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

			sidebar.Layout.Add( paintOnlySection );
		}

		sidebar.Layout.AddStretchCell();
		SetOpacityToolTip( opacityRow );

		sidebar.AddShortcuts(
			("Adjust Size", "Shift+MMB ↔"),
			("Adjust Opacity", "Shift+MMB ↕"),
			("Rotate", "CTRL+MMB ↔")
			);

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

	internal static void SetOpacityToolTip( ControlSheetRow row )
	{
		var tip = "Controls how strongly the selected material is painted";
		row.Enabled = true;
		row.ToolTip = tip;
		row.ControlWidget.ToolTip = tip;
	}

	public void DrawBrushPreview( Transform transform, Terrain terrain = null )
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
		_previewObject.BrushRotation = BrushSettings.Rotation;

		if ( terrain?.Storage is not null )
		{
			var tx = terrain.WorldTransform;
			_previewObject.CellSize = terrain.Storage.TerrainSize / terrain.Storage.Resolution;
			_previewObject.TerrainOrigin = tx.Position;
			_previewObject.TerrainRight = tx.Rotation.Right;
			_previewObject.TerrainForward = tx.Rotation.Forward;
		}
		else
		{
			_previewObject.CellSize = 0f;
		}
	}

	public void DrawSimpleBrushPreview( Vector3 worldPos, Transform tx, Terrain terrain = null )
	{
		var color = Color.FromBytes( 150, 150, 250 );
		if ( Application.KeyboardModifiers.HasFlag( Sandbox.KeyboardModifiers.Ctrl ) )
			color = color.AdjustHue( 90 );

		if ( terrain?.Storage is not null )
		{
			_previewObject ??= new BrushPreviewSceneObject( Gizmo.World );
			_previewObject.RenderLayer = SceneRenderLayer.OverlayWithDepth;
			_previewObject.Bounds = BBox.FromPositionAndSize( 0, float.MaxValue );
			_previewObject.Transform = new Transform( worldPos, tx.Rotation );
			_previewObject.Radius = BrushSettings.Size;
			_previewObject.Texture = Brush.Texture;
			_previewObject.Color = color.WithAlpha( 0 );
			_previewObject.BrushRotation = BrushSettings.Rotation;

			var ttx = terrain.WorldTransform;
			_previewObject.CellSize = terrain.Storage.TerrainSize / terrain.Storage.Resolution;
			_previewObject.TerrainOrigin = ttx.Position;
			_previewObject.TerrainRight = ttx.Rotation.Right;
			_previewObject.TerrainForward = ttx.Rotation.Forward;
		}
		else if ( _previewObject != null )
		{
			_previewObject.Delete();
			_previewObject = null;
		}

		var size = BrushSettings.Size;
		var sections = (int)(MathF.Sqrt( size ) * 5.0f).Clamp( 16, 64 );
		var length = MathX.LerpTo( 25f * 0.75f, 25f * 2f, BrushSettings.Opacity );

		var rotRad = -BrushSettings.Rotation * MathF.PI / 180f;
		var tickDir = new Vector3( 0, MathF.Cos( rotRad ), MathF.Sin( rotRad ) );

		using ( Gizmo.Scope( "TerrainSimpleBrush", worldPos, Rotation.LookAt( tx.Rotation.Up ) ) )
		{
			Gizmo.Draw.IgnoreDepth = true;
			Gizmo.Draw.Color = color;
			Gizmo.Draw.LineThickness = 4;
			Gizmo.Draw.Line( Vector3.Zero, Vector3.Forward * length );
			Gizmo.Draw.SolidSphere( Vector3.Forward * length, 2 );
			Gizmo.Draw.LineCircle( Vector3.Zero, size, 32, sections: sections );

			Gizmo.Draw.LineThickness = 2;
			Gizmo.Draw.Color = color.WithAlpha( 0.6f );
			Gizmo.Draw.Line( tickDir * size * 0.7f, tickDir * size );
		}
	}

	[Event( "scene.saved" )]
	static void OnSceneSaved( Scene scene )
	{
		foreach ( var terrain in scene.Components.GetAll<Terrain>( FindMode.EverythingInDescendants ) )
		{
			if ( terrain.Storage is null ) continue;
			if ( string.IsNullOrEmpty( terrain.Storage.ResourcePath ) ) continue;

			var asset = AssetSystem.FindByPath( terrain.Storage.ResourcePath );
			asset?.SaveToDisk( terrain.Storage );
		}
	}
}

/// <summary>
/// Container that shows/hides itself based on whether PaintTextureTool is the active subtool.
/// </summary>
internal class PaintOnlySectionWidget : Widget
{
	readonly TerrainEditorTool _tool;
	readonly ControlSheetRow _opacityRow;

	public PaintOnlySectionWidget( TerrainEditorTool tool, ControlSheetRow opacityRow ) : base( null )
	{
		_tool = tool;
		_opacityRow = opacityRow;
		Layout = Layout.Column();
		Layout.Spacing = 2;
	}

	[EditorEvent.Frame]
	void Tick()
	{
		bool isPaint = _tool.CurrentTool is PaintTextureTool;
		Hidden = !isPaint;

		if ( !_opacityRow.IsValid() ) return;

		if ( !isPaint )
		{
			_opacityRow.Enabled = true;
			_opacityRow.ToolTip = "";
			if ( _opacityRow.ControlWidget.IsValid() ) _opacityRow.ControlWidget.ToolTip = "";
		}
		else
		{
			TerrainEditorTool.SetOpacityToolTip( _opacityRow );
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

file class TerrainToolShortcutsWidget : Widget
{
	[Shortcut( "tools.terrain.raise-lower", "1", typeof( SceneViewWidget ) )]
	public void ActivateRaiseLowerTool() => EditorToolManager.SetSubTool( nameof( RaiseLowerTool ) );
	[Shortcut( "tools.terrain.smooth", "2", typeof( SceneViewWidget ) )]
	public void ActivateSmoothTool() => EditorToolManager.SetSubTool( nameof( SmoothTool ) );
	[Shortcut( "tools.terrain.flatten", "3", typeof( SceneViewWidget ) )]
	public void ActivateFlattenTool() => EditorToolManager.SetSubTool( nameof( FlattenTool ) );
	[Shortcut( "tools.terrain.noise", "4", typeof( SceneViewWidget ) )]
	public void ActivateNoiseTool() => EditorToolManager.SetSubTool( nameof( NoiseTool ) );
	[Shortcut( "tools.terrain.paint-texture", "5", typeof( SceneViewWidget ) )]
	public void ActivatePaintTextureTool() => EditorToolManager.SetSubTool( nameof( PaintTextureTool ) );
	[Shortcut( "tools.terrain.hole", "6", typeof( SceneViewWidget ) )]
	public void ActivateHoleTool() => EditorToolManager.SetSubTool( nameof( HoleTool ) );
}
