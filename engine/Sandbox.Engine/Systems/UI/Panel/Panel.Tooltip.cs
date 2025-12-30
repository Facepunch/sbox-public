
using Microsoft.AspNetCore.Components;
using Sandbox.Engine;

namespace Sandbox.UI;

public partial class Panel
{
	/// <summary>
	/// A string to show when hovering over this panel.
	/// </summary>
	[Parameter]
	public string Tooltip { get; set; }

	/// <summary>
	/// The created tooltip element will have this class, if set.
	/// </summary>
	[Parameter]
	public string TooltipClass { get; set; }

	/// <summary>
	/// You should override and return true if you're overriding <see cref="CreateTooltipPanel"/>.
	/// Otherwise this will return true if <see cref="Tooltip"/> is not empty.
	/// </summary>
	[Hide]
	public virtual bool HasTooltip => !string.IsNullOrWhiteSpace( Tooltip );

	/// <summary>
	/// Pushes the global context to whatever is suitable for this panel.
	/// This should never really have to be called, when panels tick render etc. they'll already be in the right context.
	/// This is for when the UI system is used outside of the standard contexts, like tooltips.
	/// </summary>
	IDisposable PushGlobalContext()
	{
		var rootPanel = FindRootPanel();
		var isMenu = GlobalContext.Menu.UISystem?.RootPanels.Contains( rootPanel ); // assume game context
		return isMenu == true ? GlobalContext.MenuScope() : GlobalContext.GameScope();
	}

	/// <summary>
	/// Create a tooltip panel. You can override this to create a custom tooltip panel.<br/>
	/// If you're overriding this and not setting <see cref="Tooltip"/>, then you must override and return true in <see cref="HasTooltip"/>.
	/// </summary>
	protected virtual Panel CreateTooltipPanel()
	{
		if ( string.IsNullOrWhiteSpace( Tooltip ) )
			return null;

		using var scope = PushGlobalContext();

		var p = new Panel( null );
		p.AddClass( "tooltip" );
		p.AddClass( TooltipClass );
		p.SetProperty( "style", "position: absolute; pointer-events: none; z-index: 10000;" );

		var textContents = new Label
		{
			Parent = p,
			Text = Tooltip
		};

		p.Parent = FindRootPanel();

		return p;
	}

}
