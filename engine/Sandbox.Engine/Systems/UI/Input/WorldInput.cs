namespace Sandbox.UI;

[Obsolete( "Use the WorldInput component, this class does nothing." )]
public class WorldInput
{
	public bool Enabled { get; set; }
	public Ray Ray { get; set; }
	public bool MouseLeftPressed { get; set; }
	public bool MouseRightPressed { get; set; }
	public Vector2 MouseWheel { get; set; }
	public bool UseMouseInput { get; set; }
	public Panel Hovered => null;
	public Panel Active => null;
}
