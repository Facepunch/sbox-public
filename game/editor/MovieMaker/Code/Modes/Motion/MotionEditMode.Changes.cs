using System.Collections.Immutable;
using System.Linq;
using Sandbox.MovieMaker;

namespace Editor.MovieMaker;

#nullable enable

partial class MotionEditMode
{
	private IMovieModification? _modification;
	private ToolBarGroup? _modificationControls;

	private RealTimeSince _lastActionTime;

	public override bool AllowTrackCreation => TimeSelection is not null;

	public IMovieModification? Modification
	{
		get => _modification;
	}

	public bool HasChanges => Modification is not null;

	public Color SelectionColor
	{
		get
		{
			var t = MathF.Pow( Math.Clamp( 1f - _lastActionTime, 0f, 1f ), 8f );
			var color = (HasChanges ? Theme.Yellow : Theme.Blue).WithAlpha( 0.25f );

			return Color.Lerp( color, Color.White.WithAlpha( 0.5f ), t );
		}
	}

	public string? LastActionIcon { get; private set; }

	public IMovieModification SetModification( Type type, TimeSelection selection )
	{
		if ( _modification?.GetType() == type )
		{
			TimeSelection = selection;
			return _modification;
		}

		_modification?.ClearPreview();
		_modificationControls?.Destroy();

		_modification = (IMovieModification)Activator.CreateInstance( type )!;
		_modification.Initialize( this );

		_modificationControls = ToolBar.AddGroup();

		_modification.AddControls( _modificationControls );

		var commitDisplay = new ToolBarItemDisplay( "Apply", "done", "Commit all pending track changes.", ShortcutIdentifier: "motion-edit.commit" );
		var cancelDisplay = new ToolBarItemDisplay( "Cancel", "clear", "Cancel all pending track changes.", ShortcutIdentifier: "motion-edit.clear" );

		var commit = _modificationControls.AddAction( commitDisplay, CommitChanges );
		var cancel = _modificationControls.AddAction( cancelDisplay, ClearChanges );

		commit.Background = Theme.Green;
		cancel.Background = Theme.Red;

		TimeSelection = selection;

		return _modification;
	}

	public T SetModification<T>( TimeSelection selection ) where T : IMovieModification =>
		(T)SetModification( typeof(T), selection );

	private void ClearChanges()
	{
		if ( Modification is null ) return;

		Modification?.ClearPreview();

		_modificationControls?.Destroy();
		_modificationControls = null;

		_modification = null;

		DisplayAction( "clear" );
		SelectionChanged();
	}

	private void CommitChanges()
	{
		if ( TimeSelection is not { } selection || Modification is not { } modification || !HasChanges ) return;

		using ( Session.History.Push( "Commit" ) )
		{
			modification.Commit( selection );
		}

		ClearChanges();
		DisplayAction( "approval" );

		Session.ClipModified();
	}

	private void Delete( bool shiftTime )
	{
		if ( TimeSelection is { } selection ) Delete( selection.PeakTimeRange, shiftTime );
	}

	private void Delete( MovieTimeRange timeRange, bool shiftTime )
	{
		var changed = false;

		using ( Session.History.Push( shiftTime ? "Remove Time" : "Clear Time" ) )
		{
			foreach ( var view in Session.TrackList.EditablePropertyTracks )
			{
				var track = (IProjectPropertyTrack)view.Track;

				if ( shiftTime )
				{
					changed |= track.Remove( timeRange ) && view.MarkValueChanged();
				}
				else
				{
					changed |= track.Clear( timeRange ) && view.MarkValueChanged();
				}
			}
		}

		if ( changed )
		{
			Session.ClipModified();
			DisplayAction( "delete" );
		}
	}

	protected override void OnBackspace()
	{
		Delete( true );
	}

	protected override void OnDelete()
	{
		Delete( false );
	}

	protected override void OnInsert()
	{
		if ( TimeSelection is not { } selection ) return;

		var changed = false;

		using ( Session.History.Push( "Insert" ) )
		{
			foreach ( var view in Session.TrackList.EditablePropertyTracks )
			{
				var track = (IProjectPropertyTrack)view.Track;

				changed |= track.Insert( selection.PeakTimeRange ) && view.MarkValueChanged();
			}
		}

		if ( changed )
		{
			DisplayAction( "keyboard_tab" );
		}
	}

	private static ClipboardData? Clipboard { get; set; }

	protected override void OnSelectAll()
	{
		TimeSelection = new TimeSelection( (MovieTime.Zero, Project.Duration), DefaultInterpolation );
	}

	protected override void OnCut()
	{
		OnCopy();
		Delete( false );

		DisplayAction( "content_cut" );
	}

	protected override void OnCopy()
	{
		if ( TimeSelection is not { } selection ) return;

		var timeRange = selection.TotalTimeRange;
		var tracks = new Dictionary<Guid, IReadOnlyList<IProjectPropertyBlock>>();
		var slicedBlocks = new List<IProjectPropertyBlock>();

		foreach ( var view in Session.TrackList.EditablePropertyTracks )
		{
			var track = (IProjectPropertyTrack)view.Track;

			slicedBlocks.Clear();
			slicedBlocks.AddRange( track.Slice( timeRange ) );

			if ( slicedBlocks.Count > 0 )
			{
				tracks[track.Id] = slicedBlocks.ToImmutableList();
			}
		}

		if ( tracks.Count <= 0 ) return;

		Clipboard = new ClipboardData( selection, tracks.ToImmutableDictionary() );

		DisplayAction( "content_copy" );
	}

	protected override void OnPaste()
	{
		Paste( Session.PlayheadTime );
	}

	private bool Paste( MovieTime time )
	{
		if ( Clipboard is not { } clipboard ) return false;

		using var historyScope = Session.History.Push( "Paste" );

		ClearChanges();

		var offset = time - clipboard.Selection.PeakStart;
		var selection = clipboard.Selection + offset;

		TimeSelection = selection;

		SetModification<BlendModification>( selection )
			.SetFromClipboard( clipboard, offset );

		SelectionChanged();
		DisplayAction( "content_paste" );

		return true;
	}

	private MovieResource CreateSequence( MovieTimeRange timeRange )
	{
		var project = new MovieProject();
		var offset = -timeRange.Start;

		foreach ( var editable in Session.TrackList.EditablePropertyTracks )
		{
			if ( editable.Track is not IProjectPropertyTrack propertyTrack ) continue; // TODO
			if ( propertyTrack.Slice( timeRange ) is not { Count: > 0 } slice ) continue;

			var trackCopy = (IProjectPropertyTrack)project.GetOrAddTrack( editable.Track );

			trackCopy.SetBlocks( [.. slice.Select( x => x.Shift( offset ) )] );
		}

		Delete( timeRange, false );

		var resource = new MovieResource { EditorData = project.Serialize(), Compiled = project.Compile() };
		var track = Project.AddSequenceTrack( "Sequences" );

		track.AddBlock( timeRange, new MovieTransform( -offset ), resource );

		Session.TrackList.Update();

		return resource;
	}

	protected override void OnTrackStateChanged( TrackView view )
	{
		if ( view.Track is not IProjectPropertyTrack track ) return;
		if ( TimeSelection is not { } selection || Modification is not { } modification ) return;

		modification.UpdatePreview( selection, track );
	}

	protected override bool OnPreChange( TrackView view )
	{
		if ( TimeSelection is not { } selection ) return false;
		if ( view.Track is not IProjectPropertyTrack track ) return false;
		if ( view.Target is not ITrackProperty property ) return false;

		if ( Modification is not BlendModification blend )
		{
			Modification?.Commit( selection );

			blend = SetModification<BlendModification>( selection );
		}

		return blend.PreChange( track, property );
	}

	protected override bool OnPostChange( TrackView view )
	{
		if ( TimeSelection is not { } selection || Modification is not BlendModification blend ) return false;
		if ( view.Track is not IProjectPropertyTrack track ) return false;
		if ( view.Target is not ITrackProperty property ) return false;

		return blend.PostChange( track, property ) && blend.UpdatePreview( selection, track );
	}

	private bool _hasSelectionItems;

	public void SelectionChanged()
	{
		if ( TimeSelection is { } selection )
		{
			SourceTimeRange = Modification?.SourceTimeRange;

			Modification?.UpdatePreview( selection );

			if ( !_hasSelectionItems )
			{
				_hasSelectionItems = true;

				Timeline.Add( new TimeSelectionPeakItem( this ) );

				Timeline.Add( new TimeSelectionFadeItem( this, FadeKind.FadeIn ) );
				Timeline.Add( new TimeSelectionFadeItem( this, FadeKind.FadeOut ) );

				Timeline.Add( new TimeSelectionHandleItem( this ) );
				Timeline.Add( new TimeSelectionHandleItem( this ) );
				Timeline.Add( new TimeSelectionHandleItem( this ) );
				Timeline.Add( new TimeSelectionHandleItem( this ) );
			}

			UpdateSelectionItems( Timeline.VisibleRect );
		}
		else if ( _hasSelectionItems )
		{
			_hasSelectionItems = false;

			SourceTimeRange = null;

			foreach ( var item in Timeline.Items.OfType<TimeSelectionItem>().ToArray() )
			{
				item.Destroy();
			}
		}

		Session.RefreshNextFrame();
	}

	protected override void OnViewChanged( Rect viewRect )
	{
		UpdateSelectionItems( viewRect );
	}

	private void UpdateSelectionItems( Rect viewRect )
	{
		if ( TimeSelection is not { } selection ) return;

		foreach ( var item in Timeline.Items.OfType<TimeSelectionItem>() )
		{
			item.UpdatePosition( selection, viewRect );
		}
	}

	public void DisplayAction( string icon )
	{
		_lastActionTime = 0f;
		LastActionIcon = icon;

		UpdateSelectionItems( Timeline.VisibleRect );
	}

	protected override void OnFrame()
	{
		RecordingFrame();

		if ( _lastActionTime < 1f )
		{
			UpdateSelectionItems( Timeline.VisibleRect );
		}
	}
}
