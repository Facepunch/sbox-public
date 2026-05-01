namespace MenuProject.Modals;

public class BaseModal : Panel
{
	internal Action<bool> OnClosed;

	public BaseModal()
	{
		AddClass( "modal" );

		var bg = AddChild<Panel>( "modal-background" );
		bg.AddEventListener( "onmousedown", () => CloseModal( false ) );

		AcceptsFocus = true;
	}

	protected override void OnVisibilityChanged()
	{
		if ( IsVisible )
		{
			Focus();
		}
	}

	protected override void OnEscape( PanelEvent e )
	{
		CloseModal( false );
		e.StopPropagation();
	}

	public void CloseModal( bool success )
	{
		OnClosed?.Invoke( success );
	}
}
