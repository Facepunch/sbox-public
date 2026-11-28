namespace Sandbox.Mounting;

[Expose]
public interface IMountEvents
{
	void OnMountEnabled( BaseGameMount source ) { }
	void OnMountDisabled( BaseGameMount source ) { }
}
