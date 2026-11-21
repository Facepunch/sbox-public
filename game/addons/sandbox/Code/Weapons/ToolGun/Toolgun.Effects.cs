public partial class Toolgun : ScreenWeapon
{
	/// <summary>
	/// Flips the viewmodel's selector when the player changes tools.
	/// </summary>
	public void SwitchToolMode()
	{
		var parameters = ViewModel?.GetComponent<ViewModel>()?.Renderer?.Animation.Parameters;
		if ( parameters?.TryGetBool( "selector_down", out var down ) == true )
			parameters.Set( "selector_down", !down );
	}
}
