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

	public override void OnEnabled()
	{
		AllowGameObjectSelection = false;

		// if we don't have a terrain already selected.. just grab one
		if ( !GetSelectedComponent<Terrain>().IsValid() )
		{
			Selection.Clear();
			var first = Scene.GetAllComponents<Terrain>().FirstOrDefault();
			if ( first.IsValid() ) Selection.Add( first.GameObject );
		}

		var brushSettings = new BrushSettingsWidgetWindow( SceneOverlay, EditorUtility.GetSerializedObject( BrushSettings ) );
		AddOverlay( brushSettings, TextFlag.RightBottom, 10 );
	}

	public override void OnDisabled()
	{
		_previewObject?.Delete();
		_previewObject = null;
	}

	private Transform? brushTransform;

	public override void OnUpdate()
	{
		base.OnUpdate();

		if ( Gizmo.HasMouseFocus )
		{
			var terrain = GetSelectedComponent<Terrain>();

			if ( !terrain.IsValid() )
				return;

			if ( terrain.RayIntersects( Gizmo.CurrentRay, Gizmo.RayDepth, out var hitPosition ) )
			{
				brushTransform = new Transform( terrain.WorldPosition + hitPosition );
			}
			else
			{
				brushTransform = default;
			}
		}

		if ( brushTransform.HasValue )
		{
			DrawBrushPreview( brushTransform.Value );
		}
	}

	BrushPreviewSceneObject _previewObject;

	void DrawBrushPreview( Transform transform )
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
