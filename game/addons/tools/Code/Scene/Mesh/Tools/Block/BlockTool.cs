
namespace Editor.MeshEditor;

/// <summary>
/// Create new shapes by dragging out a block
/// </summary>
[EditorTool( "tools.block-tool" )]
[Title( "Block Builder" )]
[Icon( "hardware" )]
[Group( "Mesh" )]
[Order( -1 )]
public partial class BlockTool : EditorTool
{
	private BBox _box;
	private BBox _startBox;
	private BBox _deltaBox;
	private bool _resizing;
	private bool _resizePressed;
	private bool _inProgress;
	private bool _dragging;
	private bool _finished;
	private Vector3 _dragStartPos;

	private readonly HashSet<PrimitiveBuilder> _primitives = new();
	private PrimitiveBuilder _primitive;
	private SceneObject _sceneObject;

	private PrimitiveBuilder Current
	{
		get => _primitive;
		set
		{
			if ( _primitive == value )
				return;

			_primitive = value;
			_primitive.Material = LastMaterial;

			BuildControlSheet();
			RebuildMesh();
		}
	}

	private bool InProgress
	{
		get => _inProgress;
		set
		{
			if ( _inProgress == value )
				return;

			_inProgress = value;

			UpdateStatus();
		}
	}

	private static float LastHeight = 128;
	private static Material LastMaterial = Material.Load( "materials/dev/reflectivity_30.vmat" );

	public override void OnEnabled()
	{
		base.OnEnabled();

		AllowGameObjectSelection = false;
		Selection.Clear();

		CreatePrimitiveBuilders();
	}

	public override void OnDisabled()
	{
		base.OnDisabled();

		if ( _sceneObject.IsValid() )
		{
			_sceneObject.RenderingEnabled = false;
			_sceneObject.Delete();
			_sceneObject = null;
		}

		if ( InProgress )
		{
			var go = CreateFromBox( _box );
			Selection.Set( go );
			Log.Info( "BlockTool: OnDisabled: Created object" );
			_finished = true;
			InProgress = false;
		}
		else
		{
			var selectedObjects = Selection.OfType<GameObject>().ToArray();
			Selection.Clear();
			foreach ( var o in selectedObjects )
				Selection.Add( o );
		}
	}

	private PolygonMesh Build( BBox box )
	{
		var primitive = new PrimitiveBuilder.PolygonMesh();
		_primitive.SetFromBox( box );
		_primitive.Build( primitive );

		var mesh = new PolygonMesh();
		var hVertices = mesh.AddVertices( primitive.Vertices.ToArray() );

		foreach ( var face in primitive.Faces )
		{
			var index = mesh.AddFace( face.Indices.Select( x => hVertices[x] ).ToArray() );
			mesh.SetFaceMaterial( index, face.Material );
		}

		return mesh;
	}

	private void RebuildMesh()
	{
		if ( !InProgress )
			return;

		if ( Current.Is2D )
		{
			_box.Maxs.z = _box.Mins.z;
		}
		else
		{
			_box.Maxs.z = _box.Mins.z + LastHeight;
		}

		var box = _box;
		var position = box.Center;
		box = BBox.FromPositionAndSize( 0, box.Size );

		var mesh = Build( box );

		foreach ( var hFace in mesh.FaceHandles )
			mesh.SetFaceMaterial( hFace, _primitive.Material );

		mesh.TextureAlignToGrid( Transform.Zero.WithPosition( position ) );
		mesh.SetSmoothingAngle( 40.0f );

		var model = mesh.Rebuild();
		var transform = new Transform( position );

		if ( !_sceneObject.IsValid() )
		{
			_sceneObject = new SceneObject( Scene.SceneWorld, model, transform );
		}
		else
		{
			_sceneObject.Model = model;
			_sceneObject.Transform = transform;
		}
	}

	private void CreatePrimitiveBuilders()
	{
		_primitives.Clear();

		foreach ( var type in GetBuilderTypes() )
		{
			_primitives.Add( type.Create<PrimitiveBuilder>() );
		}

		_primitive = _primitives.FirstOrDefault();
		_primitive.Material = LastMaterial;
	}

	private static IEnumerable<TypeDescription> GetBuilderTypes()
	{
		return EditorTypeLibrary.GetTypes<PrimitiveBuilder>()
			.Where( x => !x.IsAbstract ).OrderBy( x => x.Name );
	}

	private GameObject CreateFromBox( BBox box )
	{
		if ( _primitive is null )
			return null;

		using ( SceneEditorSession.Active.UndoScope( "Create Block" ).WithGameObjectCreations().Push() )
		{
			if ( _sceneObject.IsValid() )
			{
				_sceneObject.RenderingEnabled = false;
				_sceneObject.Delete();
				_sceneObject = null;
			}

			var go = new GameObject( true, "Box" );
			var mc = go.Components.Create<MeshComponent>( false );

			var position = box.Center;
			box = BBox.FromPositionAndSize( 0, box.Size );

			var polygonMesh = Build( box );

			foreach ( var hFace in polygonMesh.FaceHandles )
				polygonMesh.SetFaceMaterial( hFace, _primitive.Material );

			polygonMesh.TextureAlignToGrid( Transform.Zero.WithPosition( position ) );

			mc.WorldPosition = position;
			mc.Mesh = polygonMesh;
			mc.SmoothingAngle = 40.0f;
			mc.Enabled = true;

			return go;
		}
	}

	public override void OnSelectionChanged()
	{
		base.OnSelectionChanged();

		if ( !Selection.OfType<GameObject>().Any() )
		{
			return;
		}

		EditorToolManager.SetTool( nameof( ObjectEditorTool ) );
		_finished = true;
	}

	public override void OnUpdate()
	{
		if ( _finished )
			return;

		if ( Selection.OfType<GameObject>().Any() )
			return;

		EditorUtility.InspectorObject = this;

		if ( InProgress && Application.FocusWidget.IsValid() )
		{
			if ( Application.IsKeyDown( KeyCode.Escape ) ||
				 Application.IsKeyDown( KeyCode.Delete ) )
			{
				_resizing = false;
				_dragging = false;
				InProgress = false;

				if ( _sceneObject.IsValid() )
				{
					_sceneObject.RenderingEnabled = false;
					_sceneObject.Delete();
					_sceneObject = null;
				}
			}
		}

		if ( Current is null )
			return;

		LastMaterial = Current.Material;

		var textSize = 22 * Gizmo.Settings.GizmoScale * Application.DpiScale;

		if ( InProgress )
		{
			using ( Gizmo.Scope( "Tool" ) )
			{
				Gizmo.Hitbox.DepthBias = 0.01f;

				if ( !Gizmo.Pressed.Any && Gizmo.HasMouseFocus )
				{
					_resizing = false;
					_deltaBox = default;
					_startBox = default;

					if ( Current.Is2D )
					{
						_box.Maxs.z = _box.Mins.z;
					}
					else
					{
						_box.Maxs.z = _box.Mins.z + LastHeight;
					}
				}

				if ( Gizmo.Control.BoundingBox( "Resize", _box, out var outBox, out _resizePressed ) )
				{
					if ( !_resizing )
					{
						_startBox = _box;
						_resizing = true;
						_deltaBox = new BBox( Vector3.Zero, Vector3.Zero );
					}

					_deltaBox.Maxs += outBox.Maxs - _box.Maxs;
					_deltaBox.Mins += outBox.Mins - _box.Mins;

					_box = Gizmo.Snap( _startBox, _deltaBox );

					if ( Current.Is2D )
					{
						_box.Mins.z = _startBox.Mins.z;
						_box.Maxs.z = _startBox.Mins.z;
					}
					else
					{
						LastHeight = System.MathF.Abs( _box.Size.z );
					}

					RebuildMesh();
				}

				Gizmo.Draw.Color = Color.Red.WithAlpha( 0.5f );
				Gizmo.Draw.LineBBox( _startBox );
			}

			using ( Gizmo.Scope( "box" ) )
			{
				Gizmo.Draw.IgnoreDepth = true;
				Gizmo.Draw.LineThickness = 2;
				Gizmo.Draw.Color = Gizmo.Colors.Active.WithAlpha( 0.5f );
				Gizmo.Draw.LineBBox( _box );
				Gizmo.Draw.LineThickness = 3;
				Gizmo.Draw.Color = Gizmo.Colors.Left;
				Gizmo.Draw.ScreenText( $"L: {_box.Size.y:0.#}", _box.Maxs.WithY( _box.Center.y ), Vector2.Up * 32, size: textSize );
				Gizmo.Draw.Line( _box.Maxs.WithY( _box.Mins.y ), _box.Maxs.WithY( _box.Maxs.y ) );
				Gizmo.Draw.Color = Gizmo.Colors.Forward;
				Gizmo.Draw.ScreenText( $"W: {_box.Size.x:0.#}", _box.Maxs.WithX( _box.Center.x ), Vector2.Up * 32, size: textSize );
				Gizmo.Draw.Line( _box.Maxs.WithX( _box.Mins.x ), _box.Maxs.WithX( _box.Maxs.x ) );
				Gizmo.Draw.Color = Gizmo.Colors.Up;
				Gizmo.Draw.ScreenText( $"H: {_box.Size.z:0.#}", _box.Maxs.WithZ( _box.Center.z ), Vector2.Up * 32, size: textSize );
				Gizmo.Draw.Line( _box.Maxs.WithZ( _box.Mins.z ), _box.Maxs.WithZ( _box.Maxs.z ) );
			}

			if ( Application.FocusWidget.IsValid() && Application.IsKeyDown( KeyCode.Enter ) )
			{
				var go = CreateFromBox( _box );
				Selection.Set( go );
				Log.Info( "BlockTool: OnUpdate: Created object" );

				_finished = true;
				InProgress = false;

				EditorToolManager.SetTool( nameof( ObjectEditorTool ) );
			}
		}
		else
		{
			_resizePressed = false;
		}

		if ( _resizePressed )
			return;

		var tr = Trace.UseRenderMeshes( true )
			.UsePhysicsWorld( true )
			.Run();

		if ( !tr.Hit || _dragging )
		{
			var plane = _dragging ? new Plane( _dragStartPos, Vector3.Up ) : new Plane( Vector3.Up, 0.0f );
			if ( plane.TryTrace( new Ray( tr.StartPosition, tr.Direction ), out tr.EndPosition, true ) )
			{
				tr.Hit = true;
				tr.Normal = plane.Normal;
			}
		}

		if ( !tr.Hit )
			return;

		var r = Rotation.LookAt( tr.Normal );
		var localPosition = tr.EndPosition * r.Inverse;
		localPosition = Gizmo.Snap( localPosition, new Vector3( 0, 1, 1 ) );
		tr.EndPosition = localPosition * r;

		if ( !_dragging )
		{
			using ( Gizmo.Scope( "Aim Handle", new Transform( tr.EndPosition, Rotation.LookAt( tr.Normal ) ) ) )
			{
				Gizmo.Draw.Color = Color.White;
				Gizmo.Draw.LineCircle( 0, 2 );
				Gizmo.Draw.Color = Color.White.WithAlpha( 0.5f );
				Gizmo.Draw.LineCircle( 0, 3 );
				Gizmo.Draw.Color = Color.White.WithAlpha( 0.3f );
				Gizmo.Draw.LineCircle( 0, 6 );
				Gizmo.Draw.Color = Color.White.WithAlpha( 0.1f );
				Gizmo.Draw.LineCircle( 0, 12 );
			}
		}

		if ( Gizmo.WasLeftMousePressed )
		{
			if ( InProgress )
				CreateFromBox( _box );

			_dragging = true;
			_dragStartPos = tr.EndPosition;
			InProgress = false;
		}
		else if ( Gizmo.WasLeftMouseReleased && _dragging )
		{
			var spacing = Gizmo.Settings.SnapToGrid ? Gizmo.Settings.GridSpacing : 1.0f;
			var box = new BBox( _dragStartPos, tr.EndPosition );

			if ( box.Size.x >= spacing || box.Size.y >= spacing )
			{
				if ( Gizmo.Settings.SnapToGrid )
				{
					if ( box.Size.x < spacing ) box.Maxs.x += spacing;
					if ( box.Size.y < spacing ) box.Maxs.y += spacing;
				}

				float height = Current.Is2D ? 0 : LastHeight;
				var size = box.Size.WithZ( height );
				var position = box.Center.WithZ( box.Center.z + (height * 0.5f) );
				_box = BBox.FromPositionAndSize( position, size );
				InProgress = true;

				RebuildMesh();
			}

			_dragging = false;
			_dragStartPos = default;
		}

		if ( _dragging )
		{
			using ( Gizmo.Scope( "Rect", 0 ) )
			{
				var box = new BBox( _dragStartPos, tr.EndPosition );

				Gizmo.Draw.IgnoreDepth = true;
				Gizmo.Draw.LineThickness = 2;
				Gizmo.Draw.Color = Gizmo.Colors.Active.WithAlpha( 0.5f );
				Gizmo.Draw.LineBBox( box );
				Gizmo.Draw.Color = Gizmo.Colors.Left;
				Gizmo.Draw.ScreenText( $"L: {box.Size.y:0.#}", box.Mins.WithY( box.Center.y ), Vector2.Up * 32, size: textSize );
				Gizmo.Draw.Color = Gizmo.Colors.Forward;
				Gizmo.Draw.ScreenText( $"W: {box.Size.x:0.#}", box.Mins.WithX( box.Center.x ), Vector2.Up * 32, size: textSize );
			}
		}
	}

	[Shortcut( "tools.block-tool", "Shift+B", typeof( SceneViewportWidget ) )]
	public static void ActivateTool()
	{
		EditorToolManager.SetTool( nameof( BlockTool ) );
	}
}
