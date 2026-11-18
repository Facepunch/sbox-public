namespace Sandbox.Rendering;

/// <summary>
/// What a <see cref="SceneWorld"/> draws as on Borealis (<c>-borealisgpu</c>), for the scene objects that make and write their
/// own render objects (<see cref="SceneObject.OwnsRenderObject"/>): the render world they're in, and the device resources they
/// draw with, held while the world lives. Its world's renderer adds and removes them; a change made on the main thread outside
/// a render goes straight into the object's render object.
/// </summary>
internal interface ISceneWorldRender
{
	/// <summary>
	/// The world's render world.
	/// </summary>
	Borealis.RenderWorld World { get; }

	/// <summary>
	/// Whether the world draws on a Borealis.Gpu device.
	/// </summary>
	bool OnDevice { get; }

	/// <summary>
	/// A texture as the world's objects draw with it on the device, held by the world while it lives. Null without one.
	/// </summary>
	Borealis.GpuTexture Texture( Texture texture );

	/// <summary>
	/// A texture <paramref name="waiting"/> draws with, as <see cref="Texture"/> has it; null while the device is still reading
	/// its file, and <paramref name="change"/> is written again once it has.
	/// </summary>
	Borealis.GpuTexture TextureWhenRead( Texture texture, SceneObject waiting, SceneObjectChange change );
}
