using System.Linq;
using System.Runtime.InteropServices;

namespace Sandbox.Rendering.Bridge;

/// <summary>
/// What a <see cref="SceneMirror"/> holds besides its objects: meshes made from models, their draw-state variants, shared
/// lightmap and attribute sets, and deleted objects kept to the end of their frame. Counted for watching that none of it
/// grows for the life of a session (<see cref="SceneBridge.CachesFor"/>).
/// </summary>
internal readonly record struct MirrorCacheCounts( int Objects, int Meshes, int FailedMeshes, int Variants, int IdleMeshes, int Lightmaps, int LightmapKeys,
	int Attributed, int Sharing, int Shareable, int Deleted, long Built, long Evicted, long Reloaded )
{
	public override string ToString()
		=> $"{Objects} objects, {Meshes} meshes ({FailedMeshes} failed), {Variants} variants, {IdleMeshes} idle; lightmaps {Lightmaps}/{LightmapKeys}; attributes {Attributed} polled, {Sharing} sharing, {Shareable} shareable; {Deleted} deleted; built {Built}, evicted {Evicted}, reloaded {Reloaded}";
}

internal sealed partial class SceneMirror
{
	readonly Dictionary<Model, RenderMesh> meshes = new();
	// Shared draw-state variants; hash collisions are checked by value.
	readonly Dictionary<(RenderMesh Mesh, int Hash), List<(RenderMesh.DrawState[] States, RenderMesh Mesh)>> variants = new();
	readonly List<RenderMesh.DrawState> drawStates = new();
	long built;

	/// <summary>
	/// What the mirror holds in its caches now.
	/// </summary>
	public MirrorCacheCounts Caches
	{
		get
		{
			int deleted;
			lock ( gate ) deleted = deletedFrames.Count;

			return new MirrorCacheCounts( objects.Count, meshes.Count( x => x.Value is not null ), meshes.Count( x => x.Value is null ), variants.Sum( x => x.Value.Count ), 0,
				lightmaps.Count, lightmapKeys.Count, attributed.Count, sharing.Count, shareable.Count, deleted + lingering.Count, built, 0, 0 );
		}
	}

	/// <summary>
	/// Cache mesh variants by effective draw state so matching objects still instance.
	/// </summary>
	RenderMesh VariantFor( SceneObject sceneObject, RenderMesh mesh )
	{
		if ( mesh is null ) return null;

		SceneNative.ReadSceneObjectDraws( sceneObject, drawStates );
		if ( drawStates.Count == 0 ) return mesh;

		// Compare hash matches against scratch data; allocate only for new variants.
		var states = CollectionsMarshal.AsSpan( drawStates );
		var hash = new HashCode();
		foreach ( var state in states ) hash.Add( state );

		var key = (mesh, hash.ToHashCode());
		if ( !variants.TryGetValue( key, out var candidates ) ) variants[key] = candidates = new();

		foreach ( var candidate in candidates )
		{
			if ( states.SequenceEqual( candidate.States ) ) return candidate.Mesh;
		}

		var kept = states.ToArray();
		var variant = mesh.WithDraws( kept );
		candidates.Add( (kept, variant) );
		built++;
		return variant;
	}

	/// <summary>
	/// Cache model meshes and retain buffers for in-flight frames.
	/// </summary>
	RenderMesh MeshFor( Model model )
	{
		if ( model is null ) return null;
		if ( meshes.TryGetValue( model, out var mesh ) ) return mesh;

		try
		{
			mesh = Source2Mesh.FromModel( model );
		}
		catch ( InvalidOperationException )
		{
			// No render geometry
			mesh = null;
		}

		meshes[model] = mesh;
		built++;
		return mesh;
	}
}
