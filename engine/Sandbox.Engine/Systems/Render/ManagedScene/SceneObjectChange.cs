namespace Sandbox.Rendering;

/// <summary>
/// What changed on a <see cref="SceneObject"/>: what it writes into its render object on Borealis
/// (<see cref="SceneObject.WriteRender"/>), at once or when its world next prepares a frame.
/// </summary>
[Flags]
internal enum SceneObjectChange
{
	None = 0,

	Transform = 1 << 2,
	Model = 1 << 3,
	Tint = 1 << 4,

	/// <summary>
	/// A material override, material group or mesh group (body groups).
	/// </summary>
	Material = 1 << 5,

	/// <summary>
	/// A <see cref="SceneObject.Flags"/> flag, such as casting shadows.
	/// </summary>
	Flags = 1 << 6,

	Visibility = 1 << 7,

	/// <summary>
	/// A skinned model's bones moved: an animation update, a bone merge, or a bone set by hand.
	/// </summary>
	Bones = 1 << 8,

	/// <summary>
	/// A light's or environment probe's own settings: colour, radius, shadows, cones, cubemap and so on.
	/// </summary>
	Settings = 1 << 9,

	/// <summary>
	/// Its <see cref="SceneObject.Tags"/>, which cameras filter by.
	/// </summary>
	Tags = 1 << 10,

	/// <summary>
	/// Its <see cref="SceneObject.Attributes"/> were made. Their values change in place, so this is only said once.
	/// </summary>
	Attributes = 1 << 11,

	/// <summary>
	/// Its <see cref="SceneObject.Bounds"/> were set - what a custom object that draws itself, like a sprite batch, says
	/// it covers.
	/// </summary>
	Bounds = 1 << 12,
}

