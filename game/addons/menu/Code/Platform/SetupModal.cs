using MenuProject.Modals;
using Sandbox;
using Sandbox.Modals;
using Sandbox.UI;

namespace MenuProject.Platform;

/// <summary>
/// One of the front's pages up on its own, on the shelf (<see cref="ModalSystem"/>) - setting up a game, or picking a
/// map, for whoever asked. Going back off it, or finishing, closes it.
/// </summary>
public sealed class SetupModal : BaseModal
{
	readonly FrontPages pages;

	SetupModal( string url )
	{
		AddClass( "setup-modal" );

		pages = AddChild<FrontPages>( "pages" );
		pages.DefaultUrl = url;
		pages.OnLeave = () => CloseModal( false );
	}

	/// <summary>
	/// Set up a new game of <paramref name="package"/> - started when it's done, or given to <paramref name="done"/>
	/// instead
	/// </summary>
	public static SetupModal Game( Package package, CreateGameResults? settings = null, string map = null, Action<CreateGameResults> done = null )
	{
		var modal = new SetupModal( FrontPages.Create );
		modal.pages.SetupPackage = package;
		modal.pages.SetupSettings = settings;
		modal.pages.SetupMap = map;
		modal.pages.OnSetUp = done is null ? null : results =>
		{
			done( results );
			modal.CloseModal( true );
		};

		modal.pages.Navigate( FrontPages.Create );
		return modal;
	}

	/// <summary>
	/// Pick a map - given to <paramref name="picked"/>, by its ident
	/// </summary>
	public static SetupModal Map( string selected, Action<string> picked )
	{
		var modal = new SetupModal( FrontPages.Maps );
		modal.pages.PickMap( selected, map =>
		{
			picked?.Invoke( map );
			modal.CloseModal( true );
		} );

		return modal;
	}
}
