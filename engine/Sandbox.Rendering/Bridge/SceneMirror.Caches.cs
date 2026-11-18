using System.Linq;
using System.Runtime.InteropServices;

namespace Sandbox.Rendering.Bridge;

/// <summary>
/// What a <see cref="SceneMirror"/> holds besides its objects: meshes made from models, their draw-state variants, shared
/// attribute sets, and deleted objects kept to the end of their frame. Counted for watching that none of it grows for the
/// life of a session (<see cref="SceneBridge.CachesFor"/>).
/// </summary>
internal readonly record struct MirrorCacheCounts( int Objects, int Meshes, int FailedMeshes, int Variants, int IdleMeshes,
	int Attributed, int Sharing, int Shareable, int Deleted, long Built, long Evicted, long Reloaded )
{
	public override string ToString()
		=> $"{Objects} objects, {Meshes} meshes ({FailedMeshes} failed), {Variants} variants, {IdleMeshes} idle; attributes {Attributed} tracked, {Sharing} sharing, {Shareable} shareable; {Deleted} deleted; built {Built}, evicted {Evicted}, reloaded {Reloaded}";
}

/// <summary>
/// The mirror's mesh cache. Each model's <see cref="RenderMesh"/> is made once, and each effective draw state of it (body
/// groups, material group, overrides) once more as a variant, shared by every object in that state so they instance.
/// Objects hold what they draw (<see cref="Hold"/>, <see cref="Release"/>); what none holds is idle, and dropped once it has
/// been idle for <see cref="IdleFrames"/>. A model reloaded since its mesh was made drops the mesh, and the objects drawing
/// it make it again (<see cref="CheckReloads"/>).
/// </summary>
internal sealed partial class SceneMirror
{
	/// <summary>
	/// Frames a mesh nothing draws stays cached, so objects spawned and destroyed over and over (shells, gibs) keep reusing it.
	/// </summary>
	const ulong IdleFrames = 300;

	/// <summary>
	/// Frames between looks at what's idle, while something is.
	/// </summary>
	const ulong SweepFrames = 60;

	abstract class CachedMesh
	{
		/// <summary>
		/// The mesh, or for a model, null when it has no render geometry.
		/// </summary>
		public RenderMesh Mesh;

		/// <summary>
		/// The objects drawing it, and for a model its variants.
		/// </summary>
		public int Users;

		public ulong IdleSince;
		public bool Queued, Dropped;
	}

	sealed class CachedModel : CachedMesh
	{
		public Model Model;
		public int Revision;

		// Variants by a hash of their draw states; hash collisions are checked by value
		public readonly Dictionary<int, List<CachedVariant>> Variants = new();
	}

	sealed class CachedVariant : CachedMesh
	{
		public CachedModel Model;
		public int Hash;
		public RenderMesh.DrawState[] States;
	}

	readonly Dictionary<Model, CachedModel> models = new();

	// Every mesh an object can hold, by mesh: a model's own and its variants that differ from it
	readonly Dictionary<RenderMesh, CachedMesh> held = new( ReferenceEqualityComparer.Instance );
	readonly List<CachedMesh> idle = new();
	readonly List<RenderMesh.DrawState> drawStates = new();
	int variantCount;
	ulong nextSweep;

	int seenReloads = Resource.Reloads;
	long built, evicted, reloaded;

	/// <summary>
	/// What the mirror holds in its caches now.
	/// </summary>
	public MirrorCacheCounts Caches
	{
		get
		{
			int deleted;
			lock ( gate ) deleted = deletedFrames.Count;

			var failed = models.Count( x => x.Value.Mesh is null );
			var unused = idle.Count( x => x.Users == 0 && !x.Dropped );
			return new MirrorCacheCounts( objects.Count, models.Count - failed, failed, variantCount, unused,
				attributed.Count, sharing.Count, shareable.Count, deleted + lingering.Count, built, evicted, reloaded );
		}
	}

	/// <summary>
	/// A model's cached mesh, made the first time it's asked for. One without render geometry is cached as such, and tried
	/// again once the model reloads or nothing has asked for it for a while.
	/// </summary>
	CachedModel ModelFor( Model model )
	{
		if ( model is null ) return null;
		if ( models.TryGetValue( model, out var cached ) ) return cached;

		RenderMesh mesh;
		try
		{
			mesh = MirrorContent.Mesh( model, out var pending, pin: false );

			// Still loading on Borealis.Gpu: nothing is cached, and what draws it is asked again once it's read (CheckLoaded)
			if ( pending )
			{
				loading.Add( model );
				return null;
			}

			if ( mesh is null ) throw new InvalidOperationException( "No compiled model" );
		}
		catch ( InvalidOperationException )
		{
			// No render geometry
			mesh = null;
		}

		cached = new CachedModel { Model = model, Mesh = mesh, Revision = model.Revision };
		models[model] = cached;
		if ( mesh is not null )
		{
			held[mesh] = cached;

			// Held while it's cached here: a model no world caches is freed after a while (BackendDevice.ReleaseModel)
			MirrorContent.Gpu?.HoldModel( mesh );
		}
		built++;

		// Idle until something holds it
		Idle( cached );
		return cached;
	}

	/// <summary>
	/// The mesh an object draws: its model's, with the object's effective draw states applied, shared with every object in the
	/// same states so they still instance.
	/// </summary>
	RenderMesh VariantFor( SceneObject sceneObject, CachedModel model )
	{
		if ( model?.Mesh is not { } mesh ) return null;

		SceneNative.ReadSceneObjectDraws( sceneObject, drawStates, MaterialResolver );
		if ( drawStates.Count == 0 ) return mesh;

		// Compare hash matches against scratch data; allocate only for new variants.
		var states = CollectionsMarshal.AsSpan( drawStates );
		var hasher = new HashCode();
		foreach ( var state in states ) hasher.Add( state );
		var hash = hasher.ToHashCode();

		if ( !model.Variants.TryGetValue( hash, out var candidates ) ) model.Variants[hash] = candidates = new();

		foreach ( var candidate in candidates )
		{
			if ( states.SequenceEqual( candidate.States ) ) return candidate.Mesh;
		}

		var kept = states.ToArray();
		var variant = new CachedVariant { Model = model, Hash = hash, States = kept, Mesh = mesh.WithDraws( kept ) };
		candidates.Add( variant );
		variantCount++;

		// States that change nothing draw the model's own mesh, and go with it
		if ( variant.Mesh == mesh ) return mesh;

		held[variant.Mesh] = variant;
		model.Users++;
		built++;
		Idle( variant );
		return variant.Mesh;
	}

	/// <summary>
	/// An object starts drawing a mesh.
	/// </summary>
	void Hold( RenderMesh mesh )
	{
		if ( mesh is not null && held.TryGetValue( mesh, out var cached ) ) cached.Users++;
	}

	/// <summary>
	/// An object stops drawing a mesh. A mesh dropped since (a reload) isn't held any more, and is let go of without counting.
	/// </summary>
	void Release( RenderMesh mesh )
	{
		if ( mesh is not null && held.TryGetValue( mesh, out var cached ) && --cached.Users == 0 ) Idle( cached );
	}

	void Idle( CachedMesh cached )
	{
		cached.IdleSince = Application.FrameCount;
		if ( cached.Queued ) return;

		cached.Queued = true;
		idle.Add( cached );
	}

	/// <summary>
	/// Drop what nothing has drawn for <see cref="IdleFrames"/>. Looks only while something is idle, every
	/// <see cref="SweepFrames"/>, so a world whose objects come and go no more than that costs nothing. Dropping only lets go
	/// of the mirror's references: a frame still recording a mesh holds it in its draw lists, and the model's GPU buffers are
	/// the native model's, which the device destroys once frames using them are done.
	/// </summary>
	void SweepCaches( ulong frame )
	{
		if ( idle.Count == 0 || frame < nextSweep ) return;
		nextSweep = frame + SweepFrames;

		// A variant dropped idles its model at the end of the list, for a later sweep
		var kept = 0;
		for ( int i = 0; i < idle.Count; i++ )
		{
			var cached = idle[i];
			if ( cached.Users > 0 || cached.Dropped )
			{
				cached.Queued = false;
				continue;
			}

			if ( frame - cached.IdleSince < IdleFrames )
			{
				idle[kept++] = cached;
				continue;
			}

			cached.Queued = false;
			Drop( cached );
			evicted++;
		}

		idle.RemoveRange( kept, idle.Count - kept );
	}

	/// <summary>
	/// Take a mesh out of the cache, and a model's variants with it.
	/// </summary>
	void Drop( CachedMesh cached )
	{
		cached.Dropped = true;

		switch ( cached )
		{
			case CachedVariant variant:
				held.Remove( variant.Mesh );
				var candidates = variant.Model.Variants[variant.Hash];
				candidates.Remove( variant );
				if ( candidates.Count == 0 ) variant.Model.Variants.Remove( variant.Hash );
				variantCount--;
				if ( !variant.Model.Dropped && --variant.Model.Users == 0 ) Idle( variant.Model );
				break;

			case CachedModel model:
				if ( model.Mesh is not null )
				{
					held.Remove( model.Mesh );
					MirrorContent.Gpu?.ReleaseModel( model.Mesh );
				}

				if ( models.GetValueOrDefault( model.Model ) == model ) models.Remove( model.Model );

				foreach ( var (_, list) in model.Variants )
				{
					foreach ( var variant in list )
					{
						variant.Dropped = true;
						variantCount--;
						if ( variant.Mesh == model.Mesh ) continue;
						held.Remove( variant.Mesh );
					}
				}

				model.Variants.Clear();
				break;
		}
	}

	// Reload scratch: meshes dropped, models without geometry dropped, and aggregates to make again
	readonly List<CachedModel> stale = new();
	readonly HashSet<RenderMesh> staleMeshes = new( ReferenceEqualityComparer.Instance );
	readonly HashSet<Model> staleFailed = new();
	readonly List<SceneObject> remake = new();

	// Models loading asynchronously on Borealis.Gpu (MirrorContent.Mesh), and those whose files are now read
	readonly HashSet<Model> loading = new();
	readonly HashSet<Model> loaded = new();

	// What waits for each loading model: objects drawing it and aggregates made of it (WaitFor). Looking through every
	// object each time a model loaded cost a map's first seconds ~80 ms of native calls
	readonly Dictionary<Model, HashSet<SceneObject>> modelWaiters = new();

	/// <summary>Note that <paramref name="sceneObject"/> waits for <paramref name="model"/>, still loading: <see cref="CheckLoaded"/> applies it again.</summary>
	void WaitFor( Model model, SceneObject sceneObject )
	{
		if ( !modelWaiters.TryGetValue( model, out var waiters ) ) modelWaiters[model] = waiters = new();
		waiters.Add( sceneObject );
	}

	/// <summary>Models loading asynchronously, which what draws them waits for.</summary>
	public int ModelsLoading => loading.Count;

	// Textures loading asynchronously on Borealis.Gpu (TextureFor), with the object to apply again once each is read
	readonly List<(string Path, SceneObject Object, SceneObjectChange Change)> textureWaits = new();

	/// <summary>
	/// A texture an object draws with; on Borealis.Gpu loading asynchronously, null while it loads, and the object is applied
	/// with <paramref name="change"/> again once it's read.
	/// </summary>
	GpuTexture TextureFor( Texture texture, SceneObject sceneObject, SceneObjectChange change )
	{
		if ( MirrorContent.Gpu is not { AsyncModels: true } gpu || string.IsNullOrEmpty( texture?.ResourcePath ) ) return MirrorContent.Texture( texture );

		var loaded = gpu.LoadTextureAsync( texture.ResourcePath, out var pending );
		if ( pending ) textureWaits.Add( (texture.ResourcePath, sceneObject, change) );
		return loaded;
	}

	/// <summary>Whether an object waits for a texture (<see cref="TextureFor"/>).</summary>
	bool TexturesWaiting( SceneObject sceneObject )
	{
		foreach ( var wait in textureWaits )
		{
			if ( wait.Object == sceneObject ) return true;
		}

		return false;
	}

	/// <summary>
	/// Queue what waits for a model that has finished loading in the background: an object drawing it is asked for its model
	/// again, an aggregate made again. One whose model still can't be made this frame waits again.
	/// </summary>
	void CheckLoaded()
	{
		if ( MirrorContent.Gpu is not { } gpu ) return;

		// Textures first: an object waiting on one is applied again once it's read, and asks for it again then
		for ( int i = textureWaits.Count - 1; i >= 0; i-- )
		{
			var (path, sceneObject, change) = textureWaits[i];
			if ( !gpu.TextureRead( path ) ) continue;
			textureWaits.RemoveAt( i );
			if ( sceneObject.IsValid() ) OnChanged( sceneObject, change );
		}

		if ( loading.Count == 0 ) return;

		foreach ( var model in loading )
		{
			if ( !model.IsValid() || MirrorContent.MeshRead( model, gpu ) ) loaded.Add( model );
		}

		if ( loaded.Count == 0 ) return;
		loading.ExceptWith( loaded );

		foreach ( var model in loaded )
		{
			if ( !modelWaiters.Remove( model, out var waiters ) ) continue;
			foreach ( var sceneObject in waiters )
			{
				if ( !sceneObject.IsValid() || !objects.TryGetValue( sceneObject, out var obj ) ) continue;
				if ( obj is MeshObject { Mesh: null } ) OnChanged( sceneObject, SceneObjectChange.Model );
				else if ( obj is null && (sceneObject is SceneAggregateObject || sceneObject.GetType() == typeof( SceneObject )) ) remake.Add( sceneObject );
			}
		}

		foreach ( var sceneObject in remake )
		{
			Remove( sceneObject );
			OnChanged( sceneObject, SceneObjectChange.Added );
		}

		remake.Clear();
		loaded.Clear();
	}

	/// <summary>
	/// After a resource reload (<see cref="Resource.Reloads"/>, editor hot reload or a model compiled again), drop every model
	/// mesh whose model or whose draws' materials reloaded since it was made, with its variants, and queue the objects drawing
	/// them, so this sync makes them again. A mesh holds its materials' blend flags and sort keys, so a material reload counts.
	/// </summary>
	void CheckReloads()
	{
		var reloads = Resource.Reloads;
		if ( reloads == seenReloads ) return;
		seenReloads = reloads;

		foreach ( var (_, cached) in models )
		{
			if ( IsStale( cached ) ) stale.Add( cached );
		}

		if ( stale.Count == 0 ) return;

		foreach ( var cached in stale )
		{
			if ( cached.Mesh is null ) staleFailed.Add( cached.Model );
			else staleMeshes.Add( cached.Mesh );

			foreach ( var (_, list) in cached.Variants )
			{
				foreach ( var variant in list ) staleMeshes.Add( variant.Mesh );
			}

			Drop( cached );
			reloaded++;
		}

		foreach ( var (sceneObject, obj) in objects )
		{
			switch ( obj )
			{
				case MeshObject mesh when mesh.Mesh is { } drawn ? staleMeshes.Contains( drawn ) : staleFailed.Count > 0 && sceneObject.IsValid() && staleFailed.Contains( sceneObject.Model ):
					OnChanged( sceneObject, SceneObjectChange.Model );
					break;

				// An aggregate's mesh is fixed when it's made: make it again
				case AggregateObject aggregate when staleMeshes.Contains( aggregate.Mesh ):
					remake.Add( sceneObject );
					break;
			}
		}

		foreach ( var sceneObject in remake )
		{
			Remove( sceneObject );
			OnChanged( sceneObject, SceneObjectChange.Added );
		}

		stale.Clear();
		staleMeshes.Clear();
		staleFailed.Clear();
		remake.Clear();
	}

	static bool IsStale( CachedModel cached ) => cached.Revision != cached.Model.Revision;
}
