namespace MenuProject.Modals;

public class BaseModal : Panel
{
	internal Action<bool> OnClosed;

	public BaseModal()
	{
		AddClass( "modal" );

		var bg = AddChild<Panel>( "modal-background" );
		bg.AddEventListener( "onmousedown", () => CloseModal( false ) );
	}

	public void CloseModal( bool success )
	{
		OnClosed?.Invoke( success );
	}
}
