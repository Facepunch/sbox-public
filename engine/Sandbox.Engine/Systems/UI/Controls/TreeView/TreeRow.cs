using Sandbox.UI.Construct;

namespace Sandbox.UI;

/// <summary>
/// One visible row of a <see cref="BaseTreeView"/>. Rows are pooled and rebound as the tree
/// scrolls, so anything you add to one must be created once and updated on every bind.
/// </summary>
public class TreeRow : Panel
{
	/// <summary>
	/// The tree this row belongs to.
	/// </summary>
	public BaseTreeView Tree { get; internal set; }

	/// <summary>
	/// Index into the tree's flat row list, or -1 while pooled.
	/// </summary>
	public int RowIndex { get; internal set; } = -1;

	/// <summary>
	/// The expand/collapse arrow. Hidden on rows without children.
	/// </summary>
	public IconPanel Expander { get; }

	/// <summary>
	/// An optional icon before the label.
	/// </summary>
	public IconPanel Icon { get; }

	/// <summary>
	/// The row's text.
	/// </summary>
	public Label Label { get; }

	/// <summary>
	/// Where a row's own controls go - toggles, buttons, badges - added once and updated on every
	/// bind. Clicks on them belong to them: they don't select or activate the row.
	/// </summary>
	public Panel Content { get; }

	internal int BindVersion = -1;

	TextEntry _rename;
	float _indent = -1;
	bool _iconVisible = true;

	public TreeRow()
	{
		AddClass( "tree-row" );
		Style.Position = PositionMode.Absolute;

		Expander = Add.Icon( "arrow_right", "tree-expander" );
		Icon = Add.Icon( null, "tree-icon" );
		Label = Add.Label( null, "tree-label" );
		Content = Add.Panel( "tree-content" );
	}

	/// <summary>
	/// The row's label text. Only touches the label when it changes.
	/// </summary>
	public string Text
	{
		get => Label.Text;
		set
		{
			if ( Label.Text == value ) return;
			Label.Text = value;
		}
	}

	/// <summary>
	/// Material icon name. Null or empty hides the icon.
	/// </summary>
	public string IconName
	{
		get => Icon.Text;
		set
		{
			if ( Icon.Text != value ) Icon.Text = value;

			var visible = !string.IsNullOrEmpty( value );
			if ( visible == _iconVisible ) return;

			_iconVisible = visible;
			Icon.Style.Display = visible ? DisplayMode.Flex : DisplayMode.None;
			Icon.Style.Dirty();
		}
	}

	/// <summary>
	/// Rows drag when the tree says this row can.
	/// </summary>
	public override bool WantsDrag => Tree is not null && RowIndex >= 0 && Tree.CanDragRow( RowIndex );

	internal void SetState( int depth, bool hasChildren, bool isOpen, bool selected, float indentWidth )
	{
		SetClass( "has-children", hasChildren );
		SetClass( "open", isOpen );
		SetClass( "selected", selected );

		var indent = depth * indentWidth;
		if ( indent == _indent ) return;

		_indent = indent;
		Style.PaddingLeft = indent;
		Style.Dirty();
	}

	internal void SetRect( float left, float top, float width, float height )
	{
		var style = Style;
		bool dirty = false;

		if ( style.Left?.Value != left ) { style.Left = left; dirty = true; }
		if ( style.Top?.Value != top ) { style.Top = top; dirty = true; }
		if ( style.Width?.Value != width ) { style.Width = width; dirty = true; }
		if ( style.Height?.Value != height ) { style.Height = height; dirty = true; }

		if ( dirty ) style.Dirty();
	}

	/// <summary>
	/// Back to the pool. Parked far above the content rather than hidden: a hidden label re-lays
	/// out its text when shown again, a moved one doesn't.
	/// </summary>
	internal void Unbind()
	{
		EndRename();
		RowIndex = -1;
		BindVersion = -1;
		RemoveClass( "drop-target" );
		RemoveClass( "dragging" );

		if ( Style.Top?.Value != ParkedTop )
		{
			Style.Top = ParkedTop;
			Style.Dirty();
		}
	}

	internal const float ParkedTop = -100000;

	/// <summary>
	/// Swap the label for a text entry until enter, escape, or focus loss.
	/// </summary>
	public void BeginRename()
	{
		if ( _rename.IsValid() ) return;

		_rename = new TextEntry();
		_rename.AddClass( "rename" );
		_rename.Text = Text;
		AddChild( _rename );
		SetChildIndex( _rename, GetChildIndex( Label ) + 1 );

		Label.Style.Display = DisplayMode.None;
		Label.Style.Dirty();
		AddClass( "renaming" );

		_rename.AddEventListener( "onsubmit", e => SubmitRename( e.Value as string ) );
		_rename.AddEventListener( "oncancel", EndRename );
		_rename.AddEventListener( "onblur", EndRename );

		_rename.Focus();
		_rename.CaretPosition = _rename.TextLength;
	}

	void SubmitRename( string text )
	{
		var row = RowIndex;
		EndRename();

		if ( row < 0 || string.IsNullOrWhiteSpace( text ) ) return;
		Tree?.RowRenamed( row, text );
	}

	void EndRename()
	{
		if ( !_rename.IsValid() ) return;

		var entry = _rename;
		_rename = null;
		entry.Delete( true );

		Label.Style.Display = null;
		Label.Style.Dirty();
		RemoveClass( "renaming" );
	}

	/// <summary>
	/// True while the label is swapped for a text entry.
	/// </summary>
	public bool IsRenaming => _rename.IsValid();

	/// <summary>
	/// Did this event start on a control inside <see cref="Content"/>?
	/// </summary>
	bool IsOnContent( PanelEvent e )
	{
		return e.Target != Content && e.Target.IsValid() && e.Target.IsAncestor( Content );
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		if ( RowIndex < 0 ) return;
		if ( IsOnContent( e ) ) return;
		Tree?.Focus();
	}

	protected override void OnClick( MousePanelEvent e )
	{
		if ( RowIndex < 0 || Tree is null ) return;
		if ( IsOnContent( e ) ) return;

		if ( e.Target == Expander )
		{
			Tree.ToggleRow( RowIndex, e.HasShift );
			e.StopPropagation();
			return;
		}

		Tree.RowClicked( RowIndex, e );
		e.StopPropagation();
	}

	protected override void OnDoubleClick( MousePanelEvent e )
	{
		if ( RowIndex < 0 || Tree is null ) return;
		if ( e.Target == Expander || IsOnContent( e ) ) return;

		Tree.RowDoubleClicked( RowIndex );
		e.StopPropagation();
	}

	protected override void OnRightClick( MousePanelEvent e )
	{
		if ( RowIndex < 0 || Tree is null ) return;

		Tree.RowRightClicked( RowIndex );
		e.StopPropagation();
	}

	protected override void OnDragStart( DragEvent e )
	{
		AddClass( "dragging" );
		Tree?.SetDragging( true );
		e.StopPropagation();
	}

	protected override void OnDrag( DragEvent e )
	{
		e.StopPropagation();
	}

	protected override void OnDragEnd( DragEvent e )
	{
		RemoveClass( "dragging" );
		Tree?.SetDragging( false );
		e.StopPropagation();
	}

	protected override void OnDragEnter( PanelEvent e )
	{
		if ( !IsSiblingDrag( e ) ) return;
		AddClass( "drop-target" );
	}

	protected override void OnDragLeave( PanelEvent e )
	{
		RemoveClass( "drop-target" );
	}

	protected override void OnDrop( PanelEvent e )
	{
		RemoveClass( "drop-target" );

		if ( !IsSiblingDrag( e ) ) return;

		var source = (TreeRow)e.Target;
		Tree.RowDropped( RowIndex, source.RowIndex );
		e.StopPropagation();
	}

	/// <summary>
	/// Is this a row of the same tree being dragged over us? External drops and other panels don't count.
	/// </summary>
	bool IsSiblingDrag( PanelEvent e )
	{
		if ( e is DropEvent ) return false;
		if ( RowIndex < 0 || Tree is null ) return false;
		return e.Target is TreeRow source && source != this && source.Tree == Tree && source.RowIndex >= 0;
	}
}
