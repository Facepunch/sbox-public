using Sandbox;

namespace Menu;

[Title( "VR Model Switcher" )]
public sealed class VRModelSwitcher : Component
{
	public enum HandSource
	{
		Left,
		Right
	}

	[Property] public HandSource Source { get; set; }

	[Property] public GameObject HandTrackedObject { get; set; }
	[Property] public GameObject ControllerObject { get; set; }

	protected override void OnUpdate()
	{
		var hand = Source == HandSource.Left ? Input.VR.LeftHand : Input.VR.RightHand;
		HandTrackedObject.Enabled = hand.IsHandTracked;
		ControllerObject.Enabled = !hand.IsHandTracked;
	}
}
