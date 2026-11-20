/// <summary>
/// Identifies which toolgun input slot an action is bound to.
/// </summary>
public enum ToolInput
{
	Primary,
	Secondary,
	Reload
}

/// <summary>
/// How the input is sampled each frame.
/// </summary>
public enum InputMode
{
	/// <summary>
	/// Fires once when the button is first pressed.
	/// </summary>
	Pressed,

	/// <summary>
	/// Fires every frame while the button is held.
	/// </summary>
	Down
}

/// <summary>
/// A registered tool action. Stores the input binding, a dynamic display-name lambda,
/// the callback to invoke, and the input sampling mode.
/// </summary>
public sealed record ToolActionEntry(
	ToolInput Input,
	Func<string> Name,
	Action Callback,
	InputMode Mode = InputMode.Pressed
)
{
	/// <summary>
	/// The engine input action string for this <see cref="ToolInput"/>.
	/// </summary>
	public string InputAction => Input switch
	{
		ToolInput.Primary => "attack1",
		ToolInput.Secondary => "attack2",
		ToolInput.Reload => "reload",
		_ => null
	};
}
