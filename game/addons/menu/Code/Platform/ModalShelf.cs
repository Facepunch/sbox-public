using MenuProject.Modals;
using Sandbox.UI;

namespace MenuProject.Platform;

/// <summary>
/// A modal on the shelf, up out of the dock like the rest of its pages (<see cref="PlatformRoot.ShowModal"/>). Each one
/// it's given gets a url of its own, so going back to the page it was opened from works the same - and it's not kept once
/// it's gone from, as a game's page isn't.
/// </summary>
[Microsoft.AspNetCore.Components.Route( "/shelf/modal/{Key}" )]
public class ModalShelf : ShelfPage
{
	const string Prefix = "/shelf/modal/";

	static int made;
	static readonly Dictionary<string, BaseModal> waiting = new();

	/// <summary>
	/// The url <paramref name="modal"/> is shown at - it's put on the page made for it
	/// </summary>
	public static string UrlFor( BaseModal modal )
	{
		var key = (++made).ToString();
		waiting[key] = modal;
		return Prefix + key;
	}

	public static bool IsModal( string url ) => url?.StartsWith( Prefix ) == true;

	public BaseModal Modal { get; private set; }

	public ModalShelf()
	{
		AddClass( "modal-shelf" );
	}

	public override void SetProperty( string name, string value )
	{
		if ( name.Equals( "key", StringComparison.OrdinalIgnoreCase ) )
		{
			if ( Modal is null && waiting.Remove( value, out var modal ) )
			{
				Modal = modal;
				Modal.Parent = this;
			}

			return;
		}

		base.SetProperty( name, value );
	}

	/// <summary>
	/// The modal in the middle of the shelf - clicking round it puts it away
	/// </summary>
	public override bool ClicksThrough => true;

	public override void OnDeleted()
	{
		base.OnDeleted();
		Modal?.Delete( true );
	}
}
