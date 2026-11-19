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

	/// <summary>
	/// The cube an environment probe reflects: a realtime probe's capture once something has captured it (null until then, and
	/// the probe's settings are written again when it has), or its texture's, as <see cref="TextureWhenRead"/>.
	/// </summary>
	Borealis.GpuTexture ProbeCube( SceneCubemap cubemap );

	/// <summary>
	/// Write a model's draws, flags, placement and attributes into its mesh object: the model's mesh and variants are the
	/// world's, made and held by it.
	/// </summary>
	void WriteMesh( SceneObject sceneObject, Borealis.MeshObject mesh, SceneObjectChange change );
}
