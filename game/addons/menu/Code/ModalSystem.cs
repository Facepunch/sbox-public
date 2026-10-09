using MenuProject.Modals;
using MenuProject.Modals.PauseMenuModal;
using Sandbox;
using Sandbox.Modals;

public class ModalSystem : IModalSystem
{
	public static ModalSystem Instance;

	List<BaseModal> OpenModals = new();

	public ModalSystem()
	{
		Instance = this;
	}

	public bool HasModalsOpen()
	{
		if ( IsPauseMenuOpen )
			return true;

		return OpenModals.Any( x => x.WantsMouseInput() );
	}

	/// <summary>
	/// A modal that dims and blurs everything behind it is open. Anchored ones (the rewards drop,
	/// the friends list off a button) are popups that leave the page as it is, so they don't count.
	/// The pause menu does, unless <paramref name="countPauseMenu"/>'s off - for what lives in it,
	/// it's the page rather than something over it.
	/// </summary>
	public bool HasBlockingModalsOpen( bool countPauseMenu = true )
	{
		if ( countPauseMenu && IsPauseMenuOpen )
			return true;

		return OpenModals.Any( x => x.WantsMouseInput() && !x.HasClass( "anchored" ) );
	}

	public void CloseAll( bool immediate = false )
	{
		foreach ( var modal in OpenModals )
		{
			modal.Delete( immediate );
		}

		OpenModals.Clear();

		PauseModal.Instance?.SetClass( "hidden", true );
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
		}
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
	/// Is a modal of the given type currently open?
	/// </summary>
	public bool IsOpen<T>() where T : BaseModal
	{
		return OpenModals.Any( x => x.IsValid() && x is T );
	}

	/// <summary>
	/// The item drop popup, anchored beside <paramref name="anchor"/> (the rail's Rewards entry).
	/// Toggles closed if it's already open.
	/// </summary>
	public void Rewards( Panel anchor )
	{
		if ( CloseExisting<RewardsModal>() ) return;

		Push( new RewardsModal( anchor ) );
	}

	public void Game( string packageIdent )
	{
		if ( string.IsNullOrEmpty( packageIdent ) ) return;

		CloseExisting<GameModal>();
		Push( new GameModal { PackageIdent = packageIdent } );
	}

	public void Map( string packageIdent )
	{
		if ( string.IsNullOrEmpty( packageIdent ) ) return;

		CloseExisting<MapModal>();
		Push( new MapModal { PackageIdent = packageIdent } );
	}

	public void Package( string packageIdent, string page = "" )
	{
		// Toggle closed if it's already open
		if ( CloseExisting<PackageModal>() ) return;

		Push( new PackageModal { Page = page, PackageIdent = packageIdent } );
	}

	/// <summary>
	/// Opens the shared picker with the requested package types and filters.
	/// </summary>
	public void PackageSelect( string query, Action<Package> onPackageSelected, Action<string> onFilterChanged )
	{
		Push( new PackageSelectionModal
		{
			PackageQuery = query,
			OnPackageSelected = onPackageSelected,
			OnFilterChanged = onFilterChanged
		} );
	}

	public void MapSelect( Action<string> onSelected, string selected )
	{
		var modal = new MapSelectorModal();
		modal.OnSelected = onSelected;
		modal.SetSelected( selected );

		Push( modal );
	}

	public void Organization( Package.Organization org )
	{
		CloseExisting<OrganizationModal>();
		Push( new OrganizationModal { Org = org } );
	}

	public void Review( Package package )
	{
		Push( new ReviewModal { Package = package } );
	}

	public void FriendsList( in FriendsListModalOptions config )
	{
		Push( new FriendsListModal( config ) );
	}

	public void ServerList( in ServerListConfig config )
	{
		Push( new ServerListModal( config ) );
	}

	public void Server( Sandbox.Network.LobbyInformation lobby )
	{
		Push( new ServerModal { Server = lobby } );
	}

	public void PlayerList()
	{
		Push( new PlayerListModal() );
	}

	public void Settings( string page = "" )
	{
		var category = MenuProject.Settings.SettingsCatalog.ResolveCategory( page );

		// In a game there's no menu to navigate, so the same view goes up full bleed instead.
		if ( !Sandbox.Game.InGame && MenuProject.MainMenu.Instance?.Navigator is not null )
		{
			CloseExisting<SettingsModal>();
			MenuProject.MainMenu.Instance.Navigator.Navigate( string.IsNullOrEmpty( category ) ? "/settings" : $"/settings?Category={category}" );
			return;
		}

		// With the pause menu up, settings is one of its pages - opened in it, so its rail stays
		if ( PauseModal.Open?.Navigate( string.IsNullOrEmpty( category ) ? "/settings" : $"/settings?Category={category}" ) ?? false )
		{
			CloseExisting<SettingsModal>();
			return;
		}

		if ( CloseExisting<SettingsModal>() ) return;

		Push( new SettingsModal( category ) );
	}

	public void ServiceConnector()
	{
		Push( new ServicesModal() );
	}

	/// <summary>
	/// Set up a new game of a package - on its game page, in the page's place, rather than a window of
	/// its own over it. The page it's already on if that's open, or a new one opened straight into it.
	/// </summary>
	public void CreateGame( in CreateGameOptions options ) => CreateGame( options, null, null );

	/// <summary>
	/// Set up a new game the way <see cref="CreateGame(in CreateGameOptions)"/> does - from a play button,
	/// named in <paramref name="via"/> with the tile it was on, so the play's reported once it's started.
	/// </summary>
	public void CreateGame( in CreateGameOptions options, string via, Panel source )
	{
		if ( options.Package is null ) return;
		_ = GetGamePage( options.Package ).OpenSetup( options.Package, options.OnComplete, via, source, options.InitialSettings );
	}

	internal void OpenGameSetup( Package package, string via = null, Panel source = null, MenuProject.PartyDraft draft = null, string initialMap = null )
	{
		if ( package is null ) return;
		var selected = draft is not null ? draft.Game?.FullIdent == package.FullIdent : MenuProject.PartyView.IsGameSelected( package );
		var shared = draft is not null ? draft.GameSettings : MenuProject.PartyView.SelectedGameSettings;
		_ = GetGamePage( package ).OpenSetup( package, null, via, source, selected ? shared : null, partyActions: true, draft: draft, initialMap: initialMap );
	}

	GameModal GetGamePage( Package package )
	{
		OpenModals.RemoveAll( x => !x.IsValid() );

		var page = OpenModals.OfType<GameModal>().FirstOrDefault( x => x.PackageIdent == package.FullIdent || x.Package == package );
		if ( page is null )
		{
			CloseExisting<GameModal>();

			page = new GameModal { PackageIdent = package.FullIdent, Package = package };
			Push( page );
		}

		return page;
	}

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

		if ( PauseModal.Instance is { } pause && pause.IsValid() )
		{
			pause.ToggleClass( "hidden" );
			return;
		}

		MenuOverlay.Instance.AddChild( new PauseModal() );
	}

	public void Player( SteamId steamid, string page = "" )
	{
		Push( new PlayerModal { Page = page, SteamId = steamid } );
	}

	public void News( Sandbox.Services.News news )
	{
		Push( new PackageNewsModal { News = news } );
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
			ConfirmText = "Ok",
			CancelText = null
		};

		Push( _notice );
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
		Push( new BenchmarkResultModal( batchId, summaries ) );
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
	public bool IsPauseMenuOpen => PauseModal.Open is not null;
}
