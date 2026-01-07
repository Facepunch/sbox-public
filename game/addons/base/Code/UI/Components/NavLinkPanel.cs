namespace Sandbox.UI.Navigation;

/// <summary>
/// A panel that will navigate to an href but also have .active class if href is active
/// </summary>
[ClassName( "a" )]
[Alias( "navlink" )]
public class NavLinkPanel : Panel
{
	NavigationHost Navigator;
	public string HRef { get; set; }
	public string Match { get; set; }

	public override void OnParentChanged()
	{
		base.OnParentChanged();

		Navigator = Ancestors.OfType<NavigationHost>().FirstOrDefault();
	}

	protected override void OnClick( MousePanelEvent e )
	{
		if ( e.Button == "mouseleft" )
		{
			this.Navigate( HRef );
		}
	}

	public override void Tick()
	{
		base.Tick();

		if ( HRef == null )
			return;

		var active = Navigator?.CurrentUrlMatches( Match ?? HRef ) ?? false;
		SetClass( "active", active );
	}
}
