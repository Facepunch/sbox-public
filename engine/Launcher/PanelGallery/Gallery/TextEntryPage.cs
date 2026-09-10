namespace Sandbox.PanelGallery;

/// <summary>
/// Text entries - the text and the caret should sit centered in the box.
/// </summary>
public class TextEntryPage : GalleryPage
{
	readonly Sandbox.UI.Label _output;

	public TextEntryPage() : base( "Text Entry", "Sandbox.UI.TextEntry. Text, placeholder and caret all vertically centered; the icon sits on the right." )
	{
		var row = Case( "Placeholder" );
		row.AddChild( new Sandbox.UI.TextEntry { Placeholder = "Search projects" } );

		row = Case( "With text" );
		var filled = new Sandbox.UI.TextEntry();
		filled.Text = "The quick brown fox";
		row.AddChild( filled );

		row = Case( "With an icon" );
		row.AddChild( new Sandbox.UI.TextEntry { Placeholder = "Search", Icon = "search" } );

		// Longer than the box, so it has to scroll sideways to follow the caret. Home and End,
		// ctrl+arrows and click-drag past the edge all get exercised here
		row = Case( "Scrolls sideways" );
		var overflowing = new Sandbox.UI.TextEntry();
		overflowing.Text = "The quick brown fox jumps over the lazy dog and keeps on running well past the end of the box";
		row.AddChild( overflowing );

		// Same text with the caret parked at the end, so the scrolled state is visible without
		// having to click into it
		var scrolled = new Sandbox.UI.TextEntry();
		scrolled.Text = "The quick brown fox jumps over the lazy dog and keeps on running well past the end of the box";
		scrolled.CaretPosition = scrolled.TextLength;
		row.AddChild( scrolled );

		// Wraps, and scrolls vertically once there's more text than height
		row = Case( "Multiline" );
		var multiline = new Sandbox.UI.TextEntry { Multiline = true, Placeholder = "Several lines of text" };
		multiline.Text = "The quick brown fox\njumps over the lazy dog.\n\nEnter makes a new line here instead of submitting.";
		multiline.Style.Height = 120;
		row.AddChild( multiline );

		// More text than fits, with the caret at the end - so it starts scrolled to the bottom
		var tallText = new Sandbox.UI.TextEntry { Multiline = true };
		tallText.Text = "one\ntwo\nthree\nfour\nfive\nsix\nseven\neight\nnine\nten";
		tallText.Style.Height = 120;
		tallText.CaretPosition = tallText.TextLength;
		row.AddChild( tallText );

		row = Case( "Numeric" );
		row.AddChild( new Sandbox.UI.TextEntry { Numeric = true, MinValue = -10, MaxValue = 10, Placeholder = "-10 to 10" } );

		row = Case( "Max length" );
		row.AddChild( new Sandbox.UI.TextEntry { MaxLength = 8, Placeholder = "8 characters" } );

		// Typing :fire: turns into the emoji - and the caret should land after it, not inside it
		row = Case( "Emoji" );
		var emoji = new Sandbox.UI.TextEntry { AllowEmojiReplace = true, Placeholder = "Type :fire:" };
		emoji.Text = "Word 👍👍 jumps skip over these";
		row.AddChild( emoji );

		// Read only takes focus and selects, it just can't be typed into
		row = Case( "Read only" );
		var readOnly = new Sandbox.UI.TextEntry { ReadOnly = true };
		readOnly.Text = "Select and copy me, but don't type";
		row.AddChild( readOnly );

		row = Case( "Disabled" );
		var disabled = new Sandbox.UI.TextEntry { Disabled = true };
		disabled.Text = "Can't type in here";
		row.AddChild( disabled );

		row = Case( "Events" );
		var typed = new Sandbox.UI.TextEntry { Placeholder = "Type here" };
		typed.OnTextEdited = x => _output.Text = $"edited: {x}";
		row.AddChild( typed );

		_output = Output();
	}
}
