namespace Editor.MovieMaker;

#nullable enable

partial class Session
{
	private TrackListView? _trackList;

	/// <summary>
	/// Which tracks should be visible in the track list / dope sheet.
	/// </summary>
	public TrackListView TrackList => _trackList ??= new TrackListView( this );

	private void TrackFrame()
	{
		_trackList?.Frame();
	}
}
