namespace Editor;

public abstract class StickyPopupControlWidget : ControlWidget
{
	protected StickyPopup _popup;
	protected Layout _toolbarRow;
	protected ToolBar _toolbar;

	bool _isInlineEditor;
	bool _showInlineEditorLabel;
	protected bool IsInlineEditor => _isInlineEditor;

	public override bool IsWideMode => _isInlineEditor;
	public override bool IncludeLabel => !_isInlineEditor;

	protected StickyPopupControlWidget( SerializedProperty property ) : base( property )
	{
		HorizontalSizeMode = SizeMode.CanGrow | SizeMode.Expand;
		VerticalSizeMode = SizeMode.CanGrow;
		Layout = Layout.Column();
		Cursor = CursorShape.Finger;

		if ( property.TryGetAttribute<InlineEditorAttribute>( out var inlineEditor ) )
		{
			_isInlineEditor = true;
			_showInlineEditorLabel = inlineEditor.Label;
		}
	}

	protected abstract void BuildEditor( Widget target, bool isPopup );

	protected void InitializeInlineEditor()
	{
		if ( !IsInlineEditor ) return;

		BuildEditor( this, false );
	}

	protected void RebuildEditor()
	{
		if ( IsInlineEditor )
		{
			BuildEditor( this, false );
			return;
		}

		if ( _popup.IsValid() )
			BuildEditor( _popup, true );
	}

	protected void PrepareEditor( Widget target, bool isPopup )
	{
		target.Layout.Clear( true );
		target.ReadOnly = !SerializedProperty.IsEditable;

		if ( isPopup )
		{
			var popup = (StickyPopup)target;
			popup.OnPaintOverride = PaintPopupBackground;
		}
		else if ( _showInlineEditorLabel && SerializedProperty.Parent is not SerializedCollection )
		{
			target.Layout.Add( ControlSheet.CreateLabel( SerializedProperty ) );
		}

		_toolbarRow = target.Layout.AddRow();
		_toolbarRow.Spacing = 4;

		_toolbar = new ToolBar( target );
		_toolbar.SetIconSize( 13 );
		_toolbar.ButtonStyle = ToolButtonStyle.TextUnderIcon;
	}

	protected void FinishEditor( Widget target, bool isPopup, SerializedObject serializedObject )
	{
		_toolbarRow.Add( _toolbar );
		if ( !isPopup )
			target.Layout.AddSeparator();

		if ( !serializedObject.IsValid() ) return;

		if ( isPopup )
		{
			((StickyPopup)target).CreateProperties( serializedObject );
			return;
		}

		var inspector = InspectorWidget.Create( serializedObject );
		if ( inspector.IsValid() )
			target.Layout.Add( inspector, 1 );
		else
		{
			var sheet = ControlSheet.Create( serializedObject );
			sheet.Margin = 0;
			target.Layout.Add( sheet, 1 );
		}

		target.Layout.AddStretchCell();
	}

	protected void AddClipboardOptions( Widget target, Action rebuildEditor )
	{
		_toolbar.AddWidget( new Widget( _toolbar ) { HorizontalSizeMode = SizeMode.CanGrow | SizeMode.Expand } );
		_toolbar.AddOption( "Copy", "content_copy", action: Copy );
		_toolbar.AddOption( "Paste", "content_paste", action: () => Paste( rebuildEditor ) ).Enabled = !target.ReadOnly;
	}

	void Copy()
	{
		EditorUtility.Clipboard.Copy( ToClipboardString() );
	}

	void Paste( Action rebuildEditor )
	{
		PropertyStartEdit();
		FromClipboardString( EditorUtility.Clipboard.Paste() );
		rebuildEditor?.Invoke();
		SignalValuesChanged();
		PropertyFinishEdit();
	}

	protected void OpenPopupEditor()
	{
		_popup?.Destroy();
		_popup = null;

		var popup = new StickyPopup( null )
		{
			Owner = this,
			MinimumWidth = Width,
			Position = ScreenRect.BottomLeft
		};

		BuildEditor( popup, true );
		popup.Visible = true;
		popup.Focus( true );

		_popup = popup;
		_popup.DestroyUnrelatedPopups();
	}

	protected override void OnMouseReleased( MouseEvent e )
	{
		if ( IsInlineEditor ) return;

		if ( _popup.IsValid() )
		{
			_popup.Destroy();
			_popup = null;
			return;
		}

		OpenPopupEditor();
	}

	public override void OnDestroyed()
	{
		_popup?.Destroy();
		_popup = null;

		base.OnDestroyed();
	}

	protected bool PaintPopupBackground()
	{
		Paint.ClearPen();
		Paint.SetBrushLinear( 0, Vector2.Down * 256, Theme.SurfaceBackground.Lighten( 0.2f ).WithAlpha( 0.98f ), Theme.SurfaceBackground.WithAlpha( 0.95f ) );
		Paint.DrawRect( Paint.LocalRect );

		var toolbarColor = SerializedProperty.IsEditable ? Theme.Green : Theme.Green.Desaturate( 0.5f );
		Paint.ClearPen();
		Paint.SetBrush( toolbarColor.WithAlpha( 0.1f ) );
		Paint.DrawRect( _toolbarRow.OuterRect );

		Paint.ClearBrush();
		Paint.SetPen( Color.Black.WithAlpha( 0.33f ), 2, PenStyle.Solid );
		Paint.DrawRect( Paint.LocalRect.Shrink( 0, -10, 1, 1 ), 4 );

		return true;
	}

	protected void PaintInlineEditorHeader()
	{
		Paint.ClearPen();
		Paint.SetBrush( Theme.ControlBackground );
		Paint.DrawRect( _toolbarRow.OuterRect, Theme.ControlRadius );
	}
}
