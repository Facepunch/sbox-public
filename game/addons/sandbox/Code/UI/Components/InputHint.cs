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

	public override void Tick()
	{
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
