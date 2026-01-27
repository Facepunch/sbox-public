using System.Collections;
using Sandbox.MovieMaker;

namespace Editor.MovieMaker;

#nullable enable

public interface IHistoryItem
{
	int Index { get; }
	string Title { get; }
	string Icon { get; }
	DateTime Time { get; }

	bool Apply();
}

public interface IHistoryScope : IDisposable
{
	void PostChange();
	void IDisposable.Dispose() => PostChange();
}

public sealed class SessionHistory : IReadOnlyList<IHistoryItem>
{
	private readonly Session _session;

	private sealed class HistoryItem : IHistoryItem, IHistoryScope
	{
		private readonly SessionHistory _history;

		public int Index { get; }
		public string Title { get; }
		public string Icon { get; }

		public DateTime Time { get; private set; }
		public SessionSnapshot Snapshot { get; private set; }

		public HistoryItem( SessionHistory history, int index, string title, string icon, DateTime time, SessionSnapshot snapshot )
		{
			_history = history;

			Index = index;
			Title = title;
			Icon = icon;
			Time = time;

			Snapshot = snapshot;
		}

		public bool Apply()
		{
			_history._index = Index;

			return _history._session.Restore( Snapshot );
		}

		public void PostChange()
		{
			if ( _history.Index != Index )
			{
				Log.Warning( "Trying to change a closed history scope." );
				return;
			}

			Time = DateTime.UtcNow;
			Snapshot = _history._session.Snapshot();
		}

		public override string ToString() => Title;
	}

	private readonly List<HistoryItem> _items = new();
	private int _index;

	public bool CanUndo => Previous is not null;
	public bool CanRedo => Next is not null;

	public int Count => _items.Count;
	public int Index => _index;

	public IHistoryItem? Previous => _index > 0 ? _items[_index - 1] : null;
	public IHistoryItem Current => _items[_index];
	public IHistoryItem? Next => _index < _items.Count - 1 ? _items[_index + 1] : null;

	public SessionHistory( Session session )
	{
		_session = session;
	}

	internal void Initialize()
	{
		if ( _items.Count == 0 )
		{
			_items.Add( new HistoryItem( this, 0, "Loaded Project", "file_open", DateTime.UtcNow, _session.Snapshot() ) );
		}
	}

	public IHistoryScope Push( string title )
	{
		var before = _items[_index].Snapshot;
		var item = new HistoryItem( this, ++_index, title, _session.EditMode?.Type.Icon ?? "edit", DateTime.UtcNow, before );

		_items.RemoveRange( _index, _items.Count - _index );
		_items.Add( item );

		return item;
	}

	public bool Undo() => Previous?.Apply() ?? false;
	public bool Redo() => Next?.Apply() ?? false;

	public IEnumerator<IHistoryItem> GetEnumerator() => _items.GetEnumerator();
	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

	public IHistoryItem this[ int index ] => _items[index];
}

internal sealed record SessionProperties(
	MovieTime? TimeOffset,
	float? PixelsPerSecond,
	int FrameRate );

internal sealed record EditModeSnapshot(
	Type Type, EditMode.ISnapshot? Data );
internal sealed record SessionSnapshot(
	MovieProject.Model Project,
	EditModeSnapshot? EditMode,
	SessionProperties Properties );

partial class Session
{
	public SessionHistory History { get; }

	internal SessionSnapshot Snapshot()
	{
		var timeline = Editor.TimelinePanel?.Timeline;

		return new SessionSnapshot(
			Project.Snapshot(),
			EditMode?.Snapshot(),
			new SessionProperties( timeline?.TimeOffset, timeline?.PixelsPerSecond, FrameRate ) );
	}

	internal bool Restore( SessionSnapshot snapshot )
	{
		Project.Restore( snapshot.Project );

		if ( snapshot.EditMode is { } editMode && SetEditMode( editMode.Type ) && editMode.Data is { } data )
		{
			EditMode?.Restore( data );
		}

		var timeline = Editor.TimelinePanel?.Timeline;

		timeline?.SetView(
			snapshot.Properties.TimeOffset ?? timeline.TimeOffset,
			snapshot.Properties.PixelsPerSecond ?? timeline.PixelsPerSecond );

		FrameRate = snapshot.Properties.FrameRate;

		TrackList.Update();

		EditMode?.PostRestore();

		return true;
	}
}

partial class EditMode
{
	protected internal interface ISnapshot;

	internal EditModeSnapshot Snapshot() => new( GetType(), OnSnapshot() );

	protected virtual ISnapshot? OnSnapshot() => null;

	internal void PreRestore() => OnPreRestore();
	protected virtual void OnPreRestore() { }

	internal void Restore( ISnapshot snapshot ) => OnRestore( snapshot );
	protected virtual void OnRestore( ISnapshot snapshot ) { }

	internal void PostRestore() => OnPostRestore();
	protected virtual void OnPostRestore() { }
}
