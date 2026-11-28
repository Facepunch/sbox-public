using Microsoft.AspNetCore.Components;
using Sandbox.UI.Layout;

namespace Sandbox.UI;

/// <summary>
/// A virtualized, scrollable list panel that only creates item panels when visible.
/// </summary>
public sealed class VirtualList : BaseVirtualPanel
{
	int? _selectionAnchor;
	int _selectionEnd;
	Vector2 _selectionStartPoint;
	Vector2 _selectionEndPoint;
	bool _hasSelection;
	bool _dragging;
	bool _copyPending;
	bool _selectionNeedsApply;

	/// <summary>
	/// Vertical list layout used to position/measure items. (Swappable later if needed.)
	/// </summary>
	internal VerticalListLayout Layout { get; } = new();

	/// <summary>
	/// Fixed height of each item.
	/// </summary>
	[Parameter]
	public float ItemHeight
	{
		get => Layout.ItemHeight;
		set => Layout.ItemHeight = value;
	}

	protected override void UpdateLayoutSpacing( Vector2 spacing )
	{
		Layout.Spacing = spacing;
	}

	protected override bool UpdateLayout()
	{
		return Layout.Update( Box, ScaleFromScreen, ScrollOffset.y * ScaleFromScreen );
	}

	protected override void GetVisibleRange( out int first, out int pastEnd )
	{
		Layout.GetVisibleRange( out first, out pastEnd );
	}

	protected override void PositionPanel( int index, Panel panel )
	{
		Layout.Position( index, panel );
	}

	protected override float GetTotalHeight( int itemCount )
	{
		return Layout.GetHeight( itemCount );
	}

	internal override void BeginSelection( Panel target )
	{
		UnselectAllInChildren();
		foreach ( var (index, cell) in _created )
		{
			if ( target != cell && !target.IsAncestor( cell ) ) continue;

			_selectionAnchor = _selectionEnd = index;
			_dragging = true;
			break;
		}
	}

	internal override void EndSelection()
	{
		if ( !_dragging ) return;

		_dragging = false;
		if ( !_hasSelection ) _selectionAnchor = null;
		NeedsRebuild = true;
	}

	internal override void ClearSelectionState()
	{
		if ( !HasSelection ) return;

		_selectionAnchor = null;
		_hasSelection = false;
		_dragging = false;
		_copyPending = false;
		NeedsRebuild = true;
	}

	private protected override bool HasSelection => _selectionAnchor.HasValue;

	private protected override void OnCellCreated()
	{
		_selectionNeedsApply = true;
	}

	private protected override bool TryGetRetainedRange( out int start, out int end )
	{
		if ( _copyPending ) return TryGetSelectionRange( out start, out end );

		start = end = _selectionAnchor ?? 0;
		return _dragging && _selectionAnchor.HasValue;
	}

	bool TryGetSelectionRange( out int start, out int end )
	{
		start = end = 0;
		if ( !_hasSelection || _selectionAnchor is not int anchor ) return false;

		start = Math.Min( anchor, _selectionEnd );
		end = Math.Max( anchor, _selectionEnd );
		return true;
	}

	protected override void OnDragSelect( SelectionEvent e )
	{
		if ( !AllowChildSelection || !_selectionAnchor.HasValue )
		{
			base.OnDragSelect( e );
			return;
		}

		e.StopPropagation();
		_selectionStartPoint = ScreenPositionToPanelPosition( e.StartPoint ) + ScrollOffset;
		_selectionEndPoint = ClampToViewport( ScreenPositionToPanelPosition( e.EndPoint ) ) + ScrollOffset;

		var index = Layout.GetIndexAt( _selectionEndPoint.y * ScaleFromScreen );
		_selectionEnd = Math.Clamp( index, 0, Math.Max( 0, ItemCount - 1 ) );
		_hasSelection = true;

		ApplySelection();
	}

	Vector2 ClampToViewport( Vector2 local )
	{
		var clip = ContentClipRect;
		var top = clip.Top - Box.Rect.Top;
		var bottom = clip.Bottom - Box.Rect.Top - 1;

		if ( local.y < top ) return new Vector2( clip.Left - Box.Rect.Left, top );
		if ( local.y > bottom ) return new Vector2( clip.Right - Box.Rect.Left, bottom );
		return local;
	}

	void ApplySelection()
	{
		_selectionNeedsApply = false;

		var start = PanelPositionToScreenPosition( _selectionStartPoint - ScrollOffset );
		var end = PanelPositionToScreenPosition( _selectionEndPoint - ScrollOffset );
		base.OnDragSelect( new SelectionEvent( "ondragselect", this )
		{
			StartPoint = start,
			EndPoint = end,
			SelectionRect = new Rect( start ).AddPoint( end )
		} );
	}

	protected override void FinalLayoutChildren( Vector2 offset )
	{
		base.FinalLayoutChildren( offset );
		if ( !_hasSelection ) return;

		if ( _selectionNeedsApply ) ApplySelection();
		if ( !_copyPending || !TryGetSelectionRange( out var start, out var end ) || !SelectedCellsReady( start, end ) ) return;

		_copyPending = false;
		NeedsRebuild = true;
		Clipboard.SetText( CollectSelectedText() );
	}

	protected override void OnEvent( PanelEvent e )
	{
		if ( e is CopyEvent or CutEvent ) RequestCopy();
		base.OnEvent( e );
	}

	/// <summary>
	/// Selected rows that scrolled away have no cells. Build them for one layout, then copy.
	/// </summary>
	internal void RequestCopy()
	{
		if ( !AllowChildSelection || !TryGetSelectionRange( out var start, out var end ) ) return;
		if ( SelectedCellsReady( start, end ) ) return;

		_copyPending = true;
		NeedsRebuild = true;
	}

	public override string GetClipboardValue( bool cut )
	{
		if ( !AllowChildSelection || !HasSelection ) return base.GetClipboardValue( cut );
		return CollectSelectedText();
	}

	bool SelectedCellsReady( int start, int end )
	{
		for ( int i = start; i <= end; i++ )
		{
			if ( !_created.TryGetValue( i, out var cell ) || cell.ComputedStyle is null ) return false;
		}

		return true;
	}

	string CollectSelectedText()
	{
		if ( !TryGetSelectionRange( out var start, out var end ) || !SelectedCellsReady( start, end ) ) return null;

		var lines = new List<string>();
		for ( int i = start; i <= end; i++ )
		{
			var line = CollectSelectedChildrenText( _created[i] );
			if ( !string.IsNullOrEmpty( line ) ) lines.Add( line );
		}

		return lines.Count > 0 ? string.Join( "\n", lines ) : null;
	}
}
