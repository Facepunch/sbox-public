namespace Editor;

[Dock( "Editor", "Undo", "history" )]
public class UndoDock : Widget
{
	public UndoDock( Widget parent ) : base( parent )
	{
		Layout = Layout.Row();
		Layout.Margin = 4;
		Layout.Spacing = 4;

		Rebuild();
	}

	void Rebuild()
	{
		Layout.Clear( true );

		if ( SceneEditorSession.Active is not null )
		{
			Layout.Add( new UndoList( SceneEditorSession.Active ) );
		}
	}

	[EditorEvent.Frame]
	public void CheckForChanges()
	{
		if ( !Visible )
			return;

		if ( SetContentHash( HashCode.Combine( SceneEditorSession.Active ), 0.1f ) )
		{
			Rebuild();
		}
	}
}

class UndoList : Widget
{
	readonly SceneEditorSession _session;
	readonly Sandbox.Helpers.UndoSystem _undoSystem;
	readonly UndoListView _listView;

	readonly Option _undoOption;
	readonly Option _redoOption;
	readonly Option _clearOption;

	int _undoLevel;

	public UndoList( SceneEditorSession session )
	{
		_session = session;
		_undoSystem = session.UndoSystem;

		Layout = Layout.Column();

		var toolBar = new ToolBar( this, "UndoHistoryToolBar" );

		_undoOption = toolBar.AddOption( "Undo", "undo", () =>
		{
			_undoSystem.Undo();
			Refresh();
		} );

		_redoOption = toolBar.AddOption( "Redo", "redo", () =>
		{
			_undoSystem.Redo();
			Refresh();
		} );

		toolBar.AddSeparator();

		_clearOption = toolBar.AddOption( "Clear History", "playlist_remove", () =>
		{
			_undoSystem.Initialize();
			Refresh();
		} );

		Layout.Add( toolBar );

		_listView = new UndoListView( this );
		Layout.Add( _listView, 1 );

		Refresh();
	}

	void Refresh()
	{
		var back = _undoSystem.Back.Reverse().ToList();
		var forward = _undoSystem.Forward.ToList();
		var items = back.Select( x => x.Name ).Concat( forward.Select( x => x.Name ) );

		_listView.SetItems( items );

		_undoLevel = _undoSystem.Back.Count;

		_undoOption.Enabled = _undoSystem.Back.Count > 0;
		_redoOption.Enabled = _undoSystem.Forward.Count > 0;
		_clearOption.Enabled = _undoOption.Enabled || _redoOption.Enabled;

		_undoOption.Text = _undoSystem.Back.TryPeek( out var u ) ? u.Name : "Undo";
		_redoOption.Text = _undoSystem.Forward.TryPeek( out var r ) ? r.Name : "Redo";

		_undoOption.StatusTip = _undoOption.Text;
		_redoOption.StatusTip = _redoOption.Text;
	}

	void JumpTo( int index )
	{
		var target = index + 1;
		var current = _undoSystem.Back.Count;

		if ( target == current )
			return;

		using var scope = _session.SuppressUndoSounds();

		while ( current > target )
		{
			if ( !_undoSystem.Undo() )
				break;

			current--;
		}

		while ( current < target )
		{
			if ( !_undoSystem.Redo() )
				break;

			current++;
		}

		Refresh();
	}

	[EditorEvent.Frame]
	public void Tick()
	{
		if ( !Visible )
			return;

		if ( SetContentHash( HashCode.Combine( _undoSystem.Back.Count, _undoSystem.Forward.Count ), 0.1f ) )
		{
			Refresh();
		}
	}

	class UndoListView : ListView
	{
		readonly UndoList _history;

		public UndoListView( UndoList parent ) : base( parent )
		{
			_history = parent;

			ItemSize = new Vector2( 0, Theme.RowHeight );
			ItemSpacing = 0;
			Margin = 2;
		}

		protected override void OnPaint()
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.WindowBackground );
			Paint.DrawRect( LocalRect );

			base.OnPaint();
		}

		protected override void PaintItem( VirtualWidget item )
		{
			if ( item.Object is not string undoName )
				return;

			var rect = item.Rect.Shrink( 8, 0, 0, 0 );

			Paint.ClearPen();

			if ( Paint.HasMouseOver )
			{
				Paint.SetBrush( Theme.WindowBackground.Lighten( 0.25f ) );
				Paint.DrawRect( item.Rect );
			}

			if ( item.Row >= _history._undoLevel )
			{
				Paint.SetDefaultFont( italic: true );
				Paint.SetPen( Theme.Text.WithAlpha( Paint.HasMouseOver ? 0.5f : 0.4f ), 3.0f );
			}
			else
			{
				Paint.SetPen( Theme.Text.WithAlpha( Paint.HasMouseOver ? 0.9f : 0.8f ), 3.0f );
			}

			if ( item.Row == _history._undoLevel - 1 )
			{
				rect = item.Rect.Shrink( Theme.RowHeight, 0, 0, 0 );
				Paint.SetPen( Theme.Blue, 3.0f );
				Paint.DrawIcon( new Rect( item.Rect.Position, Theme.RowHeight ), "arrow_right", Theme.RowHeight );
			}

			Paint.DrawText( rect, undoName, TextFlag.LeftCenter | TextFlag.SingleLine );
		}

		protected override bool OnItemPressed( VirtualWidget pressedItem, MouseEvent e )
		{
			_history.JumpTo( pressedItem.Row );
			return true;
		}
	}
}
