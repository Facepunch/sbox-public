namespace Sandbox;

public static partial class Input
{
	/// <summary>
	/// Identifies which player slot to scope input for.
	/// Use with <see cref="Input.PlayerScope(PlayerIndex)"/> to read input from a specific device.
	/// </summary>
	[Expose]
	public enum PlayerIndex
	{
		/// <summary>
		/// Keyboard and mouse input only.
		/// </summary>
		KeyboardAndMouse = 0,

		/// <summary>
		/// First controller.
		/// </summary>
		Controller1 = 1,

		/// <summary>
		/// Second controller.
		/// </summary>
		Controller2 = 2,

		/// <summary>
		/// Third controller.
		/// </summary>
		Controller3 = 3,

		/// <summary>
		/// Fourth controller.
		/// </summary>
		Controller4 = 4
	}
}
