namespace Editor.RectEditor;

public enum DragState
{
	None,
	WaitingForMovement,
	Dragging,
	Panning,
}

enum GridSnapMode
{
	Nearest,
	RoundDown,
	RoundUp,
}

public class RectView : Widget
{
	private readonly Window Session;
	private Document Document => Session.Document;

	private Rect DrawRect;
	internal Pixmap SourceImage;
	private Pixmap ScaledImage;
	private DragState DragState;
	private Vector2 DragStartPos;
	private Vector2 HoveredCorner;
	private Rect NewRect;
	private Rect ViewRect;
	private List<Document.Rectangle> RectanglesUnderCursor;
	private HashSet<Document.Rectangle> DraggingRectangles = new();

	private float ZoomLevel = 1.0f;
	private Vector2 PanOffset = Vector2.Zero;
	private Vector2 PanStartOffset;

	private List<Rect> UvAssetRects = new();

	public RectView( Window session ) : base( session )
	{
		Session = session;

		Name = "Rect View";
		WindowTitle = "Rect View";
		SetWindowIcon( "space_dashboard" );

		MouseTracking = true;
		FocusMode = FocusMode.Click;

		DrawRect = GetDrawRect();
		ResetView();
	}

	protected override void OnKeyPress( KeyEvent e )
	{
		base.OnKeyPress( e );

		if ( e.Accepted ) return;

		var direction = Vector2.Zero;

		if ( e.Key == KeyCode.Left ) direction = new Vector2( -1, 0 );
		if ( e.Key == KeyCode.Right ) direction = new Vector2( 1, 0 );
		if ( e.Key == KeyCode.Up ) direction = new Vector2( 0, -1 );
		if ( e.Key == KeyCode.Down ) direction = new Vector2( 0, 1 );

		if ( direction != Vector2.Zero )
		{
			Nudge( direction );
			e.Accepted = true;
		}
	}

	private void Nudge( Vector2 direction )
	{
		var gridCountX = GetGridCountX();
		var gridCountY = GetGridCountY();
		var step = new Vector2( 1.0f / gridCountX, 1.0f / gridCountY );
		var delta = direction * step;

		if ( Session.Settings.IsFastTextureTool )
		{
			var meshRect = Document.Rectangles.OfType<Document.MeshRectangle>().FirstOrDefault();
			if ( meshRect != null )
			{
				Session.ExecuteUndoableAction( "Nudge UVs", () =>
				{
					meshRect.Min += delta;
					meshRect.Max += delta;
					meshRect.ApplyMapping( Session.Settings.FastTextureSettings, false );
					Document.Modified = true;
					Document.OnModified?.Invoke();
				} );
				Update();
			}
		}
		else if ( Document.SelectedRectangles.Count > 0 )
		{
			Session.ExecuteUndoableAction( "Nudge Rectangles", () =>
			{
				foreach ( var rect in Document.SelectedRectangles )
				{
					rect.Min += delta;
					rect.Max += delta;
				}
				Document.Modified = true;
				Document.OnModified?.Invoke();
			} );
			Update();
		}
	}

	private Vector2 SnapUVToGrid( Vector2 uv )
	{
		var gridCountX = GetGridCountX();
		var gridCountY = GetGridCountY();

		var x = (int)(gridCountX * uv.x + 0.5f);
		var y = (int)(gridCountY * uv.y + 0.5f);

		return new Vector2( x / (float)gridCountX, y / (float)gridCountY );
	}

	private Vector2 PixelToUV_OnGrid( Vector2 vPixel )
	{
		if ( !Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Ctrl ) ) return SnapUVToGrid( PixelToUV( vPixel ) );
		return PixelToUV( vPixel );
	}

	private void DragCreateRect( Vector2 mousePos )
	{
		var minStart = PixelToUV_OnGrid( DragStartPos );
		var maxStart = PixelToUV_OnGrid( DragStartPos );
		var current = PixelToUV_OnGrid( mousePos );
		var min = Vector2.Min( current, minStart );
		var max = Vector2.Max( current, maxStart );

		NewRect = new Rect( min, max - min );

		if ( Session.Settings.IsFastTextureTool )
		{
			var meshRect = Document.Rectangles.OfType<Document.MeshRectangle>().FirstOrDefault();
			if ( meshRect != null )
			{
				meshRect.Min = NewRect.TopLeft;
				meshRect.Max = NewRect.BottomRight;

				// Re-apply mapping so the UVs deform to the new shape immediately
				meshRect.ApplyMapping( Session.Settings.FastTextureSettings, false );

				Document.Modified = true;
				Document.OnModified?.Invoke();
			}
		}

		Update();
	}

	private void DragMoveRect( Vector2 mousePos )
	{
		var start = PixelToUV_OnGrid( DragStartPos );
		var end = PixelToUV_OnGrid( mousePos );
		var diff = end - start;
		if ( diff.x == 0 && diff.y == 0 )
			return;

		foreach ( var rectangle in DraggingRectangles )
		{
			rectangle.Min += diff;
			rectangle.Max += diff;
		}
		DragStartPos = mousePos;

		Document.Modified = true;
		Document.OnModified?.Invoke();
		Update();
	}

	private void DragResizeRect( Vector2 mousePos )
	{
		var currentUV = PixelToUV_OnGrid( mousePos );

		foreach ( var rectangle in DraggingRectangles )
		{
			var a = rectangle.Min;
			var b = rectangle.Max;

			// Horizontal
			if ( HoveredCorner.x < 0 )
			{
				if ( currentUV.x > b.x )
				{
					a = a.WithX( b.x );
					b = b.WithX( currentUV.x );
					HoveredCorner = HoveredCorner.WithX( 1 );
				}
				else
				{
					a = a.WithX( currentUV.x );
				}
			}
			else if ( HoveredCorner.x > 0 )
			{
				if ( currentUV.x < a.x )
				{
					b = b.WithX( a.x );
					a = a.WithX( currentUV.x );
					HoveredCorner = HoveredCorner.WithX( -1 );
				}
				else
				{
					b = b.WithX( currentUV.x );
				}
			}

			// Vertical
			if ( HoveredCorner.y < 0 )
			{
				if ( currentUV.y > b.y )
				{
					a = a.WithY( b.y );
					b = b.WithY( currentUV.y );
					HoveredCorner = HoveredCorner.WithY( 1 );
				}
				else
				{
					a = a.WithY( currentUV.y );
				}
			}
			else if ( HoveredCorner.y > 0 )
			{
				if ( currentUV.y < a.y )
				{
					b = b.WithY( a.y );
					a = a.WithY( currentUV.y );
					HoveredCorner = HoveredCorner.WithY( -1 );
				}
				else
				{
					b = b.WithY( currentUV.y );
				}
			}

			rectangle.Min = a;
			rectangle.Max = b;
		}

		Document.Modified = true;
		Document.OnModified?.Invoke();
		Update();
	}

	[EditorEvent.Frame]
	protected void OnFrame()
	{
		var mousePos = FromScreen( Application.CursorPosition );
		FindRectanglesUnderCursor( mousePos );
		FindHoveredCorner( mousePos );
		FindHoveredEdgeInPickMode( mousePos );
		SetCursorFromState();
	}

	void FindHoveredEdgeInPickMode( Vector2 mousePos )
	{
		// Update hovered edge when in pick edge mode
		var meshRect = RectanglesUnderCursor.FirstOrDefault( x => x is Document.MeshRectangle ) as Document.MeshRectangle;
		if ( meshRect is null )
			return;

		if ( Session.Settings.IsFastTextureTool && Session.Settings.FastTextureSettings.IsPickingEdge )
		{
			var mouseUV = PixelToUV( mousePos );
			meshRect.FindHoveredEdge( mouseUV );
			Update();
		}
		else
		{
			meshRect.HoveredEdge = (-1, -1);
		}
	}

	protected override void OnMouseMove( MouseEvent e )
	{
		base.OnMouseMove( e );

		if ( DragState == DragState.Panning )
		{
			var delta = e.LocalPosition - DragStartPos;
			var uvDelta = new Vector2( delta.x / DrawRect.Width, delta.y / DrawRect.Height ) * ViewRect.Size;
			PanOffset = PanStartOffset - uvDelta;
			UpdateViewRect();
			Update();
			return;
		}

		if ( DragState == DragState.WaitingForMovement && !Session.Settings.IsFastTextureTool )
		{
			if ( DraggingRectangles.Count == 1 )
			{
				Document.SelectRectangle( DraggingRectangles.First(), SelectionOperation.Set );
			}
			DragState = DragState.Dragging;
		}

		if ( DragState != DragState.None && DragState != DragState.Panning )
		{
			if ( DraggingRectangles.Count > 0 )
			{
				if ( HoveredCorner == 0 || Document.SelectedRectangles.Count > 1 )
				{
					DragMoveRect( e.LocalPosition );
				}
				else
				{
					DragResizeRect( e.LocalPosition );
				}
			}
			else
			{
				DragCreateRect( e.LocalPosition );
			}
		}
	}

	protected override void OnMousePress( MouseEvent e )
	{
		base.OnMousePress( e );

		if ( e.Button == MouseButtons.Middle )
		{
			DragState = DragState.Panning;
			DragStartPos = e.LocalPosition;
			PanStartOffset = PanOffset;
			Cursor = CursorShape.ClosedHand;
			return;
		}

		if ( e.Button == MouseButtons.Left )
		{
			// Handle edge picking mode for Fast Texture Tool
			if ( Session.Settings.IsFastTextureTool && Session.Settings.FastTextureSettings.IsPickingEdge )
			{
				var meshRect = GetFirstRectangleUnderCursor() as Document.MeshRectangle;
				if ( meshRect != null )
				{
					var clickUV = PixelToUV( e.LocalPosition );
					if ( meshRect.PickAlignmentEdge( clickUV, 0.02f ) )
					{
						Session.Settings.FastTextureSettings.IsPickingEdge = false;
						Session.Settings.FastTextureSettings.OnSettingsChanged?.Invoke();
						Update();
					}
				}
				return;
			}

			DragState = DragState.WaitingForMovement;
			DragStartPos = e.LocalPosition;
			DraggingRectangles.Clear();

			var rectUnderCursor = GetFirstRectangleUnderCursor();
			if ( rectUnderCursor is not null )
			{
				if ( Document.SelectedRectangles.Contains( rectUnderCursor ) )
				{
					// Drag all selected rectangles if the rectangle under cursor is selected already
					DraggingRectangles = Document.SelectedRectangles.ToHashSet();
				}
				else
				{
					// Just drag the rectangle under cursor if it is not selected, and then we'll select it afterwards
					DraggingRectangles = [rectUnderCursor];
				}
			}
		}
		else if ( e.Button == MouseButtons.Right && RectanglesUnderCursor.Count > 0 )
		{
			CreateContextMenu( GetFirstRectangleUnderCursor() );
		}
	}

	protected override void OnMouseReleased( MouseEvent e )
	{
		base.OnMouseReleased( e );

		if ( e.Button == MouseButtons.Middle )
		{
			DragState = DragState.None;
			return;
		}

		if ( e.Button == MouseButtons.Left )
		{
			var operation = (e.HasShift || e.HasCtrl) ? SelectionOperation.Add : SelectionOperation.Set;
			if ( DragState == DragState.Dragging )
			{
				if ( Document is not null && NewRect.Width > 0.0f && NewRect.Height > 0.0f && DraggingRectangles.Count == 0 )
				{
					Session.ExecuteUndoableAction( "Create Rectangle", () => Document.SelectRectangle( Document.AddRectangle( Session, NewRect ), operation ) );
				}
			}
			else if ( DraggingRectangles.Count == 0 || DragState == DragState.WaitingForMovement )
			{
				Session.ExecuteUndoableAction( "Select Rectangle", () => Document.SelectRectangle( GetFirstRectangleUnderCursor(), operation ) );
			}

			DragState = DragState.None;
			NewRect = default;
		}
	}

	private void CreateContextMenu( Document.Rectangle rectangle )
	{
		var m = new ContextMenu( this );

		if ( Session.Settings.IsFastTextureTool )
			return;

		m.AddOption( "Delete Rectangle", "delete", () => Session.ExecuteUndoableAction( "Delete Rectangle", () => Document.DeleteRectangles( [rectangle] ) ) );

		m.OpenAtCursor();
	}

	public void SetMaterial( Material material )
	{
		SourceImage = null;
		ScaledImage = null;

		if ( material is null )
			return;

		var texture = material.FirstTexture;
		if ( texture is null )
			return;

		RenderMaterial( material, texture.Size );

		if ( SourceImage is null )
			return;

		UpdateScaledBackgroundImage();

		if ( Session.Settings.IsFastTextureTool )
		{
			DrawRectUv();
		}
	}

	void RenderMaterial( Material material, Vector2 size )
	{
		var world = new SceneWorld();

		var camera = new SceneCamera
		{
			BackgroundColor = Color.Black,
			Ortho = true,
			Rotation = Rotation.FromPitch( 90 ),
			Position = Vector3.Up * 200,
			OrthoHeight = 100,
			World = world
		};

		var light = new SceneLight( world )
		{
			Radius = 4000,
			LightColor = Color.White * 0.8f,
			Position = new Vector3( 0, 0, 100 ),
			ShadowsEnabled = true
		};

		var debugMode = Session.Settings.FastTextureSettings.DebugMode;

		camera.DebugMode = debugMode switch
		{
			DebugMode.Default => SceneCameraDebugMode.Normal,
			DebugMode.Roughness => SceneCameraDebugMode.Roughness,
			DebugMode.Normals => SceneCameraDebugMode.NormalMap,
			_ => SceneCameraDebugMode.Normal,
		};

		var model = Model.Load( "models/dev/plane_blend.vmdl" );
		var obj = new SceneObject( world, model );
		obj.Transform = new Transform
		{
			Position = Vector3.Zero,
			Rotation = Rotation.From( 0, 180, 0 ),
			Scale = new Vector3( 1, size.x / size.y, 1 )
		};

		obj.SetMaterialOverride( material );

		SourceImage = new Pixmap( size );
		if ( !camera.RenderToPixmap( SourceImage ) )
			SourceImage = null;

		world.Delete();
		camera.Dispose();
	}

	private void DrawRectUv()
	{
		UvAssetRects.Clear();

		var materialPath = Session.Settings.ReferenceMaterial;
		if ( materialPath is null )
			return;

		var asset = AssetSystem.FindByPath( materialPath );
		if ( asset is null )
			return;

		var data = RectAssetData.Find( asset );
		if ( data is null || data.RectangleSets == null || data.RectangleSets.Count == 0 )
			return;

		var set = data.RectangleSets.FirstOrDefault();
		if ( set is null || set.Rectangles == null )
			return;

		const float invScale = 1.0f / 32768.0f;

		foreach ( var r in set.Rectangles )
		{
			if ( r == null || r.Min == null || r.Max == null || r.Min.Length < 2 || r.Max.Length < 2 )
				continue;

			var minUv = new Vector2( r.Min[0] * invScale, r.Min[1] * invScale );
			var maxUv = new Vector2( r.Max[0] * invScale, r.Max[1] * invScale );

			if ( maxUv.x <= minUv.x || maxUv.y <= minUv.y )
				continue;

			UvAssetRects.Add( new Rect( minUv, maxUv - minUv ) );
		}

		Update();
	}

	private void UpdateScaledBackgroundImage()
	{
		ScaledImage = SourceImage?.Resize( DrawRect.Size );
	}

	protected override void OnResize()
	{
		base.OnResize();

		DrawRect = GetDrawRect();

		UpdateScaledBackgroundImage();
	}

	protected override void OnWheel( WheelEvent e )
	{
		base.OnWheel( e );

		var mouseUV = PixelToUV( e.Position );

		// Zoom in/out
		var zoomFactor = e.Delta > 0 ? 1.1f : 0.9f;
		var newZoom = System.Math.Clamp( ZoomLevel * zoomFactor, 0.1f, 10.0f );

		if ( newZoom != ZoomLevel )
		{
			// Adjust pan to zoom towards mouse position
			var zoomRatio = newZoom / ZoomLevel;
			PanOffset = mouseUV - (mouseUV - PanOffset) / zoomRatio;
			ZoomLevel = newZoom;
			UpdateViewRect();
			Update();
		}
	}

	protected override void OnPaint()
	{
		Paint.ClearPen();

		Paint.SetBrush( Theme.ControlBackground );
		Paint.DrawRect( LocalRect );

		var topLeft = UVToPixel( 0 );
		var bottomRight = UVToPixel( 1 );
		var uvRect = Rect.FromPoints( topLeft, bottomRight );

		if ( ScaledImage is not null )
		{
			if ( Session.Settings.IsFastTextureTool && Session.Settings.FastTextureSettings.IsTileView )
			{
				// Tiled View
				var tileSize = uvRect.Size;
				var viewBounds = LocalRect;

				var startX = MathF.Floor( (viewBounds.Left - uvRect.Left) / tileSize.x ) * tileSize.x + uvRect.Left;
				var startY = MathF.Floor( (viewBounds.Top - uvRect.Top) / tileSize.y ) * tileSize.y + uvRect.Top;

				for ( float y = startY; y < viewBounds.Bottom; y += tileSize.y )
				{
					for ( float x = startX; x < viewBounds.Right; x += tileSize.x )
					{
						var tileRect = new Rect( x, y, tileSize.x, tileSize.y );
						Paint.Draw( tileRect, ScaledImage );
					}
				}
			}
			else
			{
				// Normal View
				Paint.Draw( uvRect, ScaledImage );
			}
		}

		if ( Session.GridEnabled )
		{
			if ( Session.Settings.IsFastTextureTool && Session.Settings.FastTextureSettings.IsTileView )
			{
				DrawTiledGrid( uvRect );
			}
			else
			{
				DrawGrid( uvRect );
			}
		}

		if ( Session.Settings.FastTextureSettings.ShowRects )
		{
			DrawUvAssetRectangles();
		}

		DrawRectangleSet( Document?.Rectangles );

		if ( DragState == DragState.Dragging )
		{
			var newTopLeft = UVToPixel( NewRect.TopLeft );
			var newBottomRight = UVToPixel( NewRect.BottomRight );
			var newRect = new Rect( newTopLeft, newBottomRight - newTopLeft );
			Paint.ClearBrush();
			Paint.SetPen( Color.Yellow, 3 );
			Paint.DrawRect( newRect );
		}
	}

	public Vector2 UVToPixel( Vector2 uv )
	{
		var transformedUV = (uv - ViewRect.TopLeft) / ViewRect.Size;
		return new Vector2( (int)((transformedUV.x * DrawRect.Width) + DrawRect.Left), (int)((transformedUV.y * DrawRect.Height) + DrawRect.Top) );
	}

	public Vector2 PixelToUV( Vector2 pixel )
	{
		var normalizedPixel = new Vector2( (pixel.x - DrawRect.Left) / DrawRect.Width, (pixel.y - DrawRect.Top) / DrawRect.Height );
		return ViewRect.TopLeft + normalizedPixel * ViewRect.Size;
	}

	private int GetGridCountX()
	{
		var width = SourceImage is null ? 512 : System.Math.Max( (int)SourceImage.Width, 1 );
		var gridSize = Math.Max( 1, Session.Settings.GridSize );
		return width / gridSize;
	}

	private int GetGridCountY()
	{
		var height = SourceImage is null ? 512 : System.Math.Max( (int)SourceImage.Height, 1 );
		var gridSize = Math.Max( 1, Session.Settings.GridSize );
		return height / gridSize;
	}

	public Document.Rectangle GetFirstRectangleUnderCursor()
	{
		return RectanglesUnderCursor?.FirstOrDefault();
	}

	void FindRectanglesUnderCursor( Vector2 mousePos )
	{
		RectanglesUnderCursor = FindRectanglesContainingPoint( PixelToUV( mousePos ) );

		Update();
	}

	void FindHoveredCorner( Vector2 mousePos )
	{
		if ( DragState != DragState.None )
			return;

		HoveredCorner = 0;
		var first = RectanglesUnderCursor.FirstOrDefault();
		if ( first is not null )
		{
			HoveredCorner = GetHoveredCornerForRectangle( first, PixelToUV( mousePos ) );
		}
	}
	void SetCursorFromState()
	{
		// Show crosshair cursor when picking edge
		if ( Session.Settings.IsFastTextureTool && Session.Settings.FastTextureSettings.IsPickingEdge )
		{
			Cursor = CursorShape.Cross;
			return;
		}

		if ( DragState == DragState.Panning )
		{
			Cursor = CursorShape.ClosedHand;
			return;
		}

		bool canResize = Document.SelectedRectangles.Count < 2 && HoveredCorner != 0;
		var rectUnderCursor = GetFirstRectangleUnderCursor();

		if ( canResize )
		{
			if ( HoveredCorner.x != 0 && HoveredCorner.y != 0 )
			{
				Cursor = (HoveredCorner.x == HoveredCorner.y) ? CursorShape.SizeFDiag : CursorShape.SizeBDiag;
			}
			else if ( HoveredCorner.x != 0 )
			{
				Cursor = CursorShape.SizeH;
			}
			else if ( HoveredCorner.y != 0 )
			{
				Cursor = CursorShape.SizeV;
			}
		}
		else if ( DragState == DragState.Dragging && DraggingRectangles.Count > 0 )
		{
			Cursor = CursorShape.ClosedHand;
		}
		else if ( DragState != DragState.Dragging )
		{
			if ( rectUnderCursor is not null )
			{
				Cursor = Document.SelectedRectangles.Contains( rectUnderCursor ) ? CursorShape.OpenHand : CursorShape.Finger;
			}
			else
			{
				Cursor = CursorShape.Cross;
			}
		}
		else
		{
			Cursor = CursorShape.Cross;
		}
	}

	public List<Document.Rectangle> FindRectanglesContainingPoint( Vector2 vPoint )
	{
		return Document.Rectangles
			 .Where( rectangle => rectangle.IsPointInRectangle( vPoint ) )
			 .Select( rectangle => new { Rectangle = rectangle, Distance = rectangle.DistanceFromPointToCenter( vPoint ) } )
			 .OrderBy( item => item.Distance )
			 .Select( item => item.Rectangle )
			 .ToList();
	}

	public Vector2 GetHoveredCornerForRectangle( Document.Rectangle rectangle, Vector2 position )
	{
		var vec = Vector2.Zero;
		var tolerance = 0.02f;

		if ( MathF.Abs( position.x - rectangle.Min.x ) < tolerance )
		{
			vec += new Vector2( -1, 0 );
		}
		else if ( MathF.Abs( position.x - rectangle.Max.x ) < tolerance )
		{
			vec += new Vector2( 1, 0 );
		}

		if ( MathF.Abs( position.y - rectangle.Min.y ) < tolerance )
		{
			vec += new Vector2( 0, -1 );
		}
		else if ( MathF.Abs( position.y - rectangle.Max.y ) < tolerance )
		{
			vec += new Vector2( 0, 1 );
		}

		return vec;
	}

	private void DrawRectangleSet( IEnumerable<Document.Rectangle> rectangles )
	{
		if ( rectangles is null )
			return;

		foreach ( var rectanglesItem in rectangles )
		{
			rectanglesItem?.OnPaint( this );
		}

		var rectangleUnderCursor = GetFirstRectangleUnderCursor();
		foreach ( var rectangle in rectangles.Where( x => !Document.IsRectangleSelected( x ) && x != rectangleUnderCursor ) )
		{
			Paint.SetBrush( rectangle.Color.WithAlpha( 0.2f ) );
			Paint.SetPen( Color.Black.WithAlpha( 192 / 255.0f ), 2 );
			DrawRectangle( rectangle );
		}

		foreach ( var rectangle in Document.SelectedRectangles )
		{
			Paint.SetBrush( Color.White.WithAlpha( 0.1f ) );
			Paint.SetPen( new Color32( 255, 255, 0 ), 3 );
			DrawRectangle( rectangle, corner: (rectangle == rectangleUnderCursor && Document.SelectedRectangles.Count < 2) ? HoveredCorner : 0 );
		}

		if ( rectangleUnderCursor is not null && !Document.IsRectangleSelected( rectangleUnderCursor ) )
		{
			Paint.SetBrush( Color.Yellow.WithAlpha( 0.1f ) );
			Paint.SetPen( Color.Yellow, 2 );
			DrawRectangle( rectangleUnderCursor, corner: HoveredCorner );
		}
	}

	private void DrawRectangle( Document.Rectangle rectangle, int nMinInset = 0, int nMaxInset = 0, Vector2 corner = default )
	{
		if ( rectangle is null )
			return;

		var minPoint = UVToPixel( rectangle.Min );
		var maxPoint = UVToPixel( rectangle.Max );
		minPoint += new Vector2( nMinInset, nMinInset );
		maxPoint -= new Vector2( nMaxInset + 1, nMaxInset + 1 );

		Paint.DrawRect( new Rect( minPoint, maxPoint - minPoint ) );

		// Draw anchor lines if needed
		if ( corner != 0 )
		{
			var penColor = Paint.Pen;
			Paint.SetPen( Color.Orange, 2 );
			if ( corner.x < 0 )
			{
				Paint.DrawLine( minPoint + Vector2.Right + Vector2.Down, minPoint.WithY( maxPoint.y ) + Vector2.Right );
			}
			else if ( corner.x > 0 )
			{
				Paint.DrawLine( maxPoint.WithY( minPoint.y ) + Vector2.Down, maxPoint );
			}
			if ( corner.y < 0 )
			{
				Paint.DrawLine( minPoint + Vector2.Down + Vector2.Right * 2, minPoint.WithX( maxPoint.x ) + Vector2.Down );
			}
			else if ( corner.y > 0 )
			{
				Paint.DrawLine( minPoint.WithY( maxPoint.y ) + Vector2.Right, maxPoint );
			}
			Paint.SetPen( penColor );
		}
	}

	private void DrawGrid( Rect rect )
	{
		const float gridOpacity = 64 / 255.0f;

		var gridCountX = GetGridCountX();
		var gridCountY = GetGridCountY();

		var stepX = 1.0f / gridCountX;
		var stepY = 1.0f / gridCountY;

		Paint.ClearBrush();

		for ( int ix = 0; ix <= gridCountX; ++ix )
		{
			var u = ix * stepX;
			var gx = UVToPixel( new Vector2( u, 0 ) ).x;

			if ( gx > rect.Left )
			{
				Paint.SetPen( new Color( 1, 1, 1, gridOpacity ) );
				Paint.DrawLine( new Vector2( gx - 1, rect.Top + 1 ), new Vector2( gx - 1, rect.Height + rect.Top - 2 ) );
			}

			if ( gx < (rect.Left + rect.Width) )
			{
				Paint.SetPen( new Color( 0, 0, 0, gridOpacity ) );
				Paint.DrawLine( new Vector2( gx, rect.Top + 1 ), new Vector2( gx, rect.Height + rect.Top - 2 ) );
			}
		}

		for ( int iy = 0; iy <= gridCountY; ++iy )
		{
			var v = iy * stepY;
			var gy = UVToPixel( new Vector2( 0, v ) ).y;

			if ( gy > rect.Top )
			{
				Paint.SetPen( new Color( 1, 1, 1, gridOpacity ) );

				if ( gy == (rect.Top + rect.Height) )
				{
					Paint.DrawLine( new Vector2( rect.Left, gy - 1 ), new Vector2( rect.Width + rect.Left - 1, gy - 1 ) );
				}
				else
				{
					Paint.DrawLine( new Vector2( rect.Left + 1, gy - 1 ), new Vector2( rect.Width + rect.Left - 2, gy - 1 ) );
				}

			}

			if ( gy < (rect.Top + rect.Height) )
			{
				Paint.SetPen( new Color( 0, 0, 0, gridOpacity ) );

				if ( gy == rect.Top )
				{
					Paint.DrawLine( new Vector2( rect.Left, gy ), new Vector2( rect.Width + rect.Left - 1, gy ) );
				}
				else
				{
					Paint.DrawLine( new Vector2( rect.Left + 1, gy ), new Vector2( rect.Width + rect.Left - 2, gy ) );
				}
			}
		}
	}

	private void DrawTiledGrid( Rect baseRect )
	{
		const float gridOpacity = 64 / 255.0f;

		var gridCountX = GetGridCountX();
		var gridCountY = GetGridCountY();

		var tileSize = baseRect.Size;
		var viewBounds = LocalRect;

		var stepPixelsX = tileSize.x / gridCountX;
		var stepPixelsY = tileSize.y / gridCountY;

		Paint.ClearBrush();

		var offsetX = baseRect.Left % stepPixelsX;
		var offsetY = baseRect.Top % stepPixelsY;

		for ( float x = viewBounds.Left + offsetX; x <= viewBounds.Right; x += stepPixelsX )
		{
			if ( x < viewBounds.Left )
				continue;

			Paint.SetPen( new Color( 1, 1, 1, gridOpacity ) );
			Paint.DrawLine( new Vector2( x - 1, viewBounds.Top ), new Vector2( x - 1, viewBounds.Bottom ) );

			Paint.SetPen( new Color( 0, 0, 0, gridOpacity ) );
			Paint.DrawLine( new Vector2( x, viewBounds.Top ), new Vector2( x, viewBounds.Bottom ) );
		}

		for ( float y = viewBounds.Top + offsetY; y <= viewBounds.Bottom; y += stepPixelsY )
		{
			if ( y < viewBounds.Top )
				continue;

			Paint.SetPen( new Color( 1, 1, 1, gridOpacity ) );
			Paint.DrawLine( new Vector2( viewBounds.Left, y - 1 ), new Vector2( viewBounds.Right, y - 1 ) );

			Paint.SetPen( new Color( 0, 0, 0, gridOpacity ) );
			Paint.DrawLine( new Vector2( viewBounds.Left, y ), new Vector2( viewBounds.Right, y ) );
		}
	}

	protected override void OnDoubleClick( MouseEvent e )
	{
		base.OnDoubleClick( e );

		if ( e.Button != MouseButtons.Left )
			return;

		var uv = PixelToUV( e.LocalPosition );
		SelectUvAssetRect( uv );
	}

	private void SelectUvAssetRect( Vector2 uv )
	{
		if ( !Session.Settings.IsFastTextureTool )
			return;

		if ( UvAssetRects.Count == 0 )
		{
			ResetUV();
			return;
		}


		var index = GetAssetRectIndexAtUV( uv );
		if ( index < 0 )
			return;

		var assetRect = UvAssetRects[index];
		var assetMin = assetRect.TopLeft;
		var assetMax = assetRect.BottomRight;

		Session.ExecuteUndoableAction( "Apply UV From Asset Rect", () =>
		{
			var meshRect = Document.Rectangles
				.OfType<Document.MeshRectangle>()
				.FirstOrDefault();

			if ( meshRect == null )
				return;

			meshRect.Min = assetMin;
			meshRect.Max = assetMax;

			Document.Modified = true;
			Document.OnModified?.Invoke();
		} );
	}

	public void ResetUV()
	{
		Session.ExecuteUndoableAction( "Apply UV From Asset Rect", () =>
		{
			var meshRect = Document.Rectangles
				.OfType<Document.MeshRectangle>()
				.FirstOrDefault();
			if ( meshRect == null )
				return;
			meshRect.Min = Vector2.Zero;
			meshRect.Max = Vector2.One;
			Document.Modified = true;
			Document.OnModified?.Invoke();
		} );
	}

	public void FocusOnUV()
	{
		//Focus the view on the selected rectangle UVs
		var selectedRect = Document.SelectedRectangles.FirstOrDefault();
		if ( selectedRect is null )
			return;
		var rectCenter = selectedRect.Max + selectedRect.Min;
		rectCenter *= 0.5f;
		PanOffset = rectCenter - (0.5f / ZoomLevel);
		UpdateViewRect();
		Update();

	}

	private Rect GetDrawRect()
	{
		const int marigin = 16;
		const int drawSnapSize = 4;

		var imageSize = SourceImage is null ? 0 : SourceImage.Size;
		var widgetWidth = System.Math.Max( (int)Width - (marigin * 2), 128 );
		var widgetHeight = System.Math.Max( (int)Height - (marigin * 2), 128 );
		var imageWidth = System.Math.Max( (int)imageSize.x, 1 );
		var imageHeight = System.Math.Max( (int)imageSize.y, 1 );

		int drawWidth;
		int drawHeight;

		if ( (imageWidth > 0) && (imageHeight > 0) )
		{
			var aspect = imageWidth / (float)imageHeight;
			var relativeWidth = (int)(widgetWidth / System.MathF.Max( aspect, 1.0f ));
			var relativeHeight = (int)(widgetHeight * System.MathF.Min( aspect, 1.0f ));

			if ( relativeWidth <= relativeHeight )
			{
				drawWidth = widgetWidth;
				drawHeight = widgetWidth * imageHeight / imageWidth;
			}
			else
			{
				drawHeight = widgetHeight;
				drawWidth = widgetHeight * imageWidth / imageHeight;
			}
		}
		else
		{
			var drawSize = System.Math.Min( widgetWidth, widgetHeight );
			drawHeight = drawSize;
			drawWidth = drawSize;
		}

		drawWidth = drawWidth / drawSnapSize * drawSnapSize;
		drawHeight = drawHeight / drawSnapSize * drawSnapSize;

		return new Rect( marigin, marigin, drawWidth, drawHeight );
	}

	private int GetAssetRectIndexAtUV( Vector2 uv )
	{
		for ( int i = 0; i < UvAssetRects.Count; i++ )
		{
			var r = UvAssetRects[i];

			if ( uv.x >= r.Left && uv.x <= r.Right &&
				 uv.y >= r.Top && uv.y <= r.Bottom )
			{
				return i;
			}
		}

		return -1;
	}

	private void DrawUvAssetRectangles()
	{
		if ( UvAssetRects.Count == 0 )
			return;

		Paint.ClearBrush();
		Paint.SetPen( Color.Black.WithAlpha( 0.6f ), 2, style: PenStyle.Dot );

		foreach ( var rect in UvAssetRects )
		{
			var p0 = UVToPixel( rect.TopLeft );
			var p1 = UVToPixel( rect.BottomRight );
			Paint.DrawRect( new Rect( p0, p1 - p0 ) );
		}
	}

	private void UpdateViewRect()
	{
		var size = 1.0f / ZoomLevel;
		ViewRect = new Rect(
			PanOffset.x,
			PanOffset.y,
			size,
			size
		);
	}

	public void ResetView()
	{
		ZoomLevel = 1.0f;
		PanOffset = Vector2.Zero;
		UpdateViewRect();
		Update();
	}

	public void FitView()
	{
		ZoomLevel = 1.0f;
		PanOffset = Vector2.Zero;
		UpdateViewRect();
		Update();
	}
}
