using Sandbox.Engine;

namespace Sandbox.UI;

/// <summary>
/// A panel that gets deleted automatically when clicked away from
/// </summary>
public abstract class BasePopup : Panel
{
	static List<BasePopup> AllPopups = new();

	/// <summary>
	/// Stay open, even when CloseAll popups is called
	/// </summary>
	public bool StayOpen { get; set; }

	/// <summary>
	/// The popup this one hangs off, if it's part of a chain like a cascading menu. Closing
	/// everything but a popup spares its chain too, so a click in a submenu keeps the parents up.
	/// </summary>
	protected virtual BasePopup ParentPopup => null;

	public static void CloseAll( Panel exceptThisOne = null )
	{
		if ( AllPopups.Count == 0 )
			return;

		// Each side only closes its own popups, so neither runs the other's popup code. Panel.IsValid,
		// not the IValid extension, which a popup could reimplement.
		var fromMenu = GlobalContext.Current == GlobalContext.Menu;
		bool IsOurs( BasePopup popup ) => (popup.FindRootPanel()?.UISystem is { } ui && ui == GlobalContext.Menu.UISystem) == fromMenu;

		AllPopups.RemoveAll( x => x is not { IsValid: true } );

		BasePopup floater = null;

		if ( exceptThisOne is Panel flt )
		{
			floater = flt.AncestorsAndSelf.OfType<BasePopup>().FirstOrDefault();
			if ( floater is not null && !IsOurs( floater ) ) floater = null;
		}

		foreach ( var panel in AllPopups.ToArray() )
		{
			if ( !IsOurs( panel ) ) continue;
			if ( panel == floater ) continue;
			if ( panel.StayOpen && panel.Parent is { IsValid: true } ) continue;
			if ( floater is not null && floater.IsInChainOf( panel ) ) continue;

			try
			{
				AllPopups.RemoveAll( x => ReferenceEquals( x, panel ) );
				panel.Delete();
			}
			catch
			{
				// ignored
			}
		}
	}

	public BasePopup()
	{
		AllPopups.Add( this );
	}

	bool IsInChainOf( BasePopup ancestor )
	{
		for ( var popup = ParentPopup; popup is not null; popup = popup.ParentPopup )
		{
			if ( popup == ancestor ) return true;
		}

		return false;
	}

	public override void OnDeleted()
	{
		base.OnDeleted();

		AllPopups.RemoveAll( x => ReferenceEquals( x, this ) );
	}
}
