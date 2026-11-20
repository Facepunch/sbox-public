using Sandbox.UI;
using Sandbox.UI.Construct;

namespace Sandbox;

public class UIHintPanel : Panel
{
	public UIHintPanel( string title, string description )
	{
		if ( !string.IsNullOrWhiteSpace( title ) )
			Add.Label( title, "name" );

		var text = Add.Label( "", "text" );

		text.IsRich = true;
		text.Tokenize = false;
		text.Text = LocalizedText.Resolve( description );
	}
}
