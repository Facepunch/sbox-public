namespace Sandbox.UI.Mounts;

public class MountButton : PopupButton
{
	[Property]
	public Popup.PositionMode PositionMode { get; set; } = Popup.PositionMode.BelowRight;

	public MountButton()
	{
		Icon = "extension";
	}

	public override void Open()
	{
		if ( Popup.IsValid() )
		{
			Popup.Delete();
			Popup = null;
			return;
		}

		Popup.CloseAll();

		Popup = new MountList();
		Popup.SetPositioning( this, PositionMode, 12.0f );
	}
}
