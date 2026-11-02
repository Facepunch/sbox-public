using Sandbox.Helpers;

namespace Editor.AnimGraph;

/// <summary>
/// Undo history dock - shows every change as a row, click any row to jump
/// the graph to that point in time.
/// </summary>
public class History : Widget
{
	private readonly Window _window;
	private readonly UndoSystem _undoSystem;
	private readonly HistoryListView _listView;

	private readonly ToolButton _undoButton;
	private readonly ToolButton _redoButton;
	private readonly ToolButton _clearButton;

	/// <summary>
	/// Rows above this are done, rows at or below are undone. Row 0 is the
	/// "Opened" baseline, so the level is Back.Count + 1.
	/// </summary>
	internal int UndoLevel { get; private set; }

	public History( Window window ) : base( window )
	{
		_window = window;
		_undoSystem = window.UndoSystem;

		Name = "History";
		WindowTitle = "History";
		SetWindowIcon( "history" );

		Layout = Layout.Column();

		var bar = new Widget( this );
		bar.FixedHeight = Theme.RowHeight + 2;
		bar.OnPaintOverride = () =>
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.SurfaceBackground );
			Paint.DrawRect( bar.LocalRect );
			return true;
		};

		bar.Layout = Layout.Row();
		bar.Layout.Margin = new Sandbox.UI.Margin( 0, 0, 0, 2 );
		bar.Layout.Spacing = 2;

		_undoButton = bar.Layout.Add( HeaderButton( "undo", "Undo", () => { _window.Undo(); Refresh(); } ) );
		_redoButton = bar.Layout.Add( HeaderButton( "redo", "Redo", () => { _window.Redo(); Refresh(); } ) );
		_clearButton = bar.Layout.Add( HeaderButton( "playlist_remove", "Clear History", () => { _undoSystem.Initialize(); Refresh(); } ) );
		bar.Layout.AddStretchCell();
		Layout.Add( bar );

		var panel = Layout.Add( new Widget( this ) { Layout = Layout.Column() }, 1 );
		panel.Layout.Margin = 4;
		panel.OnPaintOverride = () =>
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.WindowBackground );
			Paint.DrawRect( panel.LocalRect, Theme.ControlRadius );
			return true;
		};

		_listView = new HistoryListView( this, panel );
		panel.Layout.Add( _listView, 1 );

		Refresh();
	}

	private ToolButton HeaderButton( string icon, string toolTip, Action onClick )
	{
		var button = new ToolButton( "", icon, this ) { ToolTip = toolTip, MouseLeftPress = onClick };
		button.OnPaintOverride = () =>
		{
			Paint.ClearPen();
			Paint.SetBrush( button.Enabled && Paint.HasMouseOver ? Theme.ControlBackground.Lighten( 0.1f ) : Theme.ControlBackground );
			Paint.DrawRect( button.LocalRect, Theme.ControlRadius );

			Paint.ClearBrush();
			Paint.SetPen( button.Enabled ? Theme.Primary : Theme.TextControl.WithAlpha( 0.25f ) );
			Paint.DrawIcon( button.LocalRect, icon, 14, TextFlag.Center );
			return true;
		};
		return button;
	}

	private void Refresh()
	{
		var items = new List<object> { "Opened" };
		items.AddRange( _undoSystem.Back.Reverse() );
		items.AddRange( _undoSystem.Forward );

		_listView.SetItems( items );
		UndoLevel = _undoSystem.Back.Count + 1;

		_undoButton.Enabled = _undoSystem.Back.Count > 0;
		_redoButton.Enabled = _undoSystem.Forward.Count > 0;
		_clearButton.Enabled = _undoButton.Enabled || _redoButton.Enabled;

		_undoButton.ToolTip = _undoSystem.Back.TryPeek( out var u ) ? $"Undo {u.Name}" : "Undo";
		_redoButton.ToolTip = _undoSystem.Forward.TryPeek( out var r ) ? $"Redo {r.Name}" : "Redo";

		_undoButton.Update();
		_redoButton.Update();
		_clearButton.Update();

		Update();
	}

	internal void JumpTo( int row )
	{
		var target = row + 1;
		var current = _undoSystem.Back.Count + 1;

		while ( current > target && _window.Undo() )
			current--;

		while ( current < target && _window.Redo() )
			current++;

		Refresh();
	}

	[EditorEvent.Frame]
	public void Tick()
	{
		if ( !Visible )
			return;

		if ( SetContentHash( HashCode.Combine( _undoSystem.Back.Count, _undoSystem.Forward.Count ), 0.1f ) )
			Refresh();
	}

	private class HistoryListView : ListView
	{
		private readonly History _history;

		public HistoryListView( History history, Widget parent ) : base( parent )
		{
			_history = history;

			ItemSize = new Vector2( 0, Theme.RowHeight );
			ItemSpacing = 0;
			Margin = 2;

			TranslucentBackground = true;
			NoSystemBackground = true;
		}

		protected override void PaintItem( VirtualWidget item )
		{
			var (name, timestamp) = item.Object switch
			{
				UndoSystem.Entry entry => (entry.Name, entry.Timestamp.ToString( "HH:mm:ss" )),
				string label => (label, ""),
				_ => ("", "")
			};

			var rect = item.Rect.Shrink( 8, 0, 8, 0 );

			Paint.ClearPen();

			if ( Paint.HasMouseOver )
			{
				Paint.SetBrush( Theme.WindowBackground.Lighten( 0.25f ) );
				Paint.DrawRect( item.Rect );
			}

			var undone = item.Row >= _history.UndoLevel;
			Paint.SetDefaultFont( italic: undone );
			var alpha = undone ? (Paint.HasMouseOver ? 0.5f : 0.4f) : (Paint.HasMouseOver ? 0.9f : 0.8f);
			Paint.SetPen( Theme.Text.WithAlpha( alpha ) );

			if ( item.Row == _history.UndoLevel - 1 )
			{
				rect = rect with { Left = item.Rect.Left + Theme.RowHeight };
				Paint.SetPen( Theme.Blue );
				Paint.DrawIcon( new Rect( item.Rect.Position, Theme.RowHeight ), "arrow_right", Theme.RowHeight );
			}

			Paint.DrawText( rect, name, TextFlag.LeftCenter | TextFlag.SingleLine );

			if ( timestamp != "" )
			{
				Paint.SetPen( Theme.Text.WithAlpha( 0.25f ) );
				Paint.DrawText( rect, timestamp, TextFlag.RightCenter | TextFlag.SingleLine );
			}
		}

		protected override bool OnItemPressed( VirtualWidget pressedItem, MouseEvent e )
		{
			_history.JumpTo( pressedItem.Row );
			return true;
		}
	}
}
