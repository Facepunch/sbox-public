using Sandbox.UI;

namespace Sandbox;

public class InputHint : Panel
{
	Texture _lastGlyph;
	string _lastOrigin;

	[Parameter] public string Action { get; set; }
	[Parameter] public InputGlyphSize GlyphSize { get; set; } = InputGlyphSize.Medium;

	/// <summary>
	/// A raw key to show instead of <see cref="Action"/>'s.
	/// </summary>
	[Parameter] public string Key { get; set; }

	// Resolving a glyph builds strings and a lookup every call. It only changes with a rebind or a switch between
	// keyboard and controller, so look again on those or twice a second.
	(string Key, string Action, InputGlyphSize Size, bool Controller) _resolvedFor;
	RealTimeSince _sinceResolved;

	public override void Tick()
	{
		var inputs = (Key, Action, GlyphSize, Input.UsingController);
		if ( inputs == _resolvedFor && _sinceResolved < 0.5f ) return;
		_resolvedFor = inputs;
		_sinceResolved = 0;

		if ( !string.IsNullOrEmpty( Key ) )
		{
			var keyGlyph = Input.Keyboard.GetGlyph( Key, GlyphSize, false );
			if ( !keyGlyph.IsValid() || keyGlyph == _lastGlyph ) return;
			_lastGlyph = keyGlyph;
			_lastOrigin = null;

			Style.SetBackgroundImage( keyGlyph );
			Style.AspectRatio = (float)keyGlyph.Width / keyGlyph.Height;
			return;
		}

		if ( string.IsNullOrEmpty( Action ) ) return;

		var glyph = Input.GetGlyph( Action, GlyphSize, false );

		if ( glyph.IsValid() )
		{
			if ( glyph == _lastGlyph ) return;
			_lastGlyph = glyph;
			_lastOrigin = null;

			Style.SetBackgroundImage( glyph );
			Style.AspectRatio = (float)glyph.Width / glyph.Height;
		}
		else
		{
			var origin = Input.GetButtonOrigin( Action ) ?? Action;
			if ( origin == _lastOrigin ) return;
			_lastOrigin = origin;
			_lastGlyph = null;

			Style.SetBackgroundImage( string.Empty );
		}
	}
}
