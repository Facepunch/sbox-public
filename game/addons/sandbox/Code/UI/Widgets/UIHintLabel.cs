using Sandbox.UI;

namespace Sandbox;

/// <summary>
/// A label with a <see cref="UIHintPanel"/> for a tooltip: its own text as the title and a
/// description. Without a description there's no tooltip.
/// </summary>
public class UIHintLabel : Label
{
	/// <summary>
	/// What the tooltip says under the name. Can be a localization token or carry markup.
	/// </summary>
	public string Hint { get; set; }

	public override bool HasTooltip => !string.IsNullOrWhiteSpace( Hint );

	protected override Panel CreateTooltipPanel()
	{
		if ( !HasTooltip ) return null;

		var tip = new UIHintPanel( Text, Hint );
		tip.Parent = FindRootPanel();

		return tip;
	}
}
