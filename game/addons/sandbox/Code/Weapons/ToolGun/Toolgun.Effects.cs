public partial class Toolgun : ScreenWeapon
{
	bool ping = false;
	public void SwitchToolMode()
	{
		ping = !ping;
		WeaponModel?.Renderer?.Set( "firing_mode", ping ? 1 : 0 );
	}
}
