using Microsoft.AspNetCore.Components;

namespace Sandbox.UI;

/// <summary>
/// A strip of selectable tabs, independent of their content.
/// </summary>
[Library( "tabbar" )]
[StyleSheet.Inline( "tabbar", Styles )]
public class TabBar : Panel
{
	const string Styles = """
		.tabbar { flex-direction: row; flex-shrink: 0; width: 100%; min-width: 0; overflow-x: scroll; overflow-y: hidden; pointer-events: all; }
		.tabbar > .tab { flex-shrink: 0; align-items: center; white-space: nowrap; pointer-events: all; }
		.tab > .tab-title, .tab > .tab-icon, .tab > .tab-count { pointer-events: none; }
		.tab > .tab-count { flex-shrink: 0; }
		.tab > .tab-icon { width: 1em; height: 1em; flex-shrink: 0; object-fit: contain; }
		.tab > .tab-close { align-items: center; justify-content: center; flex-shrink: 0; pointer-events: all; }
		.tab > .tab-close > * { pointer-events: none; }
		""";

	Tab _selected;
	Tab _dragged;
	internal bool IsReordering => _dragged is not null;
	int _dropIndex;
	bool _revealSelection;

	/// <summary>
	/// Enable dragging to change tab order. Off by default.
	/// </summary>
	[Parameter] public bool AllowReorder { get; set; }

	/// <summary>
	/// Automatically hide titles on tabs with icons when each tab has too little space.
	/// </summary>
	[Parameter] public bool AutoHideText { get; set; } = true;

	/// <summary>
	/// Minimum available width per tab, in CSS pixels, before switching to icons. Default is 100.
	/// </summary>
	[Parameter] public float MinimumTextWidth { get; set; } = 100;

	/// <summary>
	/// Tabs in display order.
	/// </summary>
	public IReadOnlyList<Tab> Tabs => Children.OfType<Tab>().Where( x => x.IsValid && !x.IsDeleting ).ToArray();

	/// <summary>
	/// The selected tab, or null when empty.
	/// </summary>
	public Tab SelectedTab => _selected;

	/// <summary>
	/// Called after selection changes, including to null when the last tab is removed.
	/// </summary>
	public event Action<Tab> SelectionChanged;

	/// <summary>
	/// Return false to cancel closing, for example while asking about unsaved changes.
	/// </summary>
	public Func<Tab, bool> CanCloseTab { get; set; }

	/// <summary>
	/// Called when a tab is removed, before it is deleted.
	/// </summary>
	public event Action<Tab> TabRemoved;

	/// <summary>
	/// Called after a tab moves, with its new index.
	/// </summary>
	public event Action<Tab, int> TabReordered;

	public TabBar() { AddClass( "tabbar" ); CanDragScroll = false; }

	/// <summary>
	/// Add and own a tab. The first tab is selected automatically.
	/// </summary>
	public Tab AddTab( string text, string icon = null, bool canClose = false, int? count = null )
	{
		var tab = AddChild( new Tab { Text = text, Icon = icon, CanClose = canClose, Count = count } );
		if ( _selected is null ) SelectTab( tab );
		return tab;
	}

	/// <summary>
	/// Select a tab belonging to this bar.
	/// </summary>
	public virtual void SelectTab( Tab tab )
	{
		if ( tab is not null && (tab.Parent != this || !tab.IsValid || tab.IsDeleting) )
			throw new ArgumentException( "The tab does not belong to this bar.", nameof( tab ) );
		if ( _selected == tab ) return;
		SetSelection( tab );
		SelectionChanged?.Invoke( tab );
	}

	// Docking synchronizes from its own layout model without generating a second selection edit.
	internal void SetSelection( Tab tab )
	{
		_selected = tab;
		_revealSelection = tab is not null;
		foreach ( var item in Tabs ) item.SetClass( "selected", item == tab );
	}

	/// <summary>
	/// Request closing. Nonclosable tabs and vetoed requests are left alone.
	/// </summary>
	public virtual bool CloseTab( Tab tab )
	{
		if ( tab?.Parent != this || !tab.CanClose || CanCloseTab?.Invoke( tab ) == false ) return false;
		return RemoveTab( tab );
	}

	/// <summary>
	/// Remove and delete a tab regardless of its close policy.
	/// </summary>
	public bool RemoveTab( Tab tab )
	{
		if ( tab?.Parent != this ) return false;
		var focused = tab.HasFocus;
		CancelReorder();
		tab.Parent = null;
		if ( focused ) _selected?.Focus();
		tab.Delete( true );
		return true;
	}

	protected override void OnChildRemoved( Panel child )
	{
		base.OnChildRemoved( child );
		if ( IsDeleting || !IsValid || child is not Tab tab ) return;
		CancelReorder();
		if ( _selected == tab )
		{
			var remaining = Tabs;
			SelectTab( remaining.Count == 0 ? null : remaining[Math.Clamp( tab.SiblingIndex, 0, remaining.Count - 1 )] );
		}
		TabRemoved?.Invoke( tab );
	}

	/// <summary>
	/// Move a tab to a zero-based position without changing selection.
	/// </summary>
	public void MoveTab( Tab tab, int index )
	{
		var tabs = Tabs;
		if ( tab?.Parent != this ) throw new ArgumentException( "The tab does not belong to this bar.", nameof( tab ) );
		if ( index < 0 || index >= tabs.Count ) throw new ArgumentOutOfRangeException( nameof( index ) );
		if ( tabs[index] == tab ) return;
		SetChildIndex( tab, tabs[index].SiblingIndex );
		TabReordered?.Invoke( tab, index );
	}

	/// <summary>
	/// Find an insertion index from a screen position, excluding the dragged tab.
	/// </summary>
	internal int InsertionIndex( Vector2 point, Tab excluded = null ) => Tabs.Count( x => x != excluded && point.x >= x.Box.Rect.Center.x );

	internal void BeginReorder( Tab tab )
	{
		if ( !AllowReorder || tab.Parent != this ) return;
		_dragged = tab;
		tab.AddClass( "dragging" );
	}

	internal void UpdateReorder( Vector2 point )
	{
		if ( _dragged is null ) return;
		foreach ( var tab in Tabs ) { tab.RemoveClass( "drop-before" ); tab.RemoveClass( "drop-after" ); }
		if ( !AllowReorder || (point.x < Box.Rect.Left || point.x >= Box.Rect.Right || point.y < Box.Rect.Top || point.y >= Box.Rect.Bottom) ) { _dropIndex = -1; return; }
		_dropIndex = InsertionIndex( point, _dragged );
		var others = Tabs.Where( x => x != _dragged ).ToArray();
		if ( others.Length == 0 ) return;
		if ( _dropIndex < others.Length ) others[_dropIndex].AddClass( "drop-before" );
		else others[^1].AddClass( "drop-after" );
	}

	internal void EndReorder( Vector2 point )
	{
		if ( _dragged is null ) return;
		UpdateReorder( point );
		var tab = _dragged;
		var index = _dropIndex;
		CancelReorder();
		if ( index >= 0 && tab.Parent == this ) MoveTab( tab, index );
	}

	internal void CancelReorder()
	{
		_dragged?.RemoveClass( "dragging" );
		_dragged = null;
		foreach ( var tab in Tabs ) { tab.RemoveClass( "drop-before" ); tab.RemoveClass( "drop-after" ); }
	}

	/// <inheritdoc/>
	public override void FinalLayout( Vector2 offset )
	{
		base.FinalLayout( offset );
		// Newly added tabs have no screen bounds until their children have finished layout.
		// Reveal only inside this strip; selecting a tab must not move an enclosing page.
		if ( !_revealSelection || _selected?.Parent != this || !_selected.IsVisible || _selected.Box.Rect.Width <= 0 ) return;
		_revealSelection = false;
		ScrollIntoView( _selected.Box.Rect );
	}

	/// <inheritdoc/>
	public override void Tick()
	{
		base.Tick();
		if ( !IsVisible || _dragged is not null ) return;
		var tabs = Tabs.Where( x => x.IsVisible ).ToArray();
		var available = Box.RectInner.Width * ScaleFromScreen;
		if ( available <= 0 || tabs.Length == 0 ) return;
		var gap = ComputedStyle?.ColumnGap?.Value ?? 0;
		var compact = AutoHideText && (available - gap * (tabs.Length - 1)) / tabs.Length < MinimumTextWidth;
		foreach ( var tab in tabs ) tab.SetIconOnly( compact );
	}

	/// <inheritdoc/>
	public override void OnButtonTyped( ButtonEvent e )
	{
		var tabs = Tabs.Where( x => x.IsVisible ).ToArray();
		var index = Array.FindIndex( tabs, x => x.HasFocus );
		if ( index >= 0 && e.Button is "left" or "right" or "home" or "end" )
		{
			var next = e.Button switch { "home" => 0, "end" => tabs.Length - 1, _ => (index + (e.Button == "right" ? 1 : -1) + tabs.Length) % tabs.Length };
			SelectTab( tabs[next] );
			tabs[next].Focus();
			_revealSelection = true;
			e.StopPropagation = true;
			return;
		}
		base.OnButtonTyped( e );
	}
}
