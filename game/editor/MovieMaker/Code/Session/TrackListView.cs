using Sandbox.MovieMaker;
using System.Linq;

namespace Editor.MovieMaker;

#nullable enable

/// <summary>
/// Describes which tracks should be shown in the track list / dope sheet.
/// </summary>
public sealed class TrackListView
{
	public Session Session { get; }
	public int StateHash { get; private set; }
	public TrackView? LastSelected { get; set; }

	public MovieTime Duration => MovieTime.Max( Session.Project.Duration,
		_trackDict.Values
			.Select( x => x.Duration )
			.DefaultIfEmpty( MovieTime.Zero )
			.Max() );

	public float Height { get; private set; }

	private readonly SynchronizedSet<IProjectTrack, TrackView> _rootTracks;
	private readonly Dictionary<IProjectTrack, TrackView> _trackDict = new();

	public IReadOnlyList<TrackView> RootTracks => _rootTracks;

	public event Action<TrackListView>? Changed;

	public IEnumerable<TrackView> AllTracks => RootTracks.SelectMany( EnumerateDescendants );
	public IEnumerable<TrackView> VisibleTracks => RootTracks.SelectMany( EnumerateVisibleDescendants );
	public IEnumerable<TrackView> SelectedTracks => AllTracks.Where( x => x.IsSelected );

	public IEnumerable<TrackView> UnlockedTracks =>
		AllTracks.Where( x => x is { IsLocked: false } );
	public IEnumerable<TrackView> EditablePropertyTracks =>
		AllTracks.Where( x => x is { IsLocked: false, Target: ITrackProperty { CanWrite: true } } );

	public TrackView? Find( IProjectTrack track ) => _trackDict.GetValueOrDefault( track );

	public TrackView? Find( GameObject go ) => AllTracks.FirstOrDefault( x =>
		x.Target is ITrackReference<GameObject> { IsBound: true } target && target.Value == go );
	public TrackView? Find( Component cmp ) => AllTracks.FirstOrDefault( x =>
		x.Target is ITrackReference { IsBound: true } target && target.Value == cmp );
	public TrackView? Find( MovieResource resource ) => AllTracks.FirstOrDefault( x =>
		x.Track is ProjectSequenceTrack );

	private static IEnumerable<TrackView> EnumerateDescendants( TrackView track ) =>
		[track, .. track.Children.SelectMany( EnumerateDescendants )];

	private static IEnumerable<TrackView> EnumerateVisibleDescendants( TrackView track ) =>
		track.IsExpanded
			? [track, .. track.Children.SelectMany( EnumerateVisibleDescendants )]
			: [track];


	public TrackListView( Session session )
	{
		Session = session;

		_rootTracks = new SynchronizedSet<IProjectTrack, TrackView>(
			AddRootTrack, RemoveRootTrack, UpdateRootTrack );

		Update();
	}

	private TrackView AddRootTrack( IProjectTrack source ) =>
		new( this, null, source, Session.Binder.Get( source ) );

	private void RemoveRootTrack( TrackView item ) => item.OnRemoved();
	private bool UpdateRootTrack( IProjectTrack source, TrackView item ) => item.Update();

	public void Update()
	{
		if ( !_rootTracks.Update( Session.Project.RootTracks.Order() ) ) return;

		_trackDict.Clear();

		foreach ( var trackView in AllTracks )
		{
			_trackDict[trackView.Track] = trackView;
		}

		var position = 0f;
		var hashCode = new HashCode();

		foreach ( var track in _rootTracks )
		{
			track.UpdatePosition( ref position );
			hashCode.Add( track.StateHash );

			position += 8f;
		}

		StateHash = hashCode.ToHashCode();

		Height = position - 8f;

		Changed?.Invoke( this );
	}

	public void Frame()
	{
		foreach ( var track in _rootTracks )
		{
			track.Frame();
		}
	}

	public void DeselectAll()
	{
		LastSelected = null;

		foreach ( var view in SelectedTracks.ToArray() )
		{
			view.IsSelected = false;
		}
	}
}
