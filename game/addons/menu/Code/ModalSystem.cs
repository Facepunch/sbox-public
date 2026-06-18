using MenuProject.Modals;
using MenuProject.Modals.PauseMenuModal;
using Sandbox;
using Sandbox.Modals;

public class ModalSystem : IModalSystem
{
	public static ModalSystem Instance;

	List<BaseModal> OpenModals = new();

	PauseModal _pauseModal;

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

	public void CloseAll( bool immediate = false )
	{
		foreach ( var modal in OpenModals )
		{
			modal.Delete( immediate );
		}

		OpenModals.Clear();

		_pauseModal?.SetClass( "hidden", true );
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
		Push( new SettingsModal( page ) );
	}

	public void ServiceConnector()
	{
		Push( new ServicesModal() );
	}

	public void CreateGame( in CreateGameOptions options )
	{
		Push( new CreateGameModal( options ) );
	}

	public void PauseMenu()
	{
		OpenModals.RemoveAll( x => !x.IsValid() );

		if ( OpenModals.Any() )
		{
			var top = OpenModals.Last();
			top.Delete();
			OpenModals.Remove( top );
			return;
		}

		_pauseModal = MenuOverlay.Instance.Children.OfType<PauseModal>().FirstOrDefault();

		if ( _pauseModal != null )
		{
			_pauseModal.ToggleClass( "hidden" );
			return;
		}

		_pauseModal = new PauseModal();
		MenuOverlay.Instance.AddChild( _pauseModal );
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

	public void Notice( string title, string message, string icon )
	{
		Push( new NoticeModal
		{
			Title = title,
			Message = message,
			Icon = icon
		} );
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
	public bool IsPauseMenuOpen => _pauseModal.IsValid() && _pauseModal.IsPauseMenuOpen();
}
