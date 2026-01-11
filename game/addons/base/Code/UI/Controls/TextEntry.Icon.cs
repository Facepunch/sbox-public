namespace Sandbox.UI;

public partial class TextEntry
{
	/// <summary>
	/// The <see cref="UI.IconPanel"/> that displays <see cref="Icon"/>
	/// </summary>
	public IconPanel IconPanel { get; protected set; }

	/// <summary>
	/// If set, will display a <a href="https://fonts.google.com/icons">material icon</a> at the end of the text entry.
	/// </summary>
	[Property]
	public string Icon
	{
		get => IconPanel?.Text;
		set
		{
			if ( string.IsNullOrEmpty( value ) )
			{
				IconPanel?.Delete( true );
				IconPanel = default;
			}
			else
			{
				IconPanel ??= AddChild<IconPanel>( value );
				IconPanel.Text = value;
			}

			SetClass( "has-icon", IconPanel.IsValid() );
		}
	}

	bool _hasClearButton;

	/// <summary>
	/// If true then Icon/IconPanel will be set to a clear button that clears the text when clicked.
	/// </summary>
	public bool HasClearButton
	{
		get => _hasClearButton;

		set
		{
			if ( _hasClearButton == value ) return;
			_hasClearButton = value;

			if ( _hasClearButton )
			{
				Icon = "cancel";
				IconPanel.AddClass( "clearbutton" );
				IconPanel.AddEventListener( "onclick", () =>
				{
					Text = string.Empty;
					OnValueChanged();
				} );
			}
			else
			{
				Icon = null;
			}
		}
	}
}
