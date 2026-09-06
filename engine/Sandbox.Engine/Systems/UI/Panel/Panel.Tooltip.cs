
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
	/// Build a richer tooltip than a line of text. Called with the tooltip panel as it's about to be
	/// shown - add whatever it should contain: labels, images, anything. If <see cref="Tooltip"/> is
	/// set too, that text is already in there as the first child.
	/// </summary>
	[Parameter]
	public Action<Panel> OnTooltip { get; set; }

	/// <summary>
	/// You should override and return true if you're overriding <see cref="CreateTooltipPanel"/>.
	/// Otherwise this will return true if <see cref="Tooltip"/> or <see cref="OnTooltip"/> is set.
	/// </summary>
	[Hide]
	public virtual bool HasTooltip => !string.IsNullOrWhiteSpace( Tooltip ) || OnTooltip is not null;

	/// <summary>
	/// Pushes the global context to whatever is suitable for this panel.
	/// This should never really have to be called, when panels tick render etc. they'll already be in the right context.
	/// This is for when the UI system is used outside of the standard contexts, like tooltips.
	/// </summary>
	IDisposable PushGlobalContext()
	{
		var host = UISystem;

		if ( host is not null && host == GlobalContext.Menu?.UISystem ) return GlobalContext.MenuScope();
		if ( host is not null && host == GlobalContext.Game?.UISystem ) return GlobalContext.GameScope();

		// A surface of its own, like an editor window - whatever context we're in is the right one
		return null;
	}

	/// <summary>
	/// Create a tooltip panel. You can override this to create a custom tooltip panel.<br/>
	/// If you're overriding this and not setting <see cref="Tooltip"/>, then you must override and return true in <see cref="HasTooltip"/>.
	/// </summary>
	protected virtual Panel CreateTooltipPanel()
	{
		if ( string.IsNullOrWhiteSpace( Tooltip ) && OnTooltip is null )
			return null;

		using var scope = PushGlobalContext();

		var p = new Panel( null );
		p.AddClass( "tooltip" );
		p.AddClass( TooltipClass );
		p.SetProperty( "style", "position: absolute; pointer-events: none; z-index: 10000;" );

		if ( !string.IsNullOrWhiteSpace( Tooltip ) )
		{
			var textContents = new Label
			{
				Parent = p,
				Text = Tooltip
			};
		}

		OnTooltip?.Invoke( p );

		p.Parent = FindRootPanel();

		return p;
	}

	/// <summary>
	/// The tooltip system's way in to <see cref="CreateTooltipPanel"/>.
	/// </summary>
	internal Panel BuildTooltip() => CreateTooltipPanel();

	/// <summary>
	/// If the tooltip text changed while the tooltip is up, we'll update it here. Only the plain
	/// text tooltip - a tooltip built by hand is whatever it was built as.
	/// </summary>
	internal void UpdateTooltip( Panel tooltipPanel )
	{
		if ( OnTooltip is not null ) return;
		if ( !tooltipPanel.HasChildren ) return;
		if ( tooltipPanel.ChildrenCount != 1 ) return;
		if ( tooltipPanel.Children.First() is not Label textPanel ) return;

		textPanel.Text = Tooltip;
	}
}
