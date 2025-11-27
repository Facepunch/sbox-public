using Sandbox;

namespace Editor;

/// <summary>
/// Represents a single layer item in the LayerPanel.
/// </summary>
public class LayerPanelItem : Widget
{
	public Layer Layer { get; }
	public LayerPanel Panel { get; }

	public int Indent { get; set; }
	public bool IsSelected { get; set; }

	public Action OnSelected;
	public Action OnVisibilityToggled;
	public Action OnLockToggled;
	public Action OnDeleted;
	public Action<string> OnRenamed;
	public Action<Color> OnColorChanged;

	private bool _isRenaming;
	private LineEdit _renameEdit;

	private const float RowHeight = 28f;
	private const float IndentSize = 16f;
	private const float IconSize = 18f;
	private const float ColorIndicatorWidth = 4f;

	public LayerPanelItem( Layer layer, LayerPanel panel ) : base( null )
	{
		Layer = layer;
		Panel = panel;

		FixedHeight = RowHeight;
		Cursor = CursorShape.Finger;

		Layout = Layout.Row();
		Layout.Spacing = 4;
		Layout.Margin = new Margin( 4, 2 );
	}

	protected override void OnPaint()
	{
		var rect = LocalRect;

		// Background
		var bgColor = IsSelected ? Theme.Selection : Theme.ControlBackground;
		if ( !IsSelected && Paint.HasMouseOver )
		{
			bgColor = bgColor.Lighten( 0.05f );
		}

		Paint.ClearPen();
		Paint.SetBrush( bgColor );
		Paint.DrawRect( rect, Theme.ControlRadius );

		// Color indicator on left edge
		var colorRect = new Rect( rect.Left, rect.Top, ColorIndicatorWidth, rect.Height );
		Paint.SetBrush( Layer.Color );
		Paint.DrawRect( colorRect, new Vector4( Theme.ControlRadius, 0, 0, Theme.ControlRadius ) );

		// Calculate content area with indent
		var contentLeft = ColorIndicatorWidth + 4 + ( Indent * IndentSize );
		var contentRect = rect.Shrink( contentLeft, 0, 0, 0 );

		// Group expand/collapse icon
		if ( Layer.IsGroup )
		{
			var expandRect = new Rect( contentLeft, rect.Top, IconSize, rect.Height );
			Paint.SetPen( Theme.ControlText );
			Paint.DrawIcon( expandRect, Layer.IsExpanded ? "expand_more" : "chevron_right", 14, TextFlag.Center );
			contentLeft += IconSize;
		}

		// Visibility icon
		var visibilityRect = new Rect( contentLeft, rect.Top, IconSize, rect.Height );
		var visibilityIcon = Layer.Visible ? "visibility" : "visibility_off";
		var visibilityColor = Layer.Visible ? Theme.ControlText : Theme.ControlText.WithAlpha( 0.3f );
		Paint.SetPen( visibilityColor );
		Paint.DrawIcon( visibilityRect, visibilityIcon, 14, TextFlag.Center );
		contentLeft += IconSize + 2;

		// Lock icon
		var lockRect = new Rect( contentLeft, rect.Top, IconSize, rect.Height );
		var lockIcon = Layer.Locked ? "lock" : "lock_open";
		var lockColor = Layer.Locked ? Theme.Yellow : Theme.ControlText.WithAlpha( 0.3f );
		Paint.SetPen( lockColor );
		Paint.DrawIcon( lockRect, lockIcon, 14, TextFlag.Center );
		contentLeft += IconSize + 4;

		// Layer name
		if ( !_isRenaming )
		{
			var nameRect = new Rect( contentLeft, rect.Top, rect.Width - contentLeft - 40, rect.Height );
			var textColor = Layer.Visible ? Theme.ControlText : Theme.ControlText.WithAlpha( 0.5f );
			Paint.SetPen( textColor );
			Paint.SetDefaultFont( 8, 400 );
			Paint.DrawText( nameRect, Layer.Name, TextFlag.LeftCenter );
		}

		// Member count badge
		var members = Layer.GetMembers();
		if ( members.Count > 0 )
		{
			var badgeText = members.Count.ToString();
			var badgeRect = new Rect( rect.Right - 30, rect.Top + 6, 24, 16 );
			Paint.SetBrush( Theme.Primary.WithAlpha( 0.3f ) );
			Paint.ClearPen();
			Paint.DrawRect( badgeRect, 8 );
			Paint.SetPen( Theme.ControlText );
			Paint.SetDefaultFont( 7, 400 );
			Paint.DrawText( badgeRect, badgeText, TextFlag.Center );
		}

		// Opacity indicator if not fully opaque
		if ( Layer.Opacity < 1f )
		{
			var opacityText = $"{(int)( Layer.Opacity * 100 )}%";
			var opacityRect = new Rect( rect.Right - 60, rect.Top, 28, rect.Height );
			Paint.SetPen( Theme.ControlText.WithAlpha( 0.5f ) );
			Paint.SetDefaultFont( 7, 400 );
			Paint.DrawText( opacityRect, opacityText, TextFlag.RightCenter );
		}
	}

	protected override void OnMousePress( MouseEvent e )
	{
		if ( e.LeftMouseButton )
		{
			var rect = LocalRect;
			var contentLeft = ColorIndicatorWidth + 4 + ( Indent * IndentSize );

			// Check if clicking on group expand icon
			if ( Layer.IsGroup )
			{
				var expandRect = new Rect( contentLeft, rect.Top, IconSize, rect.Height );
				if ( expandRect.IsInside( e.LocalPosition ) )
				{
					Layer.IsExpanded = !Layer.IsExpanded;
					Panel.Update();
					return;
				}
				contentLeft += IconSize;
			}

			// Check if clicking visibility icon
			var visibilityRect = new Rect( contentLeft, rect.Top, IconSize, rect.Height );
			if ( visibilityRect.IsInside( e.LocalPosition ) )
			{
				// Alt+click to solo
				if ( e.HasAlt )
				{
					SoloLayer();
				}
				else
				{
					OnVisibilityToggled?.Invoke();
				}
				return;
			}
			contentLeft += IconSize + 2;

			// Check if clicking lock icon
			var lockRect = new Rect( contentLeft, rect.Top, IconSize, rect.Height );
			if ( lockRect.IsInside( e.LocalPosition ) )
			{
				OnLockToggled?.Invoke();
				return;
			}

			// Otherwise select the layer
			OnSelected?.Invoke();
		}
	}

	protected override void OnDoubleClick( MouseEvent e )
	{
		if ( e.LeftMouseButton )
		{
			StartRename();
		}
	}

	protected override void OnContextMenu( ContextMenuEvent e )
	{
		Panel.OpenContextMenu( Layer );
		e.Accepted = true;
	}

	private void SoloLayer()
	{
		var manager = LayerManager.Get( Layer.Manager?.Scene );
		if ( manager is not null )
		{
			manager.Solo( Layer.Name );
		}
	}

	private void StartRename()
	{
		if ( _isRenaming ) return;

		_isRenaming = true;

		var contentLeft = ColorIndicatorWidth + 4 + ( Indent * IndentSize );
		if ( Layer.IsGroup ) contentLeft += IconSize;
		contentLeft += IconSize + 2 + IconSize + 4; // visibility + lock

		_renameEdit = new LineEdit( this );
		_renameEdit.Text = Layer.Name;
		_renameEdit.Position = new Vector2( contentLeft, 2 );
		_renameEdit.Size = new Vector2( Width - contentLeft - 40, Height - 4 );
		_renameEdit.Focus();
		_renameEdit.SelectAll();

		_renameEdit.EditingFinished += FinishRename;
		_renameEdit.OnKeyPress += e =>
		{
			if ( e.Key == KeyCode.Escape )
			{
				CancelRename();
				e.Accepted = true;
			}
		};
	}

	private void FinishRename()
	{
		if ( !_isRenaming ) return;

		var newName = _renameEdit?.Text;
		CancelRename();

		if ( !string.IsNullOrWhiteSpace( newName ) && newName != Layer.Name )
		{
			OnRenamed?.Invoke( newName );
		}
	}

	private void CancelRename()
	{
		_isRenaming = false;
		_renameEdit?.Destroy();
		_renameEdit = null;
		Update();
	}

	protected override void OnKeyPress( KeyEvent e )
	{
		if ( e.Key == KeyCode.Delete && IsSelected )
		{
			OnDeleted?.Invoke();
			e.Accepted = true;
		}
		else if ( e.Key == KeyCode.F2 && IsSelected )
		{
			StartRename();
			e.Accepted = true;
		}
	}
}
