using Sandbox.UI;

namespace Sandbox;

/// <summary>
/// Displays the current device glyph for an action or the keyboard glyph for a physical binding.
/// </summary>
public class InputHint : Panel
{
	/// <summary>
	/// The action whose binding to display.
	/// </summary>
	[Parameter] public string Action { get; set; }

	/// <summary>
	/// The physical key to display for a binding captured without an action.
	/// </summary>
	[Parameter] public string Key { get; set; }

	/// <summary>
	/// The requested resolution of the glyph texture.
	/// </summary>
	[Parameter] public InputGlyphSize GlyphSize { get; set; } = InputGlyphSize.Medium;

	// Resolving a glyph builds strings and a lookup every call. It only changes with a rebind or a switch between
	// keyboard and controller, so look again on those or twice a second.
	(string Action, string Key, InputGlyphSize Size, bool Controller) _resolvedFor;
	RealTimeSince _sinceResolved;

	/// <inheritdoc/>
	public override void Tick()
	{
		var inputs = (Action, Key, GlyphSize, Input.UsingController);
		if ( inputs == _resolvedFor && _sinceResolved < 0.5f ) return;
		_resolvedFor = inputs;
		_sinceResolved = 0;

		var glyph = string.IsNullOrWhiteSpace( Action ) ? null : Input.GetGlyph( Action, GlyphSize, false );
		if ( Key is not null ) glyph = Input.Keyboard.GetGlyph( Key, GlyphSize, false );
		if ( glyph == Style.BackgroundImage ) return;

		Style.BackgroundImage = glyph;
		Style.AspectRatio = glyph.IsValid() ? (float)glyph.Width / glyph.Height : null;
	}
}
