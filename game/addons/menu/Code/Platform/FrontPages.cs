using Sandbox;
using Sandbox.Modals;
using Sandbox.UI;
using Sandbox.UI.Navigation;

namespace MenuProject.Platform;

/// <summary>
/// The default front's pages under the game's name - its menu (<see cref="FrontHome"/>), setting up a new game
/// (<see cref="CreateGamePage"/>), picking its map (<see cref="MapsPage"/>) and its servers (<see cref="ServersPage"/>).
/// One fades out and the next fades in (GameFront's stylesheet). Also up on its own, in a modal on the shelf
/// (<see cref="SetupModal"/>), to set up a game a game asked for or to pick a map.
/// </summary>
public class FrontPages : NavigationHost
{
	public const string Home = "/platform/front";
	public const string Create = "/platform/create";
	public const string Servers = "/platform/servers";
	public const string Maps = "/platform/maps";

	public bool OffHome => CurrentUrl is not null && CurrentUrl != Home;

	/// <summary>
	/// The game being set up - the front's game (<see cref="PlatformMenu.Current"/>) when it's not given
	/// </summary>
	public Package SetupPackage { get; set; }

	/// <summary>
	/// What it's set up with to start - the party's, or its own defaults, when it's not given
	/// </summary>
	public CreateGameResults? SetupSettings { get; set; }

	/// <summary>
	/// The map it starts on, when one's been picked already
	/// </summary>
	public string SetupMap { get; set; }

	/// <summary>
	/// Given what's been set up instead of starting it - a game asked for its setup (Game.Overlay.CreateGame)
	/// </summary>
	public Action<CreateGameResults> OnSetUp { get; set; }

	/// <summary>
	/// Off its first page, when it's up on its own - the modal it's in closes
	/// </summary>
	public Action OnLeave { get; set; }

	/// <summary>
	/// Back a page - or out of it, from its first, when it's up on its own
	/// </summary>
	public void Leave()
	{
		if ( OnLeave is not null && (CurrentUrl == DefaultUrl || CurrentUrl is null) ) OnLeave();
		else GoBack();
	}

	public FrontPages()
	{
		DefaultUrl = Home;
	}

	/// <summary>
	/// The map the map browser's picking in place of
	/// </summary>
	public string PickingMap { get; private set; }

	Action<string> mapPicked;

	/// <summary>
	/// On to the map browser - <paramref name="picked"/> gets the one picked there, as it comes back to this page
	/// </summary>
	public void PickMap( string current, Action<string> picked )
	{
		PickingMap = current;
		mapPicked = picked;
		Navigate( Maps );
	}

	public void MapPicked( string map )
	{
		var picked = mapPicked;
		mapPicked = null;

		GoBack();
		picked?.Invoke( map );
	}

	public static FrontPages Of( Panel panel ) => panel.AncestorsAndSelf.OfType<FrontPages>().FirstOrDefault();

	/// <summary>
	/// A click on a page's empty space, round its panel and sections - back to its menu, as clicking round a shelf puts
	/// it away. Anywhere on the page but its panel and its sections' tabs: the column the sections are in runs the
	/// page's height, so its empty space above and below them counts too.
	/// </summary>
	protected override void OnClick( MousePanelEvent e )
	{
		base.OnClick( e );

		var target = e.Target;
		if ( !OffHome || target is null || !CurrentPanel.IsValid ) return;
		if ( !target.AncestorsAndSelf.Contains( CurrentPanel ) ) return;
		if ( target.AncestorsAndSelf.TakeWhile( x => x != CurrentPanel ).Any( x => x.HasClass( "panel" ) || x.HasClass( "tab" ) ) ) return;

		GoBack();
	}
}
