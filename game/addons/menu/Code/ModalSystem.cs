using MenuProject.Modals;
using MenuProject.Modals.PauseMenuModal;
using Sandbox;
using Sandbox.Modals;
using Sandbox.Services;

public class ModalSystem : IModalSystem
{
	public static ModalSystem Instance;

	List<BaseModal> OpenModals = new List<BaseModal>();

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
	}

	protected void Push( BaseModal modal )
	{
		MenuOverlay.Instance.AddChild( modal );
		modal.OnClosed += ( s ) => OnModalClosing( modal, s );
		OpenModals.Add( modal );
	}

	void OnModalClosing( BaseModal modal, bool success )
	{
		modal.Delete();
		OpenModals.Remove( modal );
	}

	public void Game( string packageIdent )
	{
		if ( string.IsNullOrEmpty( packageIdent ) ) return;

		OpenModals.RemoveAll( x => !x.IsValid() );

		if ( OpenModals.OfType<GameModal>().FirstOrDefault() is { } gameModal )
		{
			// Close existing modal when hitting it again
			gameModal.CloseModal( true );
		}

		var modal = new GameModal();
		modal.PackageIdent = packageIdent;

		Push( modal );
	}

	public void Map( string packageIdent )
	{
		if ( string.IsNullOrEmpty( packageIdent ) ) return;

		OpenModals.RemoveAll( x => !x.IsValid() );

		if ( OpenModals.OfType<MapModal>().FirstOrDefault() is { } mapModal )
		{
			// Close existing modal when hitting it again
			mapModal.CloseModal( true );
		}

		var modal = new MapModal();
		modal.PackageIdent = packageIdent;

		Push( modal );
	}

	public void Package( string packageIdent, string page = "" )
	{
		OpenModals.RemoveAll( x => !x.IsValid() );

		// should probably bring it to top?
		if ( OpenModals.OfType<PackageModal>().FirstOrDefault() is { } packageModal )
		{
			// Close the modal when hitting it again
			packageModal.CloseModal( true );
			return;
		}

		var modal = new PackageModal();
		modal.Page = page;
		modal.PackageIdent = packageIdent;

		Push( modal );
	}

	public void PackageSelect( string query, Action<Package> onPackageSelected, Action<string> onFilterChanged )
	{
		var modal = new PackageSelectionModal();
		modal.PackageQuery = query;
		modal.OnPackageSelected = onPackageSelected;
		modal.OnFilterChanged = onFilterChanged;

		Push( modal );
	}

	public void Organization( Package.Organization org )
	{
		if ( OpenModals.OfType<OrganizationModal>().FirstOrDefault() is { } packageModal )
		{
			// Close the modal when hitting it again
			packageModal.CloseModal( true );
		}

		var modal = new OrganizationModal();
		modal.Org = org;
		Push( modal );
	}
	public void Review( Package package )
	{
		var modal = new ReviewModal();
		modal.Package = package;
		Push( modal );
	}

	public void FriendsList( in FriendsListModalOptions config )
	{
		Push( new FriendsListModal( config ) );
	}

	public void ServerList( in ServerListConfig config )
	{
		var modal = new ServerListModal( config );
		Push( modal );
	}

	public void PlayerList()
	{
		var modal = new PlayerListModal();
		Push( modal );
	}

	public void Settings( string page = "" )
	{
		var modal = new SettingsModal( page );
		Push( modal );
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
		var modal = new PlayerModal();
		modal.Page = page;
		modal.SteamId = steamid;

		Push( modal );
	}

	public void News( News news )
	{
		Push( new PackageNewsModal { News = news } );
	}

	public void WorkshopPublish( in WorkshopPublishOptions options )
	{
		Push( new WorkshopPublishModal { Options = options } );
	}

	public bool IsModalOpen => HasModalsOpen();
	public bool IsPauseMenuOpen => _pauseModal.IsValid() && _pauseModal.IsPauseMenuOpen();
}
