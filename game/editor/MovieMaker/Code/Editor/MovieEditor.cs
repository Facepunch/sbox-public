using Sandbox.MovieMaker;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Editor.MovieMaker;

#nullable enable

public partial class MovieEditor : Widget, IHotloadManaged
{
	public const string HelpUrl = "https://sbox.game/dev/doc/systems/movie-maker/";

	private static readonly ConditionalWeakTable<MovieEditor, object?> _editors = new();

	public static IEnumerable<Session> ActiveSessions => _editors
		.Select( x => x.Key )
		.Where( x => x.IsValid() )
		.Where( x => x.Session is not null )
		.Select( x => x.Session! );

	// We want sessions to survive entering play mode etc., so identify by MoviePlayer component ID
	// and which resource they're editing. A null resource means embedded.

	public readonly record struct SessionKey( Guid PlayerId, string? ResourcePath );

	private readonly Dictionary<SessionKey, Session> _sessions;

	public Session? Session { get; private set; }

	public ListPanel? ListPanel { get; private set; }
	public TimelinePanel? TimelinePanel { get; private set; }
	public HistoryPanel? HistoryPanel { get; private set; }

	public bool ShowHistory
	{
		get => HistoryPanel is { Visible: true };
		set
		{
			if ( HistoryPanel is not { } history ) return;

			EditorCookie.Set( "moviemaker.showhistory", value );
			history.Visible = value;
		}
	}

	public MovieEditor( Widget parent, IReadOnlyDictionary<SessionKey, Session>? sessions = null ) : base( parent )
	{
		_editors.Add( this, null );
		_sessions = sessions?.ToDictionary() ?? new Dictionary<SessionKey, Session>();

		Layout = Layout.Column();
		FocusMode = FocusMode.TabOrClickOrWheel;

		MinimumSize = new Vector2( 800, 300 );

		UpdateEditorContext();

		if ( Session is null )
		{
			CreateStartupHelper();
		}

		EditorUtility.OnInspect += EditorUtility_OnInspect;
	}

	internal IReadOnlyDictionary<SessionKey, Session> Sessions => _sessions.ToImmutableDictionary();

	public override void OnDestroyed()
	{
		Session?.Deactivate();
		Session = null;

		_editors.Remove( this );

		EditorUtility.OnInspect -= EditorUtility_OnInspect;

		base.OnDestroyed();
	}

	[EditorEvent.Hotload]
	private void OnHotload()
	{
		Session?.TrackList.Update();
	}

	private void EditorUtility_OnInspect( EditorUtility.OnInspectArgs ev )
	{
		if ( GetMoviePlayer( ev.Object ) is { } player && Session?.Player != player )
		{
			Switch( player );
		}
	}

	private MoviePlayer? GetMoviePlayer( object? obj )
	{
		if ( obj is Array { Length: > 0 } array )
		{
			obj = array.GetValue( 0 );
		}

		return (obj as GameObject)?.GetComponent<MoviePlayer>();
	}

	private void Initialize( MoviePlayer player, IMovieResource? resource, SessionContext? context )
	{
		Session?.Deactivate();

		var key = new SessionKey( player.Id, (resource as MovieResource)?.ResourcePath );

		if ( _sessions.TryGetValue( key, out var session ) )
		{
			Session = session;
		}
		else
		{
			Session = _sessions[key] = new Session( resource );
		}

		Session.Initialize( this, player, context );

		Layout.Clear( true );

		var splitter = new Splitter( this );

		Layout.Add( splitter );

		// Timeline first, other panels depend on it

		TimelinePanel = new TimelinePanel( this, Session );
		ListPanel = new ListPanel( this, Session );
		HistoryPanel = new HistoryPanel( this, Session );

		splitter.AddWidget( ListPanel );
		splitter.AddWidget( TimelinePanel );
		splitter.AddWidget( HistoryPanel );

		splitter.SetCollapsible( 0, false );
		splitter.SetStretch( 0, 1 );
		splitter.SetCollapsible( 1, false );
		splitter.SetStretch( 1, 3 );

		HistoryPanel.Visible = EditorCookie.Get( "moviemaker.showhistory", false );

		Session.Activate();
	}

	public void CloseSession()
	{
		Layout.Clear( true );

		Session?.Deactivate();
		Session = null;

		ListPanel = null;
		TimelinePanel = null;
		HistoryPanel = null;

		CreateStartupHelper();
	}

	void CreateStartupHelper()
	{
		var row = Layout.AddRow();

		row.AddStretchCell();

		var col = row.AddColumn();
		col.AddStretchCell();

		col.Add( new Label( "Create a Movie Player component to get started.\nThe Movie Player is responsible for playing a movie clip in-game." ) { Alignment = TextFlag.Center } );
		col.AddSpacingCell( 32 );

		var button = col.Add( new Button.Primary( "Create Movie Player Component", "add_circle" ) );

		button.Clicked = CreateNewPlayer;
		button.Enabled = SceneEditorSession.Active is { Scene.IsValid: true };

		col.AddStretchCell();

		row.AddStretchCell();
	}

	[EditorEvent.Frame]
	public void Frame()
	{
		UpdateEditorContext();

		Session?.Frame();
	}

	[Shortcut( "timeline.playtoggle", "Space", ShortcutType.Window )]
	public void PlayToggle()
	{
		if ( Session is null )
			return;

		Session.IsPlaying = !Session.IsPlaying;
	}

	[Shortcut( "timeline.navtostart", "Home", ShortcutType.Window )]
	public void NavigateToStart()
	{
		if ( Session is null )
			return;

		Session.PlayheadTime = 0d;
		TimelinePanel?.Timeline.PanToPlayheadTime();
	}

	[Shortcut( "timeline.navtoend", "End", ShortcutType.Window )]
	public void NavigateToEnd()
	{
		if ( Session is null )
			return;

		Session.PlayheadTime = Session.Duration;
		TimelinePanel?.Timeline.PanToPlayheadTime();
	}

	[Shortcut( "editor.save", "CTRL+S" )]
	public void OnSave()
	{
		Session?.Save();
	}

	[Event( "scene.beforesave" )]
	private static void OnSceneBeforeSave( Scene scene )
	{
		// Save all active sessions involving the saved scene

		foreach ( var moviePlayer in scene.Components.GetAll<MoviePlayer>( FindMode.EverythingInDescendants ) )
		{
			if ( ActiveSessions.FirstOrDefault( x => x.Player == moviePlayer ) is not { } session ) continue;

			try
			{
				if ( session.Resource is MovieResource )
				{
					session.Save();
				}
			}
			catch ( Exception ex )
			{
				Log.Warning( ex );
			}
		}
	}

	[Shortcut( "editor.undo", "CTRL+Z" )]
	public void OnUndo()
	{
		Session?.Undo();
	}

	[Shortcut( "editor.redo", "CTRL+Y" )]
	public void OnRedo()
	{
		Session?.Redo();
	}

	/// <summary>
	/// Look for any clips we can edit. If the clip we're editing has gone - stop editing it.
	/// </summary>
	void UpdateEditorContext()
	{
		var activeEditorSession = SceneEditorSession.Active;
		var activeScene = activeEditorSession?.Scene;

		// The current session exists
		if ( Session is { } session )
		{
			// Whatever we were editing doesn't exist anymore!
			if ( !session.Player.IsValid || session.Player.Scene != activeScene || session.Player.Resource != session.Root.Resource )
			{
				CloseSession();
			}
		}

		// No active scene, can't open a player
		if ( activeScene is null ) return;

		// session is null, lets load the first player
		if ( Session is null )
		{
			if ( activeScene.GetAllComponents<MoviePlayer>().FirstOrDefault() is { } player )
			{
				Switch( player );
			}
		}
	}

	public void Switch( MoviePlayer player )
	{
		Initialize( player, player.Resource, null );
	}

	public void EnterSequence( MovieResource resource, MovieTransform transform, MovieTimeRange timeRange )
	{
		var oldTimeline = TimelinePanel!.Timeline;

		var timeOffset = transform.Inverse * oldTimeline.TimeOffset;
		var pixelsPerSecond = (float)(transform.Inverse.Scale.FrequencyScale * oldTimeline.PixelsPerSecond);

		Initialize( Session!.Player, resource, new SessionContext( Session, transform, timeRange ) );

		TimelinePanel!.Timeline.SetView( timeOffset, pixelsPerSecond );
	}

	public void ExitSequence()
	{
		if ( Session?.Context is not { } context ) return;

		var oldTimeline = TimelinePanel!.Timeline;
		var timeOffset = Session.SequenceTransform * oldTimeline.TimeOffset;
		var pixelsPerSecond = (float)(Session.SequenceTransform.Scale.FrequencyScale * oldTimeline.PixelsPerSecond);

		var resource = Session.Resource;

		Session.Save();
		Initialize( Session.Player, context.Parent.Resource, context.Parent.Context );

		TimelinePanel!.Timeline.SetView( timeOffset, pixelsPerSecond );

		if ( resource is MovieResource movieResource )
		{
			Session.Project.RefreshSequenceTracks( movieResource );
		}
	}

	public void CreateNewPlayer()
	{
		using ( SceneEditorSession.Scope() )
		{
			var go = new GameObject( true, "New Movie Player" );
			go.Components.Create<MoviePlayer>();

			SceneEditorSession.Active.Selection.Set( go );
		}
	}

	public void SwitchToEmbedded()
	{
		if ( Session!.Resource is EmbeddedMovieResource ) return;

		Session.Player.Resource = new EmbeddedMovieResource
		{
			Compiled = Session.Resource.Compiled,
			EditorData = Session.Project.Serialize()
		};

		Switch( Session.Player );
	}

	public void SwitchToNewEmbedded()
	{
		if ( Session is not { } session ) return;

		if ( session is { Resource: EmbeddedMovieResource, Project.IsEmpty: false } )
		{
			Dialog.AskConfirm( ConfirmSwitchToNewEmbedded, question: "The current embedded clip will be lost. Are you sure?" );
			return;
		}

		if ( session is { Resource: MovieResource resource, HasUnsavedChanges: true } )
		{
			Dialog.AskConfirm( () =>
			{
				session.Save();
				ConfirmSwitchToNewEmbedded();
			}, ConfirmSwitchToNewEmbedded, question: $"Save unsaved changes to {resource.ResourceName}.movie?", okay: "Save", cancel: "Don't Save" );
			return;
		}

		ConfirmSwitchToNewEmbedded();
	}

	private void ConfirmSwitchToNewEmbedded()
	{
		if ( Session is not { } session ) return;

        // We're deleting the old embedded session, so remove it from the active session list
		_sessions.Remove( new SessionKey( session.Player.Id, null ) );

		var player = session.Player;

		player.Resource = new EmbeddedMovieResource();

		Switch( player );
	}

	public void SwitchResource( MovieResource resource )
	{
		if ( Session is not { } session ) return;
		if ( session.Root.Resource == resource ) return;

		if ( session is { Resource: EmbeddedMovieResource, Project.IsEmpty: false } )
		{
			Dialog.AskConfirm( () =>
			{
				ConfirmedSwitchResource( resource );
			}, question: "Switching to a clip resource will cause your embedded clip to be lost. Are you sure?" );
			return;
		}

		if ( session is { Resource: MovieResource unsaved, HasUnsavedChanges: true } )
		{
			Dialog.AskConfirm( () =>
				{
					session.Save();
					ConfirmedSwitchResource( resource );
				}, () => ConfirmedSwitchResource( resource ), question: $"Save unsaved changes to {unsaved.ResourceName}.movie?",
				okay: "Save", cancel: "Don't Save" );
			return;
		}

		ConfirmedSwitchResource( resource );
	}

	private void ConfirmedSwitchResource( MovieResource resource )
	{
		Session!.Player.Resource = resource;

		Switch( Session.Player );
	}

	public void SaveFileAs() => SaveAsDialog( "Save Movie As..",
		() => new MovieResource { Compiled = Session!.Project.Compile(), EditorData = Session.Project.Serialize() },
		ConfirmedSwitchResource );

	public void SaveAsDialog( string title, Func<MovieResource> createResource, Action<MovieResource>? afterSave = null )
	{
		var fd = new FileDialog( null );
		fd.Title = title;
		fd.Directory = Project.Current.GetAssetsPath();
		fd.DefaultSuffix = $".movie";
		fd.SetFindFile();
		fd.SetModeSave();
		fd.SetNameFilter( $"Movie Clip File (*.movie)" );

		if ( !fd.Execute() )
			return;

		var sceneAsset = AssetSystem.CreateResource( "movie", fd.SelectedFile );
		var file = createResource();

		sceneAsset.SaveToDisk( file );

		afterSave?.Invoke( file );
	}

	/// <summary>
	/// Returns true if the given movie <paramref name="resource"/> has an open session.
	/// This includes parent sessions of the current session, for nested movies.
	/// </summary>
	public bool IsMovieOpen( IMovieResource resource )
	{
		var session = Session;

		while ( session is not null )
		{
			if ( session.Resource == resource ) return true;

			session = session.Parent;
		}

		return false;
	}
}
