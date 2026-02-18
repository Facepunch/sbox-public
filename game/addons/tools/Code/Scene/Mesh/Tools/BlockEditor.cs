namespace Editor.MeshEditor;

/// <summary>
/// Create stuff as long as it fits in a box, woah crazy.
/// </summary>
[Title( "Block" ), Icon( "view_in_ar" )]
public sealed class BlockEditor( PrimitiveTool tool ) : PrimitiveEditor( tool )
{
	PrimitiveBuilder _primitive = EditorTypeLibrary.Create<PrimitiveBuilder>( nameof( BlockPrimitive ) );
	Material _activeMaterial = tool.ActiveMaterial;

	BBox? _box;
	BBox _startBox;
	BBox _deltaBox;
	Vector3 _dragStartPos;
	bool _dragStarted;
	Model _previewModel;

	static float TextSize => 22 * Gizmo.Settings.GizmoScale * Application.DpiScale;

	private static float s_lastHeight = 128;

	public override bool CanBuild => _primitive is not null && _box.HasValue;
	public override bool InProgress => _dragStarted || CanBuild;

	public override PolygonMesh Build()
	{
		if ( !CanBuild ) return null;

		var box = _box.Value;
		if ( _primitive.Is2D )
		{
			box.Maxs.z = box.Mins.z;
		}

		var material = Tool.ActiveMaterial;
		var primitive = new PrimitiveBuilder.PolygonMesh();
		_primitive.Material = material;
		_primitive.SetFromBox( box );
		_primitive.Build( primitive );

		var mesh = new PolygonMesh();
		var vertices = mesh.AddVertices( [.. primitive.Vertices] );

		foreach ( var face in primitive.Faces )
		{
			var index = mesh.AddFace( [.. face.Indices.Select( x => vertices[x] )] );
			mesh.SetFaceMaterial( index, material );
		}

		mesh.TextureAlignToGrid( Transform.Zero );
		mesh.SetSmoothingAngle( 40.0f );

		return mesh;
	}

	public override void OnCreated( MeshComponent component )
	{
		var selection = SceneEditorSession.Active.Selection;
		selection.Set( component.GameObject );

		if ( !_dragStarted )
		{
			EditorToolManager.SetSubTool( nameof( ObjectSelection ) );
			Tool.MeshTool.SetMoveMode<ResizeMode>();
		}

		_box = null;
		_dragStarted = false;
	}

	void StartStage( SceneTrace trace )
	{
		var tr = trace.Run();

		if ( !tr.Hit )
		{
			var plane = new Plane( Vector3.Up, 0.0f );
			if ( plane.TryTrace( Gizmo.CurrentRay, out var point, true ) )
			{
				tr.Hit = true;
				tr.Normal = plane.Normal;
				tr.EndPosition = point;
			}
		}

		if ( !tr.Hit ) return;

		tr.EndPosition = GridSnap( tr.EndPosition, tr.Normal );

		if ( Gizmo.WasLeftMousePressed )
		{
			_dragStartPos = tr.EndPosition;
			_dragStarted = true;

			if ( _box.HasValue )
			{
				Tool.Create();
			}

			_box = null;
			_dragStarted = true;
		}
		else
		{
			var size = 3.0f * Gizmo.Camera.Position.Distance( tr.EndPosition ) / 1000.0f;
			Gizmo.Draw.Color = Color.White;
			Gizmo.Draw.SolidSphere( tr.EndPosition, size );
		}
	}

	void DraggingStage()
	{
		var plane = new Plane( _dragStartPos, Vector3.Up );
		if ( !plane.TryTrace( Gizmo.CurrentRay, out var point, true ) ) return;

		point = GridSnap( point, Vector3.Up );

		if ( !Gizmo.IsLeftMouseDown )
		{
			var delta = point - _dragStartPos;

			if ( delta.x.AlmostEqual( 0.0f ) || delta.y.AlmostEqual( 0.0f ) )
			{
				_box = null;
				_dragStarted = false;

				return;
			}

			_box = new BBox( _dragStartPos, point + Vector3.Up * s_lastHeight );
			_dragStarted = false;

			BuildPreview();
		}
		else
		{
			var box = new BBox( _dragStartPos, point );
			Gizmo.Draw.IgnoreDepth = true;
			Gizmo.Draw.LineThickness = 2;
			Gizmo.Draw.Color = Gizmo.Colors.Active.WithAlpha( 0.5f );
			Gizmo.Draw.LineBBox( box );

			var textScope = new TextRendering.Scope
			{
				Text = null,
				TextColor = Color.White,
				FontSize = TextSize,
				FontName = "Roboto Mono",
				FontWeight = 400,
				LineHeight = 1,
				Outline = new TextRendering.Outline() { Color = Color.Black, Enabled = true, Size = 3 }
			};

			textScope.Text = $"L: {box.Size.y:0.#}";
			textScope.TextColor = Gizmo.Colors.Left;
			Gizmo.Draw.ScreenText( textScope, box.Mins.WithY( box.Center.y ), Vector2.Up * 32 );

			textScope.Text = $"W: {box.Size.x:0.#}";
			textScope.TextColor = Gizmo.Colors.Forward;
			Gizmo.Draw.ScreenText( textScope, box.Mins.WithX( box.Center.x ), Vector2.Up * 32 );
		}
	}

	public override void OnUpdate( SceneTrace trace )
	{
		if ( Application.IsKeyDown( KeyCode.Escape ) ||
			 Application.IsKeyDown( KeyCode.Delete ) )
		{
			Cancel();
		}

		if ( _activeMaterial != Tool.ActiveMaterial )
		{
			BuildPreview();
			_activeMaterial = Tool.ActiveMaterial;
		}

		if ( !Gizmo.Pressed.Any )
		{
			if ( _dragStarted )
			{
				DraggingStage();
			}
			else
			{
				StartStage( trace );
			}
		}

		DrawBox();
	}

	void Cancel()
	{
		_box = null;
		_dragStarted = false;
	}

	public override void OnCancel()
	{
		Cancel();
	}

	void DrawBox()
	{
		if ( !_box.HasValue ) return;

		var box = _box.Value;

		if ( _primitive.Is2D )
		{
			box.Maxs.z = box.Mins.z;
		}

		using ( Gizmo.Scope( "box" ) )
		{
			Gizmo.Hitbox.DepthBias = 0.01f;

			if ( !Gizmo.Pressed.Any )
			{
				_startBox = box;
				_deltaBox = default;
			}

			if ( Gizmo.Control.BoundingBox( "Resize", box, out var outBox ) )
			{
				_deltaBox.Maxs += outBox.Maxs - box.Maxs;
				_deltaBox.Mins += outBox.Mins - box.Mins;

				box = Gizmo.Snap( _startBox, _deltaBox );

				if ( _primitive.Is2D )
				{
					var b = box;
					b.Mins.z = _box.Value.Mins.z;
					b.Maxs.z = _box.Value.Maxs.z;
					_box = b;

					box.Mins.z = b.Mins.z;
					box.Maxs.z = b.Mins.z;
				}
				else
				{
					s_lastHeight = MathF.Abs( box.Size.z );

					_box = box;
				}

				BuildPreview();
			}

			using ( Gizmo.Scope( "Bounds" ) )
			{
				DimensionDisplay.DrawBounds( box );
			}
		}

		if ( _previewModel.IsValid() && !_previewModel.IsError )
		{
			Gizmo.Draw.Model( _previewModel );
		}
	}

	static Vector3 GridSnap( Vector3 point, Vector3 normal )
	{
		var basis = Rotation.LookAt( normal );
		return Gizmo.Snap( point * basis.Inverse, new Vector3( 0, 1, 1 ) ) * basis;
	}

	public override Widget CreateWidget()
	{
		return new BlockEditorWidget( this, BuildPreview );
	}

	void BuildPreview()
	{
		var mesh = Build();
		_previewModel = mesh?.Rebuild();
	}

	private static IEnumerable<TypeDescription> GetBuilderTypes()
	{
		return EditorTypeLibrary.GetTypes<PrimitiveBuilder>()
			.Where( x => !x.IsAbstract )
			.OrderBy( x => x.Name );
	}

	class BlockEditorWidget : ToolSidebarWidget
	{
		readonly BlockEditor _editor;
		readonly Layout _controlLayout;
		PrimitiveBuilder _primitive;
		readonly Action _onEdited;

		public BlockEditorWidget( BlockEditor editor, Action onEdited )
		{
			_editor = editor;
			_primitive = _editor._primitive;
			_onEdited = onEdited;

			Layout.Margin = 0;

			{
				var group = AddGroup( "Shape Type" );
				var list = group.Add( new PrimitiveListView( this ) );
				list.FixedWidth = 200;
				list.SetItems( GetBuilderTypes() );
				list.SelectItem( list.Items.FirstOrDefault( x => (x as TypeDescription).TargetType == _primitive?.GetType() ) );
				list.ItemSelected = ( e ) => OnPrimitiveSelected( (e as TypeDescription).TargetType );
				list.BuildLayout();
			}

			_controlLayout = Layout.AddColumn();
			BuildControlSheet();

			Layout.AddStretchCell();
		}

		void OnPrimitiveSelected( Type type )
		{
			_editor._primitive = EditorTypeLibrary.Create<PrimitiveBuilder>( type );
			_editor.BuildPreview();
		}

		void BuildControlSheet()
		{
			using var x = SuspendUpdates.For( this );

			_controlLayout.Clear( true );

			if ( _primitive is null ) return;

			var title = EditorTypeLibrary.GetType( _primitive.GetType() ).Title;
			var w = new ToolSidebarWidget( this );
			w.Layout.Margin = 0;
			_controlLayout.Add( w );

			var group = w.AddGroup( $"{title} Properties" );
			var so = _primitive.GetSerialized();
			so.OnPropertyChanged += ( e ) => _onEdited?.Invoke();
			var sheet = new ControlSheet();
			sheet.AddObject( so );
			group.Add( sheet );
		}

		[EditorEvent.Frame]
		public void Frame()
		{
			if ( _primitive == _editor._primitive ) return;

			_primitive = _editor._primitive;
			BuildControlSheet();
		}
	}
}
