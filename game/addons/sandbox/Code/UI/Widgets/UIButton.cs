using Sandbox.UI;

namespace Sandbox;

/// <summary>
/// UI button. A <see cref="UISheet"/> uses it for methods marked with [Button], and [EnabledIf] decides when they can be pressed.
/// </summary>
public class UIButton : Button
{
	SerializedProperty _method;

	/// <summary>
	/// The method this runs when pressed, set by a sheet. It takes the title and icon from [Button], or the name of the method.
	/// </summary>
	public SerializedProperty Method
	{
		get => _method;
		set
		{
			_method = value;
			if ( _method is null ) return;

			_method.TryGetAttribute<ButtonAttribute>( out var button );

			Text = string.IsNullOrWhiteSpace( button?.Title ) ? _method.DisplayName : button.Title;
			Icon = button?.Icon;
		}
	}

	/// <summary>
	/// Moves the text into the tooltip and leaves only the icon, square like any icon-only button.
	/// Clearing the text alone keeps the empty text column, which pushes the icon off centre.
	/// </summary>
	public void MakeIconOnly()
	{
		Tooltip = Text;
		Text = null;
		RightColumn.Style.Display = DisplayMode.None;
	}

	public override void Tick()
	{
		base.Tick();

		var hasText = TextLabel.IsValid() && TextLabel.Style.Display == DisplayMode.Flex;
		SetClass( "icon-only", !hasText && !string.IsNullOrEmpty( Icon ) );

		if ( _method is UIActionProperty { Enabled: { } enabled } )
			Disabled = !enabled();
		else if ( _method is not null && _method.TryGetAttribute<EnabledIfAttribute>( out var condition ) )
			Disabled = !condition.Test( _method.Parent );
	}

	protected override void OnClick( MousePanelEvent e )
	{
		base.OnClick( e );

		if ( Disabled ) return;

		_method?.Invoke();
	}
}
