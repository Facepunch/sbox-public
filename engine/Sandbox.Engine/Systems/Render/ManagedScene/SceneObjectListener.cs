namespace Sandbox.Rendering;

/// <summary>
/// What changed on a <see cref="SceneObject"/>, for an <see cref="ISceneObjectListener"/>.
/// </summary>
[Flags]
internal enum SceneObjectChange
{
	None = 0,

	/// <summary>
	/// Created in the world. Its subclass may still be setting itself up, so read it later, not in the call.
	/// </summary>
	Added = 1 << 0,

	/// <summary>
	/// Destroyed. Its native side is going: don't read it.
	/// </summary>
	Removed = 1 << 1,

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

/// <summary>
/// Told about every change to a <see cref="SceneWorld"/>'s scene objects, as the change is made - so a renderer
/// that isn't native (Sandbox.SceneRenderer, with <c>r_managed_scene</c>) can keep its own objects in step without
/// polling. A world has at most one, in <see cref="SceneWorld.ChangeListener"/>.
///
/// Changes come from any thread - animation updates run in parallel - so an implementation should only record
/// them, and act on them later on the main thread.
/// </summary>
internal interface ISceneObjectListener
{
	void OnChanged( SceneObject sceneObject, SceneObjectChange change );
}
