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
		if ( !Back() ) CloseModal( false );
		e.StopPropagation();
	}

	/// <summary>
	/// Step back within it, rather than closing it, on escape - true if it did. Nothing to step back
	/// from by default.
	/// </summary>
	public virtual bool Back() => false;

	public void CloseModal( bool success )
	{
		OnClosed?.Invoke( success );
	}
}
