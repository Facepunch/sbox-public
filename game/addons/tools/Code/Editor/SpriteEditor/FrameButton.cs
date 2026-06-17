namespace Editor.SpriteEditor;

public class FrameButton : Widget
{
	private Window SpriteEditor;
	private Timeline Timeline;

	public bool IsCurrentFrame => SpriteEditor.CurrentFrameIndex == FrameIndex;
	public Sprite.Frame Frame => SpriteEditor.SelectedAnimation?.Frames[FrameIndex];

	public int FrameIndex;
	private Pixmap Pixmap;

	Drag dragData;
	bool draggingBehind = false;
	bool draggingAhead = false;
	private int lastHash = 0;

	public static float FrameSize = 1f;
	private static Dictionary<Sprite.BroadcastEventType, string> _enumIconCache = new();

	public FrameButton( Timeline timeline, int index ) : base( null )
	{
		SpriteEditor = timeline.SpriteEditor;
		Timeline = timeline;
		FrameIndex = index;
		Cursor = CursorShape.Finger;

		FixedSize = new Vector2( 16, 32f );
		HorizontalSizeMode = SizeMode.Ignore;
		VerticalSizeMode = SizeMode.Ignore;

		// Get the texture for the frame
		var texture = Frame?.Texture;
		if ( texture is not null )
		{
			Pixmap = Pixmap.FromTexture( texture );
		}

		StatusTip = $"Frame {FrameIndex}";

		IsDraggable = true;
		AcceptDrops = true;
	}

	protected override void OnContextMenu( ContextMenuEvent e )
	{
		base.OnContextMenu( e );

		var m = new Menu( this );

		m.AddOption( "Edit Frame", "edit", OpenEditDialog );

		if ( Timeline.SelectedFrames.Contains( this ) )
		{
			m.AddOption( "Duplicate", "content_copy", Timeline.DuplicateSelection );
			m.AddOption( "Delete", "delete", Timeline.DeleteSelection );
		}
		else
		{
			// When right-clicking a frame that isn't selected, only effect that frame
			m.AddOption( "Duplicate", "content_copy", Duplicate );
			m.AddOption( "Delete", "delete", Delete );
		}

		m.OpenAtCursor( false );

		e.Accepted = true;
	}

	protected override void OnPaint()
	{
		bool isSelected = Timeline.SelectedFrames.Contains( this );
		var anim = SpriteEditor.SelectedAnimation;
		var frameSize = MathF.Max( (Timeline.Height - 60f) * FrameSize, 1 );
		FixedSize = new Vector2( frameSize, frameSize + 16f );

		var controlColor = IsUnderMouse ? Theme.Highlight : Theme.ButtonBackground;
		if ( isSelected ) controlColor = Theme.Primary.WithAlpha( 0.2f );
		Paint.SetBrushAndPen( controlColor );
		Paint.DrawRect( LocalRect );

		Paint.SetBrushAndPen( Theme.ControlBackground );
		Paint.DrawRect( Rect.FromPoints( LocalRect.TopLeft.WithY( 16f ), LocalRect.BottomRight ).Shrink( 4 ) );
		if ( IsCurrentFrame )
		{
			Paint.SetBrushAndPen( Theme.Primary.WithAlpha( 0.5f ).WithAlpha( 0.5f ) );
			Paint.DrawRect( Rect.FromPoints( LocalRect.TopLeft, LocalRect.BottomRight.WithY( 20f ) ) );
		}

		Paint.SetPen( Theme.Text );
		var rect = Rect.FromPoints( LocalRect.TopLeft + Vector2.Down * 4f, LocalRect.BottomRight.WithY( 16f ) );
		Paint.DrawText( rect, FrameIndex.ToString(), TextFlag.Center );

		if ( dragData?.IsValid ?? false )
		{
			Paint.SetBrushAndPen( Theme.WindowBackground.WithAlpha( 0.5f ) );
			Paint.DrawRect( LocalRect );
		}

		var frame = Frame;
		var pixRect = Rect.FromPoints( LocalRect.TopLeft + Vector2.Down * 16f, LocalRect.BottomRight ).Shrink( 4 );
		if ( Pixmap is not null )
		{
			// Draw the Texture, scaled to fit (with proper aspect ratio)
			var aspectRatio = Pixmap.Width / (float)Pixmap.Height;
			if ( aspectRatio > 1f )
			{
				// Pixmap is wider than tall
				float newHeight = pixRect.Width / aspectRatio;
				float yOffset = (pixRect.Height - newHeight) / 2f;
				pixRect.Top += yOffset;
				pixRect.Bottom = pixRect.Top + newHeight;
			}
			else
			{
				// Pixmap is taller than wide
				float newWidth = pixRect.Height * aspectRatio;
				float xOffset = (pixRect.Width - newWidth) / 2f;
				pixRect.Left += xOffset;
				pixRect.Right = pixRect.Left + newWidth;
			}
			Paint.Draw( pixRect, Pixmap );
		}
		if ( frame is not null )
		{
			// Draw Broadcast Messages
			if ( (frame?.BroadcastMessages?.Count ?? 0) > 0 )
			{
				Paint.SetDefaultFont( 8 * FrameSize );
				var tagsText = string.Join( ", ", frame.BroadcastMessages.Distinct() );
				var iconText = string.Join( " ", frame.BroadcastMessages.Select( x => GetEnumIcon( x.Type ) ).Distinct() ).Trim();
				if ( string.IsNullOrEmpty( tagsText ) ) tagsText = " ";
				var tagsRect = Paint.MeasureText( pixRect, tagsText, TextFlag.CenterBottom );
				var iconOffset = new Vector2( tagsRect.Size.y + 6 * FrameSize, 0 );
				tagsRect.Left = pixRect.Left;
				tagsRect.Right = pixRect.Right;
				tagsRect.Top -= 5 * FrameSize;
				tagsRect.Bottom = pixRect.Bottom;
				Paint.ClearPen();
				Paint.SetBrush( Theme.Primary.WithAlpha( 0.4f ) );
				Paint.DrawRect( tagsRect );
				Paint.ClearBrush();
				Paint.SetPen( Theme.Text );
				tagsRect.Left += 4 * FrameSize;
				var iconRect = Paint.DrawIcon( tagsRect, iconText, 12 * FrameSize, TextFlag.LeftCenter );
				tagsRect.Left += iconRect.Width + 4 * FrameSize;
				tagsRect.Bottom -= 2f * FrameSize;
				Paint.DrawText( tagsRect, tagsText, TextFlag.LeftCenter );
			}
		}

		base.OnPaint();

		// Draw drag indicators
		if ( draggingBehind )
		{
			Paint.SetPen( Theme.Primary, 2f, PenStyle.Dot );
			Paint.DrawLine( LocalRect.TopLeft, LocalRect.BottomLeft );
		}
		else if ( draggingAhead )
		{
			Paint.SetPen( Theme.Primary, 2f, PenStyle.Dot );
			Paint.DrawLine( LocalRect.TopRight, LocalRect.BottomRight );
		}
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		base.OnMouseClick( e );

		if ( e.Button != MouseButtons.Left ) return;

		Timeline.CurrentFrame = FrameIndex;

		if ( e.HasShift && Timeline.LastSelectedIndex > -1 )
		{
			Timeline.SelectedFrames.Clear();
			var start = Math.Min( Timeline.LastSelectedIndex, FrameIndex );
			var end = Math.Max( Timeline.LastSelectedIndex, FrameIndex );
			for ( int i = start; i <= end; i++ )
			{
				var btn = Timeline.FrameButtons.ElementAt( i );
				if ( btn is not null )
				{
					Timeline.SelectedFrames.Add( btn );
				}
			}
		}
		else
		{
			if ( !e.HasCtrl )
			{
				Timeline.SelectedFrames.Clear();
			}

			Timeline.SelectedFrames.Add( this );
			Timeline.LastSelectedIndex = FrameIndex;
		}
	}

	protected override void OnDoubleClick( MouseEvent e )
	{
		base.OnDoubleClick( e );

		if ( e.Button != MouseButtons.Left ) return;

		OpenEditDialog();
	}

	protected override void OnDragStart()
	{
		base.OnDragStart();

		dragData = new Drag( this );

		var newDrag = new DragData();
		newDrag.DraggingIndex = FrameIndex;
		if ( Timeline.SelectedFrames.Contains( this ) )
		{
			foreach ( var frame in Timeline.SelectedFrames )
			{
				newDrag.Frames.Add( frame );
			}
		}
		else
		{
			newDrag.Frames.Add( this );
		}
		dragData.Data.Object = newDrag;

		dragData.Execute();
	}

	public override void OnDragHover( DragEvent ev )
	{
		base.OnDragHover( ev );

		if ( !TryDragOperation( ev, out var dragDelta ) )
		{
			draggingBehind = false;
			draggingAhead = false;
			return;
		}

		draggingBehind = dragDelta > 0;
		draggingAhead = dragDelta < 0;
	}

	public override void OnDragDrop( DragEvent ev )
	{
		base.OnDragDrop( ev );
		ResetDrag();

		if ( !TryDragOperation( ev, out var delta ) )
		{
			return;
		}

		Move( ev, delta );
	}

	public override void OnDragLeave()
	{
		base.OnDragLeave();
		ResetDrag();
	}

	void ResetDrag()
	{
		draggingBehind = false;
		draggingAhead = false;
	}

	bool TryDragOperation( DragEvent ev, out int dragDelta )
	{
		dragDelta = 0;
		var dragging = ev.Data.OfType<DragData>().FirstOrDefault();
		if ( dragging is null ) return false;
		var otherIndex = dragging.DraggingIndex;

		if ( otherIndex < 0 || FrameIndex < 0 || SpriteEditor.SelectedAnimation is null || FrameIndex == otherIndex )
		{
			return false;
		}

		dragDelta = otherIndex - FrameIndex;
		return true;
	}

	void Move( DragEvent ev, int delta )
	{
		var dragging = ev.Data.OfType<DragData>().FirstOrDefault();
		if ( dragging is null ) return;

		SpriteEditor.ExecuteUndoableAction( $"Reorder {dragging.Frames.Count} Frames", () =>
		{
			var targetIndex = FrameIndex;
			var framesToMove = new List<Sprite.Frame>();

			// Determine if we're moving to the right (forward) or left (backward)
			bool movingRight = delta < 0; // delta < 0 means dragging from higher index to lower index (moving right)

			// If moving right, we need to insert after the target, so increment target index
			if ( movingRight )
			{
				targetIndex++;
			}

			// Remove all frames first
			foreach ( var frameBtn in dragging.Frames.OrderByDescending( x => x.FrameIndex ) )
			{
				if ( frameBtn.FrameIndex < 0 || frameBtn.FrameIndex >= SpriteEditor.SelectedAnimation.Frames.Count )
					continue;
				var frame = SpriteEditor.SelectedAnimation.Frames[frameBtn.FrameIndex];
				framesToMove.Add( frame );
				SpriteEditor.SelectedAnimation.Frames.RemoveAt( frameBtn.FrameIndex );

				// Only adjust target index for frames that were before the original target
				if ( frameBtn.FrameIndex < FrameIndex )
				{
					targetIndex--;
				}
			}
			framesToMove.Reverse();

			// Insert at the target index
			foreach ( var frame in framesToMove )
			{
				SpriteEditor.SelectedAnimation.Frames.Insert( targetIndex, frame );
				targetIndex++;
			}

			SpriteEditor?.OnFramesChanged?.Invoke();
			Timeline.SelectedFrames.Clear();
			Timeline.SelectedFrames.UnionWith( Timeline.FrameButtons.Where( x => framesToMove.Contains( x.Frame ) ) );
		} );
	}

	private void OpenEditDialog()
	{
		var windowTitle = $"Editing Frame {FrameIndex}";
		var serializedObject = new MultiSerializedObject();
		if ( Timeline.SelectedFrames.Contains( this ) )
		{
			foreach ( var frame in Timeline.SelectedFrames )
			{
				serializedObject.Add( frame.Frame.GetSerialized() );
			}
			if ( Timeline.SelectedFrames.Count > 1 )
			{
				windowTitle = $"Editing {Timeline.SelectedFrames.Count} Frames";
			}
		}
		else
		{
			serializedObject.Add( Frame.GetSerialized() );
		}
		serializedObject.Rebuild();

		var popup = new Dialog( this );
		popup.SetSizeMode( SizeMode.Default, SizeMode.CanShrink );
		popup.Window.SetSizeMode( SizeMode.Default, SizeMode.CanShrink );
		popup.Window.Title = windowTitle;
		popup.Window.SetWindowIcon( "info" );
		popup.Window.SetModal( true );
		popup.Window.Height = 200;
		popup.Layout = Layout.Column();
		popup.Layout.Margin = 16;

		var scroller = new ScrollArea( popup );
		scroller.Canvas = new Widget();
		scroller.Canvas.Layout = Layout.Column();
		scroller.Canvas.HorizontalSizeMode = SizeMode.Flexible;
		popup.Layout.Add( scroller );

		var controlSheet = new ControlSheet();
		controlSheet.AddObject( serializedObject );
		scroller.Canvas.Layout.Add( controlSheet );
		scroller.Canvas.Layout.AddStretchCell();

		popup.Show();
	}

	private void Duplicate()
	{
		SpriteEditor.ExecuteUndoableAction( $"Duplicate Frame {FrameIndex}", () =>
		{
			var frameJson = Json.Serialize( SpriteEditor.SelectedAnimation.Frames[FrameIndex] );
			var newFrame = Json.Deserialize<Sprite.Frame>( frameJson );
			SpriteEditor.SelectedAnimation.Frames.Insert( FrameIndex, newFrame );
		} );

		SpriteEditor?.OnSpriteModified?.Invoke();
	}

	private void Delete()
	{
		SpriteEditor.ExecuteUndoableAction( $"Delete Frame {FrameIndex}", () =>
		{
			if ( SpriteEditor.SelectedAnimation.Frames.Count < 1 )
				return;
			SpriteEditor.SelectedAnimation.Frames.RemoveAt( FrameIndex );
			if ( SpriteEditor.CurrentFrameIndex >= SpriteEditor.SelectedAnimation.Frames.Count )
				SpriteEditor.CurrentFrameIndex = SpriteEditor.SelectedAnimation.Frames.Count - 1;
		} );

		SpriteEditor?.OnSpriteModified?.Invoke();
	}

	private static string GetEnumIcon( Sprite.BroadcastEventType type )
	{
		if ( _enumIconCache.TryGetValue( type, out var icon ) )
		{
			return icon;
		}
		var newIcon = EditorTypeLibrary.GetEnumDescription( type.GetType() ).GetEntry( type ).Icon;
		_enumIconCache[type] = newIcon;
		return newIcon;
	}

	[EditorEvent.Frame]
	private void OnFrame()
	{
		var hash = System.HashCode.Combine( IsCurrentFrame );
		if ( hash != lastHash )
		{
			lastHash = hash;
			Update();
		}
	}

	class DragData
	{
		public List<FrameButton> Frames = new();
		public int DraggingIndex;
	}
}
