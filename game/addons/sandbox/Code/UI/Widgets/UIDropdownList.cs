using Sandbox.UI;
using Sandbox.UI.Construct;

namespace Sandbox;

/// <summary>
/// The list under an open <see cref="UIDropdown"/>. It's a popup, so it lives above the menu and closes by itself when something else is pressed.
/// </summary>
public class UIDropdownList : Popup
{
	public UIDropdownList( Panel source, List<UIDropdown.Item> items, object selected, bool tokenize, Action<UIDropdown.Item> onPick )
		: base( source, PositionMode.BelowStretch, 0f )
	{
		CloseWhenParentIsHidden = true;

		for ( int i = 0; i < items.Count; i++ )
		{
			var item = items[i];

			var row = Add.Panel( "option" );
			row.SetClass( "alt", i % 2 == 1 );
			row.SetClass( "active", Equals( item.Value, selected ) );

			var label = row.Add.Label( "", "label" );
			label.Tokenize = tokenize;
			label.Text = item.Label;

			if ( item.Font is not null )
				label.Style.FontFamily = item.Font;

			row.AddEventListener( "onclick", () =>
			{
				onPick?.Invoke( item );
				Delete();
			} );
		}
	}

	// keeps a popup the dropdown sits in, like the resource picker, open while the list is used
	protected override BasePopup ParentPopup => PopupSource?.AncestorsAndSelf.OfType<BasePopup>().FirstOrDefault();
}
