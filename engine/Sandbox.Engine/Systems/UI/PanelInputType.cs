namespace Sandbox.UI
{
	/// <summary>
	/// Determines whether a focused panel receives input as UI events or redirects it to the game's input system.
	/// </summary>
	[Expose]
	public enum PanelInputType
	{
		/// <summary>
		/// Regular expected behaviour.
		/// </summary>
		UI,

		/// <summary>
		/// Redirect to the game's Input system
		/// </summary>
		Game
	}
}
