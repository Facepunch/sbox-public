namespace Sandbox.Rendering;

/// <summary>
/// What a <see cref="SceneWorld"/> draws as on Borealis (<c>-borealisgpu</c>): the render world its objects' render objects are
/// in (<see cref="SceneObject.OwnsRenderObject"/>), its lighting, and the device resources they draw with, held while the world
/// lives. A change made on the main thread outside a render goes straight into the object's render object; the rest wait for
/// the next frame the world prepares.
/// </summary>
internal interface ISceneWorldRender
{
	/// <summary>
	/// An object was made in the world, from any thread: its render object is made and written whole in the next frame the world
	/// prepares, once its constructor is done.
	/// </summary>
	void Made( SceneObject sceneObject );

	/// <summary>
	/// An object was deleted (<see cref="SceneObject.Delete"/>), from any thread: it goes now on the main thread outside a render,
	/// else in the next frame the world prepares. A light lights to the end of its frame.
	/// </summary>
	void Deleted( SceneObject sceneObject );

	/// <summary>
	/// An object's native side was destroyed: it goes, if it hasn't.
	/// </summary>
	void Destroyed( SceneObject sceneObject );

	/// <summary>
	/// A change its object couldn't write as it was made (on another thread, during a render, before its render object was made):
	/// written in the next frame the world prepares.
	/// </summary>
	void Later( SceneObject sceneObject, SceneObjectChange change );

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

	/// <summary>
	/// Write a <see cref="SceneModelInstances"/>' model, flags, tint and placement into its render object (its instances are
	/// the object's own to write).
	/// </summary>
	void WriteInstances( SceneObject sceneObject, Borealis.InstancedMeshObject instances, SceneObjectChange change );

	/// <summary>
	/// A native aggregate's model and fragments (a map's world geometry), or null if the object isn't one, or with
	/// <paramref name="waiting"/> while its model is still loading: the object is made again once it has.
	/// </summary>
	Borealis.AggregateObject MakeAggregate( SceneObject sceneObject, out bool waiting );

	/// <summary>
	/// Write a change to what lights the world rather than drawing in it (<see cref="SceneObject.LightsWorld"/>): a sun, a light
	/// probe volume. Nothing for one the world hasn't added yet: it's written whole when it is.
	/// </summary>
	void WriteLighting( SceneObject sceneObject, SceneObjectChange change );

	/// <summary>
	/// Write an aggregate's visibility, tint, flags and lightmaps.
	/// </summary>
	void WriteAggregate( SceneObject sceneObject, Borealis.AggregateObject aggregate, SceneObjectChange change );

	/// <summary>
	/// At the sync point (<see cref="RenderSync.Run"/>): write what waits for the world's next frame (objects made, gone and
	/// changed since), if anything does, so no view of the frame finds it unwritten. Nothing for a world with nothing waiting.
	/// </summary>
	void Sync() { }
}
