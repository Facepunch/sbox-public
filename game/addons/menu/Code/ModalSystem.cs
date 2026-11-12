using MenuProject.Modals;
using MenuProject.Platform;
using Sandbox;
using Sandbox.Modals;

/// <summary>
/// What games and the menu open over themselves. Pages - a map, a server list, a player - come up out of the dock as
/// pages of the shelf (<see cref="PlatformRoot.ShowModal"/>). Questions and forms - a review, a report, a password -
/// pop up over everything on the overlay, the loading screen too.
/// </summary>
public class ModalSystem : IModalSystem
{
	public static ModalSystem Instance;

	List<BaseModal> OpenModals = new();

	public ModalSystem()
	{
		Instance = this;
	}

	static PlatformRoot Root => PlatformRoot.Instance;

	public bool HasModalsOpen()
	{
		if ( IsPauseMenuOpen )
			return true;

		if ( Root?.WantsMouse ?? false )
			return true;

		return OpenModals.Any( x => x.WantsMouseInput() );
	}

	/// <summary>
	/// A modal's up over everything on the overlay - not an anchored popup
	/// </summary>
	internal bool HasOverlayModalsOpen => OpenModals.Any( x => x.IsValid() && x.WantsMouseInput() && !x.HasClass( "anchored" ) );

	public void CloseAll( bool immediate = false )
	{
		foreach ( var modal in OpenModals )
		{
			modal.Delete( immediate );
		}

		OpenModals.Clear();

		Root?.CloseModal( Root.ShelfModal );
	}

	/// <summary>
	/// Add a modal to the overlay, stack it above any others, and start tracking it if it's a <see cref="BaseModal"/>.
	/// </summary>
	protected void Push( Panel modal )
	{
		MenuOverlay.Instance.AddChild( modal );

		modal.Style.ZIndex = (OpenModals.LastOrDefault()?.Style.ZIndex ?? 1000) + 10;

		if ( modal is BaseModal basemodal )
		{
			basemodal.OnClosed += ( s ) => OnModalClosing( basemodal, s );
			OpenModals.Add( basemodal );

			if ( !basemodal.HasClass( "anchored" ) ) Root?.PauseForModal();
		}
	}

	/// <summary>
	/// Up out of the dock as a page of the shelf - gone back from once it's closed. Opened from a modal or a game's page
	/// already on it, it's over that instead, so what it was opened from is still there to come back to.
	/// </summary>
	void Shelve( BaseModal modal )
	{
		if ( Root is not { } root || root.ShelfModal is not null || PlatformShelf.IsGame( root.Shelf ) )
		{
			Push( modal );
			return;
		}

		modal.OnClosed += _ => root.CloseModal( modal );
		root.ShowModal( modal );
	}

	void OnModalClosing( BaseModal modal, bool success )
	{
		modal.Delete();
		OpenModals.Remove( modal );
	}

	/// <summary>
	/// Close any open modal of the given type. Returns true if one was open - useful for toggling.
	/// </summary>
	bool CloseExisting<T>() where T : BaseModal
	{
		OpenModals.RemoveAll( x => !x.IsValid() );

		if ( OpenModals.OfType<T>().FirstOrDefault() is { } existing )
		{
			existing.CloseModal( true );
			return true;
		}

		return false;
	}

	/// <summary>
	/// Put the friends list away if it's up - true if it was
	/// </summary>
	public bool CloseFriendsList() => CloseExisting<FriendsListModal>();

	/// <summary>
	/// Is a modal of the given type currently open - over everything, or on the shelf?
	/// </summary>
	public bool IsOpen<T>() where T : BaseModal
	{
		return OpenModals.Any( x => x.IsValid() && x is T ) || Root?.ShelfModal is T;
	}

	public void Game( string packageIdent )
	{
		if ( string.IsNullOrEmpty( packageIdent ) ) return;

		Root?.ShowGame( packageIdent );
	}

	public void Map( string packageIdent ) => Package( packageIdent );

	/// <summary>
	/// A package's page, up out of the dock - a game's, a map's, anything's - on <paramref name="page"/> of it
	/// ("changes", "achievements") if given
	/// </summary>
	public void Package( string packageIdent, string page = "" )
	{
		if ( string.IsNullOrEmpty( packageIdent ) ) return;

		Root?.ShowGame( packageIdent, null, string.IsNullOrEmpty( page ) ? null : page );
	}

	/// <summary>
	/// Opens the shared picker with the requested package types and filters.
	/// </summary>
	public void PackageSelect( string query, Action<Package> onPackageSelected, Action<string> onFilterChanged )
	{
		Shelve( new PackagePicker
		{
			Query = query,
			OnPicked = onPackageSelected,
			OnQueryChanged = onFilterChanged
		} );
	}

	public void MapSelect( Action<string> onSelected, string selected )
	{
		Shelve( SetupModal.Map( selected, onSelected ) );
	}

	public void Review( Package package )
	{
		Push( new ReviewModal { Package = package } );
	}

	/// <summary>
	/// The friends list - only ever one. Asked for again off what brought it up, it's put away instead.
	/// </summary>
	public void FriendsList( in FriendsListModalOptions config )
	{
		OpenModals.RemoveAll( x => !x.IsValid() );

		if ( OpenModals.OfType<FriendsListModal>().FirstOrDefault() is { } open )
		{
			var again = open.AnchorPanel is not null && open.AnchorPanel == config.Anchor?.Panel;
			CloseExisting<FriendsListModal>();
			if ( again ) return;
		}

		Push( new FriendsListModal( config ) );
	}

	public void ServerList( in ServerListConfig config )
	{
		Shelve( new ServerListModal( config ) );
	}

	public void Server( Sandbox.Network.LobbyInformation lobby )
	{
		Shelve( new ServerModal { Server = lobby } );
	}

	public void PlayerList()
	{
		Shelve( new PlayersModal() );
	}

	/// <summary>
	/// Settings up on a category, or put away if it's up and none's asked for
	/// </summary>
	public void Settings( string page = "" )
	{
		Root?.ToggleSettings( MenuProject.Settings.SettingsCatalog.ResolveCategory( page ) );
	}

	public void ServiceConnector()
	{
		Push( new ServicesModal() );
	}

	/// <summary>
	/// Set up a new game of a package, on the shelf - finished, it's handed to the options' callback rather than started.
	/// </summary>
	public void CreateGame( in CreateGameOptions options )
	{
		if ( options.Package is null ) return;
		Shelve( SetupModal.Game( options.Package, options.InitialSettings, done: options.OnComplete ) );
	}

	/// <summary>
	/// Set up a new game of <paramref name="package"/> to start it, on the shelf - on <paramref name="map"/>, if one's
	/// been picked
	/// </summary>
	internal void SetUpGame( Package package, string map = null )
	{
		if ( package is null ) return;
		Shelve( SetupModal.Game( package, map: map ) );
	}

	/// <summary>
	/// Escape - the top modal steps back or goes, otherwise the platform takes it (what's up on it, or pausing)
	/// </summary>
	public void PauseMenu()
	{
		OpenModals.RemoveAll( x => !x.IsValid() );

		if ( OpenModals.Any() )
		{
			var top = OpenModals.Last();
			if ( top.Back() ) return;

			top.Delete();
			OpenModals.Remove( top );
			return;
		}

		Root?.Escape();
	}

	public void Player( SteamId steamid, string page = "" )
	{
		Shelve( new PlayerModal { Page = page, SteamId = steamid } );
	}

	public void News( Sandbox.Services.News news )
	{
		Shelve( new PackageNewsModal { News = news } );
	}

	public void WorkshopPublish( in WorkshopPublishOptions options )
	{
		Push( new WorkshopPublishModal { Options = options } );
	}

	/// <summary>
	/// The notice that's up, if there is one.
	/// </summary>
	QuestionModal _notice;

	/// <summary>
	/// Something to be told - disconnected by the host, a party that's gone - as a question with the one
	/// answer, in the menu's blue. One at a time: a new one takes the place of the last rather than
	/// piling up over it for each to be clicked away.
	/// </summary>
	public void Notice( string title, string message, string icon )
	{
		if ( _notice.IsValid() )
			_notice.CloseModal( false );

		_notice = new QuestionModal
		{
			Title = title,
			Message = message,
			ConfirmText = "#menu.common.ok",
			CancelText = null
		};

		Push( _notice );
	}

	/// <summary>
	/// A server we're joining wants its password (sv_password). Null if they'd rather not join.
	/// </summary>
	public Task<string> ServerPassword( string serverName )
	{
		var answer = new TaskCompletionSource<string>();

		var modal = new QuestionModal
		{
			Title = string.IsNullOrWhiteSpace( serverName ) ? "#menu.games.password" : serverName,
			Message = "#menu.games.password_needed",
			ConfirmText = "#menu.common.join",
			ConfirmIcon = "key",
			Entry = ""
		};

		// Before it's deleted - and gone without an answer if everything's closed
		modal.OnClosed += confirmed => answer.TrySetResult( confirmed ? modal.Entry : null );
		modal.OnGone = () => answer.TrySetResult( null );

		Push( modal );
		return answer.Task;
	}

	/// <summary>
	/// The open link question that's up, if there is one.
	/// </summary>
	QuestionModal _openUrl;

	/// <summary>
	/// A game wants to open a web page (Game.Overlay.OpenUrl) - ask first, showing the link as it'll open.
	/// While one's up, more are ignored, so a game can't swap the link out from under the click.
	/// </summary>
	public void OpenUrl( string url )
	{
		if ( string.IsNullOrEmpty( url ) || _openUrl.IsValid() )
			return;

		_openUrl = new QuestionModal
		{
			Title = "#menu.overlay.open_url",
			Message = url,
			ConfirmText = "#menu.overlay.open_url_confirm",
			ConfirmIcon = "open_in_new",
			OnConfirm = () => MenuUtility.OpenUrl( url )
		};

		_openUrl.AddClass( "open-url" );

		Push( _openUrl );
	}

	/// <summary>
	/// <c>menu_mock_notice</c> - a notice like the host's when it drops you, to look at without being dropped.
	/// With "error" for the two paragraph kind a failed load gives.
	/// </summary>
	[MenuConCmd( "menu_mock_notice", Help = "Show a made up notice - 'error' for a load error's" )]
	public static void MockNotice( string kind = "" )
	{
		if ( kind == "error" )
		{
			Instance?.Notice( "Loading Error", "An error occurred when loading this game.\n\nCouldn't find scene 'maps/flatgrass.scene'.", "error" );
			return;
		}

		Instance?.Notice( "Disconnected", "The host closed the server.", "wifi_off" );
	}

	public void BenchmarkResults( Guid batchId, IReadOnlyList<BenchmarkTestSummary> summaries )
	{
		Shelve( new BenchmarkResultModal( batchId, summaries ) );
	}

	public void Report( string packageIdent )
	{
		Push( new ReportModal { PackageIdent = packageIdent } );
	}

	public void Open( Panel modal )
	{
		Push( modal );
	}

	public bool IsModalOpen => HasModalsOpen();
	public bool IsPauseMenuOpen => Root?.Paused ?? false;
}
