namespace Editor.AnimGraph;

[EditorForAssetType( "anim" )]
public sealed class ClipAssetEditor : AssetDocumentEditor
{
	private ClipAssetPreview _clipPreview;
	private ClipProperties _clipSettings;
	private ClipEventTimeline _timeline;
	private ClipEventProperties _eventProperties;
	private ClipDocument Document => Session.Document as ClipDocument;
	protected override string EditorName => "Clip";
	protected override string LayoutCookie => "AnimationClipEditor.v2";
	private protected override IAnimationDocument LoadDocument( string path ) => ClipDocument.Load( path );

	protected override void CreateEditor( Widget center, ScrollArea settings )
	{
		_clipPreview = center.Layout.Add( new ClipAssetPreview( center, Asset, OnPreviewModelChanged ), 1 );
		_clipSettings = new ClipProperties( settings ) { Committed = Commit, Enabled = !Session.ReadOnly };
		settings.Canvas = _clipSettings;
		_eventProperties = new ClipEventProperties( this ) { MinimumWidth = 320, Edited = OnEventEdited };
	}

	protected override void CreateDocumentDocks()
	{
		_timeline = new ClipEventTimeline( this )
		{
			ReadOnly = Session.ReadOnly,
			Edited = Commit,
			FinishEdit = _eventProperties.CommitEdit,
			Selected = OnEventSelected,
			EditRequested = ShowEventProperties,
			Scrubbed = _clipPreview.ScrubTo
		};
		_clipPreview.PlayheadMoved = OnPreviewPlayheadMoved;
		_timeline.SetTransport( _clipPreview.MoveTransportTo( _timeline ) );
		var timelineDock = DockManager.AddDock( "Timeline", "view_timeline", _timeline, DockArea.Hidden );
		timelineDock.MinimumSizeFromContent = true;
		timelineDock.ContentMargins = new Sandbox.UI.Margin( 2, 0, 2, 2 );
		DockManager.AddDock( "Event Properties", "bolt", _eventProperties, DockArea.Hidden ).MinimumSizeFromContent = true;
	}

	protected override void BuildDocumentLayout( DockWidget settings )
	{
		var timeline = DockManager.OpenDock( "Timeline", DockArea.Bottom, PreviewDock );
		DockManager.OpenDock( "Event Properties", DockArea.Center, settings );
		DockManager.RaiseDock( "Event Properties" );
		DockManager.SetSplitterProportions( timeline, 0.68f, 0.32f );
	}

	private void ShowEventProperties()
	{
		DockManager.SetDockState( "Event Properties", true );
		DockManager.RaiseDock( "Event Properties" );
	}

	private void OnEventSelected( ClipDocument.Track track, ClipDocument.Event item )
		=> _eventProperties.SetEvent( item, track?.EventClass, Session.ReadOnly );

	private void OnEventEdited()
	{
		Commit( "Edit Event" );
		_timeline.RefreshEvent();
	}

	private void OnPreviewPlayheadMoved( float position ) => _timeline.Playhead = position;

	protected override void RefreshDocument()
	{
		_clipSettings.SetDocument( Document );
		BindTimeline();
	}

	private void BindTimeline()
	{
		var clip = Clip.Load( Asset.Path );
		var frames = clip?.FrameCount ?? 0;
		_eventProperties.FrameCount = frames;
		_timeline.SetClip( frames > 1 ? Document : null, frames, clip?.Duration ?? 0, "Compile this clip with at least two frames to edit events." );
	}

	protected override void UpdatePreview() => _clipPreview.SetDocument( Document, Session.ReadOnly );

	protected override void RefreshCompiledAsset()
	{
		var clip = Clip.Load( Asset.Path );
		if ( (clip?.FrameCount ?? 0) == _eventProperties.FrameCount && (clip?.Duration ?? 0) == _timeline.Duration )
			return;

		var position = _clipPreview.PlaybackPosition;
		var playing = _clipPreview.IsPlaying;
		BindTimeline();
		_clipPreview.ScrubTo( position );
		_clipPreview.IsPlaying = playing;
	}

	protected override void FinishEditing()
	{
		base.FinishEditing();
		_timeline?.FinishEditing();
	}

	protected override void OnDocumentClosing() => _eventProperties?.CommitEdit();
}
