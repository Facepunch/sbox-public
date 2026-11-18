namespace Sandbox;

public partial class SceneObject
{
	/// <summary>
	/// Whether it makes and writes its own render object (<see cref="MakeRenderObject"/>, <see cref="WriteRender"/>): its world's
	/// renderer adds it and removes it, and a change made on the main thread outside a render of a world holding it goes straight
	/// into it. Other kinds are made and written by their world's renderer.
	/// </summary>
	internal virtual bool OwnsRenderObject => false;

	/// <summary>
	/// Make the render object it draws as (<see cref="OwnsRenderObject"/>). Its world's renderer adds it to its render world and
	/// writes everything into it.
	/// </summary>
	internal virtual Borealis.RenderObject MakeRenderObject() => null;

	/// <summary>
	/// Write what changed into its render object (<see cref="OwnsRenderObject"/>), with <paramref name="render"/> for the
	/// device resources it draws with.
	/// </summary>
	internal virtual void WriteRender( Borealis.RenderObject obj, Rendering.SceneObjectChange change, Rendering.ISceneWorldRender render )
	{
		if ( (change & Rendering.SceneObjectChange.Tags) != 0 ) obj.Tags = RenderTags();
	}

	/// <summary>
	/// Its tags as string tokens, as a render object filters by them (<see cref="TagTokens"/>).
	/// </summary>
	internal uint[] RenderTags()
	{
		var tokens = TagTokens;
		return tokens.Count == 0 ? [] : [.. tokens];
	}

	/// <summary>
	/// What waits for its world's renderer even for an object that writes its own render object: coming and going, and its
	/// attributes being made.
	/// </summary>
	const Rendering.SceneObjectChange RendererChanges = Rendering.SceneObjectChange.Added | Rendering.SceneObjectChange.Removed
		| Rendering.SceneObjectChange.Deleted | Rendering.SceneObjectChange.Attributes;

	/// <summary>
	/// Write a change straight into its own render object if it can: one that owns it, made already, on the main thread, outside
	/// a render of a world holding it. Otherwise its world's renderer takes the change, and writes it before the next render.
	/// </summary>
	bool WroteRender( Rendering.SceneObjectChange change )
	{
		if ( (change & RendererChanges) != 0 || !OwnsRenderObject || RenderObject is not { World: not null } obj ) return false;
		if ( World?.Render is not { } render || obj.InRender || !ThreadSafe.IsMainThread ) return false;

		using ( Borealis.Zones.ObjectWrite.Start() ) WriteRender( obj, change, render );
		return true;
	}
}
