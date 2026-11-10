namespace Sandbox.UI;

/// <summary>
/// A text entry that takes a colour in any form <see cref="Color.Parse(string)"/> knows - hex, rgb(),
/// a name, "r, g, b, a" floats - with an optional "* N" brightness on the end. Text that doesn't
/// parse goes red rather than applying.
/// </summary>
[StyleSheet.Inline( "colortextentry", Styles )]
internal class ColorTextEntry : TextEntry
{
	const string Styles = """
		.colortextentry.invalid
		{
			color: #ff6b6b;
		}
		""";

	/// <summary>
	/// Called with the text each time it parses as a colour while it's typed.
	/// </summary>
	public Action<string> ColorEntered { get; set; }

	/// <summary>
	/// Called when editing ends, so the owner can put the tidy form of the colour back.
	/// </summary>
	public Action Blurred { get; set; }

	public ColorTextEntry()
	{
		AddClass( "colortextentry" );
	}

	public override void OnValueChanged()
	{
		base.OnValueChanged();

		if ( Color.TryParse( Text, out _ ) )
		{
			SetClass( "invalid", false );
			ColorEntered?.Invoke( Text );
		}
		else
		{
			SetClass( "invalid", true );
		}
	}

	protected override void OnBlur( PanelEvent e )
	{
		base.OnBlur( e );

		SetClass( "invalid", false );
		Blurred?.Invoke();
	}
}
