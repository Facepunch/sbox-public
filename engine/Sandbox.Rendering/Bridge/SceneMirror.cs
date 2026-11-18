using Sandbox.Rendering;
using System.Linq;
using System.Runtime.InteropServices;

namespace Sandbox.Rendering.Bridge;

/// <summary>
/// Incremental <see cref="SceneWorld"/> mirror for Borealis (<c>-borealisgpu</c>).
/// Queues object changes from any thread and applies changed fields before rendering.
/// Object attributes are read when a view draws the object, as their values change in place without notifications.
/// </summary>
internal sealed partial class SceneMirror : ISceneObjectListener, ISceneWorldRender, IAttributeSource, IDisposable
{
	readonly SceneWorld source;

	/// <summary>
	/// The mirrored world: one per scene world. A camera drawing it as an extra world draws it in its main world's
	/// (<see cref="RenderWorld.Include"/>).
	/// </summary>
	public RenderWorld World { get; } = new();

	/// <summary>
	/// Unsupported object counts by type, for comparison reports.
	/// </summary>
	public Dictionary<string, int> Unsupported { get; } = new();

	/// <summary>
	/// What a scene object of this world draws as (<see cref="SceneObject.RenderObject"/>, its own), or null if it isn't drawn.
	/// </summary>
	internal RenderObject Find( SceneObject sceneObject ) => sceneObject?.RenderObject is { } obj && obj.World == World ? obj : null;

	// The world's objects added that draw as nothing (suns, skies, probe volumes, those not supported), and how many objects
	// were added in all
	readonly HashSet<SceneObject> undrawn = new();
	int added;

	/// <summary>
	/// Whether an object has been added: it has a render object in this world, or draws as nothing.
	/// </summary>
	bool Added( SceneObject sceneObject, out RenderObject obj )
	{
		obj = Find( sceneObject );
		return obj is not null || undrawn.Contains( sceneObject );
	}
	readonly HashSet<SceneDirectionalLight> suns = new();

	// Objects with attributes, read when a view draws them (Refresh).
	readonly Dictionary<MeshObject, RenderAttributes> attributed = new();

	// Changes as they're reported, from any thread, and the batch being applied
	readonly object gate = new();
	Dictionary<SceneObject, SceneObjectChange> pending = new();
	Dictionary<SceneObject, SceneObjectChange> applying = new();

	/// <summary>Changes reported since the last <see cref="Sync"/>: a scene that just loaded has thousands.</summary>
	public int PendingChanges
	{
		get
		{
			lock ( gate ) return pending.Count;
		}
	}

	bool sunDirty = true;

	const SceneObjectChange Everything = (SceneObjectChange)~0 & ~SceneObjectChange.Removed & ~SceneObjectChange.Deleted;

	/// <summary>
	/// Start mirroring a world: take over its change listener, and queue everything already in it.
	/// </summary>
	public SceneMirror( SceneWorld source )
	{
		this.source = source;

		lock ( source.InternalSceneObjects )
		{
			source.ChangeListener = this;
			source.Render = this;
			foreach ( var sceneObject in source.InternalSceneObjects )
				pending[sceneObject] = SceneObjectChange.Added;
		}
	}

	/// <summary>
	/// What a change waits for the next sync to apply, merged with the object's other changes: an object coming or going, and
	/// what works its draws or attributes out again. The rest of a change to an object that reads nothing from native applies
	/// at once (<see cref="WritesDirectly"/>).
	/// </summary>
	const SceneObjectChange Queued = SceneObjectChange.Added | SceneObjectChange.Removed | SceneObjectChange.Deleted | SceneObjectChange.Model
		| SceneObjectChange.Material | SceneObjectChange.Attributes;

	// While Sync applies changes: a change reported meanwhile (a texture that loaded) waits for the next
	bool syncing;

	/// <summary>
	/// Whether an object's changes go straight into its render object rather than waiting for a sync: the kinds that read
	/// nothing from native code (decals, point and spot lights, cubemaps, custom and dynamic objects, and models managed code
	/// made). Animated objects and those native code made (a map's) wait for the sync, which reads what native has.
	/// </summary>
	static bool WritesDirectly( SceneObject sceneObject ) => !sceneObject.OwnsRenderObject && sceneObject switch
	{
		SceneModel or SceneDirectionalLight or SceneLightProbe or SceneSkyBox => false,
		SceneCustomObject or SceneDynamicObject => true,
		_ => sceneObject.GetType() == typeof( SceneObject ) && sceneObject.ResolvesDraws,
	};

	/// <summary>
	/// A change, from any thread. One to an object that writes directly (<see cref="WritesDirectly"/>), made on the main thread
	/// outside a sync and outside a render of the world, goes into its render object at once; anything else is queued for the
	/// next sync.
	/// </summary>
	public void OnChanged( SceneObject sceneObject, SceneObjectChange change )
	{
		if ( (change & Queued) == 0 && !syncing && ThreadSafe.IsMainThread && WritesDirectly( sceneObject )
			&& Find( sceneObject ) is { } obj && !obj.InRender && sceneObject.IsValid() )
		{
			using ( Borealis.Zones.MirrorDirect.Start() )
				ApplyTo( sceneObject, obj, change );
			return;
		}

		lock ( gate )
		{
			// Native lights with a deleted light until its frame ends: the frame it was deleted in decides how long it's kept
			if ( (change & SceneObjectChange.Deleted) != 0 ) deletedFrames[sceneObject] = Application.FrameCount;
			pending[sceneObject] = pending.GetValueOrDefault( sceneObject ) | change;
		}
	}

	// The frame each object was deleted in, until it's removed, and those kept for the rest of it
	readonly Dictionary<SceneObject, ulong> deletedFrames = new();
	readonly List<SceneObject> lingering = new();

	/// <summary>
	/// Stop listening. The world keeps its last state, but isn't drawn in another's any more, nor another in it.
	/// </summary>
	public void Dispose()
	{
		lock ( source.InternalSceneObjects )
		{
			if ( source.ChangeListener == this ) source.ChangeListener = null;
			if ( source.Render == this ) source.Render = null;
		}

		World.ExcludeEverywhere();
		lock ( lightmapFolders )
		{
			if ( MirrorContent.Gpu is { } gpu )
			{
				foreach ( var folder in lightmapFolders ) gpu.ReleaseMapLightmaps( folder );
			}

			lightmapFolders.Clear();
		}

		if ( MirrorContent.Gpu is { } held )
		{
			lock ( heldMaterials )
			{
				foreach ( var path in heldMaterials ) held.ReleaseModelMaterial( path );
				heldMaterials.Clear();
			}

			lock ( heldTextures )
			{
				foreach ( var path in heldTextures ) held.ReleaseTexture( path );
				heldTextures.Clear();
			}
		}
		// The models this world held go a while after no world does (BackendDevice.ReleaseModel)
		if ( MirrorContent.Gpu is { } device )
		{
			foreach ( var (_, cached) in models )
			{
				if ( cached.Mesh is not null && !cached.Dropped ) device.ReleaseModel( cached.Mesh );
			}

			models.Clear();
		}
	}

	/// <summary>
	/// Whether the world has been synced at least once: a child view syncs only a mirror nothing's drawn yet.
	/// </summary>
	public bool HasSynced { get; private set; }

	/// <summary>Whether <see cref="Sync"/> defers what applying changes writes into one list (on). For comparisons.</summary>
	internal static bool DeferApplyUploads { get; set; } = true;

	/// <summary>
	/// Apply what changed since the last sync, for a camera about to render the world.
	/// </summary>
	public void Sync( SceneCamera camera, bool skyboxView = false ) => SyncWith( camera, skyboxView, lighting: true );

	/// <summary>
	/// Apply what changed to the world's objects since the last sync, for a camera drawing it as an extra world: its lighting
	/// isn't used, the main world's lights it.
	/// </summary>
	public void SyncObjects( SceneCamera camera ) => SyncWith( camera, false, lighting: false );

	void SyncWith( SceneCamera camera, bool skyboxView, bool lighting )
	{
		using var zone = Borealis.Zones.MirrorSync.Start();
		HasSynced = true;
		syncing = true;
		try
		{
			SyncChanges( camera, skyboxView, lighting );
		}
		finally
		{
			syncing = false;
		}
	}

	void SyncChanges( SceneCamera camera, bool skyboxView, bool lighting )
	{

		// A reload queues the objects drawing what it changed, and so does a model finishing loading
		using ( Borealis.Zones.MirrorLoaded.Start() )
		{
			CheckReloads();
			CheckLoaded();
			CheckCaptured();
		}

		lock ( gate )
		{
			(pending, applying) = (applying, pending);
		}

		// Deleted in an earlier frame: native has stopped drawing them
		var frame = Application.FrameCount;
		for ( int i = lingering.Count - 1; i >= 0; i-- )
		{
			if ( DeletedThisFrame( lingering[i], frame ) ) continue;
			Remove( lingering[i] );
			lingering.RemoveAt( i );
		}

		// What applying the changes writes to the device (models, textures, new capture cubes' clears) goes in one list with
		// the frame's next submission, not a submission each
		using ( Borealis.Zones.MirrorApply.Start() )
		using ( MirrorContent.Gpu is { } gpu && DeferApplyUploads ? gpu.Gpu.DeferUploads() : default )
		{
			foreach ( var (sceneObject, change) in applying )
				Apply( sceneObject, change, frame );
		}

		applying.Clear();

		SweepCaches( frame );

		// An extra world's objects are lit by the main world's sun, sky, probe volumes and fog, as native lights the view
		if ( lighting )
		{
			using ( Borealis.Zones.MirrorLighting.Start() )
				SyncLighting( camera, skyboxView );
		}
	}

	/// <summary>
	/// Whether an object was deleted (<see cref="SceneObjectChange.Deleted"/>) in this frame, which native still draws it in.
	/// </summary>
	bool DeletedThisFrame( SceneObject sceneObject, ulong frame )
	{
		lock ( gate ) return deletedFrames.TryGetValue( sceneObject, out var deleted ) && deleted == frame;
	}

	void Apply( SceneObject sceneObject, SceneObjectChange change, ulong frame )
	{
		if ( (change & (SceneObjectChange.Removed | SceneObjectChange.Deleted)) != 0 )
		{
			// A light deleted this frame: native lights with it until the frame ends, so the frames rendered meanwhile keep it
			// too - even when something native finished rendering views and destroyed it already (a bitmap render does), which
			// is when Removed comes. A deleted mesh native stops drawing at once
			if ( sceneObject is SceneLight && DeletedThisFrame( sceneObject, frame ) )
			{
				if ( !lingering.Contains( sceneObject ) ) lingering.Add( sceneObject );
				return;
			}

			Remove( sceneObject );
			return;
		}

		// Native deletion can precede queued change processing.
		if ( !sceneObject.IsValid() ) return;

		if ( !Added( sceneObject, out var obj ) )
		{
			obj = Add( sceneObject );
			change = Everything;
		}

		ApplyTo( sceneObject, obj, change );
	}

	/// <summary>A change to an object already mirrored (<paramref name="obj"/> null for one that isn't drawn as an object).</summary>
	void ApplyTo( SceneObject sceneObject, RenderObject obj, SceneObjectChange change )
	{
		// One that writes its own
		if ( sceneObject.OwnsRenderObject )
		{
			if ( obj is not null ) sceneObject.WriteRender( obj, change, this );
			return;
		}

		if ( obj is not null && (change & SceneObjectChange.Tags) != 0 )
			obj.Tags = TagsOf( sceneObject );

		switch ( obj )
		{
			case MeshObject mesh:
				ApplyMesh( sceneObject, mesh, change );
				break;

			case CustomObject custom:
				ApplyCustom( sceneObject, custom, change );
				break;

			case AggregateObject aggregate:
				ApplyAggregate( sceneObject, aggregate, change );
				break;

			case null when sceneObject is SceneDirectionalLight:
				sunDirty = true;
				break;

			case null when sceneObject is SceneLightProbe probe && lightProbes.TryGetValue( probe, out var volume ):
				ApplyLightProbe( probe, volume, change );
				break;
		}
	}

	readonly Dictionary<SceneLightProbe, Borealis.LightProbeVolume> lightProbes = new();

	/// <summary>Whether a realtime probe's cube is made when it's first captured (on), not when the probe is mirrored. For comparisons.</summary>
	internal static bool LazyCaptureCubes { get; set; } = true;

	// Realtime probes whose cube hasn't been made yet (nothing has captured them), with their engine texture
	readonly Dictionary<SceneCubemap, Texture> uncaptured = new();
	int capturesSeen;

	/// <summary>Write again the probes whose cube a capture has made since the last sync (<see cref="ProbeCube"/>).</summary>
	void CheckCaptured()
	{
		if ( uncaptured.Count == 0 || capturesSeen == MirrorContent.CaptureCubesMade ) return;
		capturesSeen = MirrorContent.CaptureCubesMade;
		foreach ( var (cubemap, texture) in uncaptured )
		{
			if ( MirrorContent.MadeCaptureCube( texture ) is not null ) OnChanged( cubemap, SceneObjectChange.Settings );
		}
	}

	/// <summary>
	/// The world lightmaps a lightmapped object draws with: those of the map its model is in
	/// (<c>maps/construct_25/worldnodes/...</c>), read from the map's world file. Null for an object without lightmaps.
	/// </summary>
	ShaderParameters LightmapFor( SceneObject sceneObject, Model model )
	{
		if ( MirrorContent.Gpu is not { } gpu ) return null;
		if ( SceneNative.LightmapKey( sceneObject ) == 0 || model?.ResourcePath is not { } path || !path.StartsWith( "maps/", StringComparison.OrdinalIgnoreCase ) ) return null;
		var end = path.IndexOf( '/', 5 );
		if ( end <= 5 ) return null;

		// Held while this world draws them, so a map's lightmaps go once no world does
		var folder = path[..end];
		lock ( lightmapFolders )
		{
			if ( lightmapFolders.Add( folder ) ) gpu.HoldMapLightmaps( folder );
		}

		return gpu.MapLightmapParameters( folder );
	}

	// The maps whose lightmaps this world's objects drew with (LightmapFor), released when it goes
	readonly HashSet<string> lightmapFolders = new( StringComparer.OrdinalIgnoreCase );

	// The file materials and textures this world's sky and fog drew with (WorldMaterial, WorldTexture), held while it lives
	readonly HashSet<string> heldMaterials = new( StringComparer.OrdinalIgnoreCase );
	readonly HashSet<string> heldTextures = new( StringComparer.OrdinalIgnoreCase );

	/// <summary><see cref="WorldMaterial"/>, made once, for what takes a material lookup (objects' draws).</summary>
	public Func<Material, GpuMaterial> MaterialResolver => materialResolver ??= WorldMaterial;
	Func<Material, GpuMaterial> materialResolver;

	/// <summary><see cref="WorldTexture"/>, made once, for conversions that take a texture lookup (fog's sky textures).</summary>
	public Func<Texture, GpuTexture> TextureResolver => textureResolver ??= WorldTexture;
	Func<Texture, GpuTexture> textureResolver;

	/// <summary>
	/// A material this world draws with (its sky's), as <see cref="MirrorContent.Material"/> has it, but a file's held by the
	/// world rather than kept for good: it goes with the world unless something else holds it.
	/// </summary>
	public GpuMaterial WorldMaterial( Material material )
	{
		if ( MirrorContent.Gpu is not { } gpu || material?.FilePath is not { Length: > 0 } path || material.MadeFromShader is not null || material.SetInCode is { Count: > 0 } ) return MirrorContent.Material( material );
		lock ( heldMaterials ) return heldMaterials.Add( path ) ? gpu.ModelMaterial( path ) : gpu.HeldMaterial( path );
	}

	GpuTexture ISceneWorldRender.Texture( Texture texture ) => WorldTexture( texture );

	bool ISceneWorldRender.OnDevice => MirrorContent.Gpu is not null;

	GpuTexture ISceneWorldRender.TextureWhenRead( Texture texture, SceneObject waiting, SceneObjectChange change ) => TextureFor( texture, waiting, change );

	/// <summary>A texture this world draws with (its fog's sky), as <see cref="MirrorContent.Texture"/> has it, but a file's held by the world.</summary>
	public GpuTexture WorldTexture( Texture texture )
	{
		// A material's texture (a sky's cubemap for fog and sky lighting): the material's, held by the world
		if ( MirrorContent.Gpu is { } device && texture is not null && string.IsNullOrEmpty( texture.ResourcePath ) && Sandbox.Texture.MaterialOrigins.TryGetValue( texture, out var origin ) && origin is Sandbox.Texture.MaterialOrigin o )
		{
			GpuMaterial material;
			lock ( heldMaterials ) material = heldMaterials.Add( o.Material ) ? device.ModelMaterial( o.Material ) : device.HeldMaterial( o.Material );
			return device.MaterialTexture( material, o.Parameter );
		}

		if ( MirrorContent.Gpu is not { } gpu || texture is null || texture.IsRenderTarget || MirrorContent.IsCaptureCube( texture ) || texture.ResourcePath is not { Length: > 0 } path ) return MirrorContent.Texture( texture );
		lock ( heldTextures ) return heldTextures.Add( path ) ? gpu.HoldTexture( path ) : gpu.HeldTexture( path );
	}

	/// <summary>
	/// Mirror probe placement and selection data; create attributes once (<c>CLightBinner2::GetLightProbeVolumeData</c>).
	/// </summary>
	void ApplyLightProbe( SceneLightProbe probe, Borealis.LightProbeVolume volume, SceneObjectChange change )
	{
		if ( (change & SceneObjectChange.Transform) != 0 )
			volume.Transform = probe.Transform;

		if ( (change & SceneObjectChange.Visibility) != 0 )
			volume.Enabled = probe.RenderingEnabled;

		if ( volume.Attributes is not null ) return;

		SceneNative.ReadLightProbeVolume( probe, volume );
		volume.Handshake = probe.Handshake;
		if ( MirrorContent.Gpu is not { } gpu ) return;

		// Its constants made here, over its textures read by path, once they've loaded
		var texture = TextureFor( probe.ProbeTexture, probe, SceneObjectChange.Settings );
		var indices = TextureFor( probe.IndicesTexture, probe, SceneObjectChange.Settings );
		var scalars = TextureFor( probe.ScalarsTexture, probe, SceneObjectChange.Settings );
		if ( !TexturesWaiting( probe ) ) volume.Attributes = gpu.LightProbeVolumeParameters( texture, indices, scalars, probe.Transform, volume.BoxMins, volume.BoxMaxs );
	}

	/// <summary>
	/// Create a supported render object, or null. Suns update world lighting separately.
	/// </summary>
	RenderObject Add( SceneObject sceneObject )
	{
		// Probe volumes belong to the world's lighting list, not its draw list. An extra world's aren't drawn in another's: the
		// list is the main world's, for every view of it
		if ( sceneObject is SceneLightProbe probe )
		{
			var volume = new Borealis.LightProbeVolume();
			lightProbes[probe] = volume;
			World.LightProbeVolumes.Add( volume );
			undrawn.Add( sceneObject );
			added++;
			return null;
		}

		RenderObject obj = sceneObject.OwnsRenderObject ? sceneObject.MakeRenderObject() : sceneObject switch
		{
			SceneDirectionalLight => null,
			SceneSkyBox => null,
			SceneModel => new MeshObject( null, sceneObject.Transform ),
			SceneCustomObject custom => new BridgeCustomObject( custom ),
			SceneDynamicObject dynamic => new BridgeDynamicObject( dynamic ),
			SceneAggregateObject => AggregateFor( sceneObject ),

			// A map's world geometry is native aggregates, which have no managed type of their own
			_ when sceneObject.GetType() == typeof( SceneObject ) => (RenderObject)AggregateFor( sceneObject, out var waiting ) ?? (waiting ? null : new MeshObject( null, sceneObject.Transform )),
			_ => null,
		};

		added++;
		sceneObject.RenderObject = obj;
		if ( obj is not null ) World.Add( obj );
		else
		{
			undrawn.Add( sceneObject );
			if ( sceneObject is SceneDirectionalLight sun ) suns.Add( sun );
			else if ( sceneObject is not SceneSkyBox ) Count( sceneObject, 1 );
		}

		return obj;
	}

	void Remove( SceneObject sceneObject )
	{
		lock ( gate ) deletedFrames.Remove( sceneObject );
		if ( !Added( sceneObject, out var obj ) ) return;
		added--;
		undrawn.Remove( sceneObject );
		if ( obj is not null ) sceneObject.RenderObject = null;

		if ( obj is MeshObject mesh )
		{
			Release( mesh.Mesh );
			attributed.Remove( mesh );
			mesh.AttributesFrom = null;
			sharing.Remove( mesh );
			mesh.LightmapAttributes = null;
		}
		else if ( obj is AggregateObject aggregate )
		{
			Release( aggregate.Mesh );
			aggregate.LightmapAttributes = null;
		}

		if ( sceneObject is SceneCubemap cubemap ) uncaptured.Remove( cubemap );
		if ( sceneObject is SceneLightProbe probe && lightProbes.Remove( probe, out var volume ) ) World.LightProbeVolumes.Remove( volume );
		else if ( obj is not null ) World.Remove( obj );
		else if ( sceneObject is SceneDirectionalLight sun ) sunDirty |= suns.Remove( sun );
		else if ( sceneObject is not SceneSkyBox ) Count( sceneObject, -1 );
	}

	void Count( SceneObject sceneObject, int by )
	{
		var name = sceneObject.GetType().Name;
		var count = Unsupported.GetValueOrDefault( name ) + by;
		if ( count > 0 ) Unsupported[name] = count;
		else Unsupported.Remove( name );
	}

	void ApplyMesh( SceneObject sceneObject, MeshObject mesh, SceneObjectChange change )
	{
		// Native draw states include body groups and all material overrides, including whole-object overrides.
		// Tint/flag changes do not require draw readback.
		var meshChanged = false;
		const SceneObjectChange Draws = SceneObjectChange.Model | SceneObjectChange.Material | SceneObjectChange.Visibility;
		if ( (change & Draws) != 0 )
		{
			var drawnModel = sceneObject.Model;
			var drawn = sceneObject.RenderingEnabled ? VariantFor( sceneObject, ModelFor( drawnModel ) ) : null;
			if ( drawn is null && drawnModel is not null && loading.Contains( drawnModel ) ) WaitFor( drawnModel, sceneObject );
			if ( drawn != mesh.Mesh )
			{
				Release( mesh.Mesh );
				Hold( drawn );
				mesh.Mesh = drawn;
				meshChanged = true;

				// Shared world lightmap attributes.
				mesh.LightmapAttributes = drawn is { HasLightmappedDraws: true } ? LightmapFor( sceneObject, drawnModel ) : null;
			}
		}

		if ( (change & SceneObjectChange.Tint) != 0 )
			mesh.Tint = sceneObject.ColorTint;

		// Fixed LOD from the renderer or map.
		if ( (change & (Draws | SceneObjectChange.Settings)) != 0 )
			mesh.LodOverride = sceneObject.LodOverride;

		if ( (change & SceneObjectChange.Flags) != 0 )
		{
			// Static state controls shadow caching and world-tag filtering.
			var flags = sceneObject.Flags;
			mesh.CastShadows = flags.CastShadows;
			mesh.IsStatic = flags.IsStatic;
			mesh.IsWorld = mesh.IsStatic || flags.HasFlag( SceneObjectFlags.IsHammerGeometry );

			// Its renderer's own command lists (Renderer.ExecuteBefore, ExecuteAfter)
			if ( flags.WantsExecuteBefore || flags.WantsExecuteAfter )
			{
				var callbacks = mesh.Callbacks as BridgeDrawCallbacks ?? new BridgeDrawCallbacks( sceneObject );
				callbacks.Before = flags.WantsExecuteBefore;
				callbacks.After = flags.WantsExecuteAfter;
				mesh.Callbacks = callbacks;
			}
			else
			{
				mesh.Callbacks = null;
			}
		}

		// Material changes recompute native blend flags.
		if ( (change & (SceneObjectChange.Flags | Draws)) != 0 )
			ApplyMeshLayers( sceneObject, mesh );

		if ( (change & SceneObjectChange.Transform) != 0 )
			mesh.Transform = sceneObject.Transform;

		// Local lighting origins move with the object.
		// Only a map's objects have one (worldnode.cpp), so one managed code made isn't asked
		if ( (change & (SceneObjectChange.Transform | SceneObjectChange.Flags | Draws)) != 0 )
			mesh.LightingOrigin = !sceneObject.ResolvesDraws && sceneObject.native.HasLightingOrigin() ? sceneObject.native.GetLightingOrigin() : null;

		// Track mutable attributes, read when a view draws the object (Refresh)
		if ( (change & SceneObjectChange.Attributes) != 0 )
		{
			if ( sceneObject.CreatedAttributes is { } attributes ) attributed[mesh] = attributes;
			else attributed.Remove( mesh );
			mesh.AttributesFrom = attributed.ContainsKey( mesh ) ? this : null;
			sharing.Remove( mesh );
			mesh.Attributes = null;
		}

		// Restore posed local bounds after mesh changes reset them to bind-pose bounds.
		const SceneObjectChange Pose = SceneObjectChange.Bones | SceneObjectChange.Transform | SceneObjectChange.Model;
		if ( ((change & Pose) != 0 || meshChanged) && sceneObject is SceneModel model && mesh.Mesh is { IsSkinned: true } )
		{
			// The matrices native skins with, straight into the object's own: a bone each, as many as its mesh's bind pose has
			model.GetBoneWorldMatrices( mesh.PoseBoneMatrices( mesh.Mesh.BindPose.Length ) );

			// Native evaluates morph flex rules each frame, from the model's: only a new model or mesh changes whether it morphs
			if ( meshChanged || (change & SceneObjectChange.Model) != 0 )
			{
				mesh.MorphSource = model.Model is not { MorphCount: > 0 } ? null
					: MirrorContent.Gpu is not { } gpu ? null
					: mesh.MorphSource is GpuSceneModelMorphs gpuMorphs && gpuMorphs.Source == model ? gpuMorphs : new GpuSceneModelMorphs( model, gpu );
			}

			// Native bounds include deformation padding.
			var volumes = MemoryMarshal.Cast<SceneDeformationVolumeData, DeformationVolume>( model.DeformationVolumes );
			if ( !volumes.SequenceEqual( mesh.DeformationVolumes ) ) mesh.DeformationVolumes = volumes.ToArray();
			var anchors = model.DeformationAnchors;
			if ( !anchors.SequenceEqual( mesh.DeformationAnchors ) ) mesh.DeformationAnchors = anchors.ToArray();

			mesh.LocalBounds = ToLocal( model.Bounds, mesh.Transform );
		}
	}

	/// <summary>
	/// An aggregate's model and fragments as native draws them, or null if the object isn't one.
	/// </summary>
	AggregateObject AggregateFor( SceneObject sceneObject ) => AggregateFor( sceneObject, out _ );

	/// <summary>An aggregate's model and fragments, or null; <paramref name="waiting"/> while its model is still loading.</summary>
	AggregateObject AggregateFor( SceneObject sceneObject, out bool waiting )
	{
		var model = SceneNative.AggregateModel( sceneObject, out var lightmapped );
		waiting = false;
		if ( ModelFor( model )?.Mesh is not { } mesh )
		{
			waiting = model is not null && loading.Contains( model );
			if ( waiting ) WaitFor( model, sceneObject );
			return null;
		}

		Hold( mesh );

		return new AggregateObject( mesh, SceneNative.ReadAggregateFragments( sceneObject ) ) { Lightmapped = lightmapped };
	}

	/// <summary>
	/// Mirror an aggregate's flags, tint and lightmaps. Its fragments are fixed when it's made.
	/// </summary>
	void ApplyAggregate( SceneObject sceneObject, AggregateObject aggregate, SceneObjectChange change )
	{
		if ( (change & SceneObjectChange.Visibility) != 0 )
			aggregate.Visible = sceneObject.RenderingEnabled;

		if ( (change & SceneObjectChange.Tint) != 0 )
			aggregate.Tint = sceneObject.ColorTint;

		if ( (change & SceneObjectChange.Flags) != 0 )
		{
			var flags = sceneObject.Flags;
			ApplyLayerFlags( aggregate, flags );
			aggregate.CastShadows = flags.CastShadows;
			aggregate.IsStatic = flags.IsStatic;
			aggregate.IsWorld = aggregate.IsStatic || flags.HasFlag( SceneObjectFlags.IsHammerGeometry );
			aggregate.OcclusionCulling = !flags.HasFlag( SceneObjectFlags.NoOcclusionCulling );
		}

		if ( (change & SceneObjectChange.Added) != 0 && aggregate.Lightmapped )
		{
			aggregate.LightmapAttributes = LightmapFor( sceneObject, SceneNative.AggregateModel( sceneObject, out _ ) );
		}
	}

	/// <summary>
	/// Mirror custom-object placement, culling bounds and flags.
	/// </summary>
	static void ApplyCustom( SceneObject sceneObject, CustomObject custom, SceneObjectChange change )
	{
		// A dynamic object's material is known before it draws (gizmos, the debug overlay): whether it needs a stencil, so the
		// view's depth has one before the frame records. Other custom objects pick their materials as they draw
		if ( custom is BridgeDynamicObject dynamic && RenderBackend.Default is BorealisGpuRenderBackend gpu ) custom.NeedsStencil = gpu.NeedsStencil( dynamic.Source.ManagedMaterial );

		const SceneObjectChange Placement = SceneObjectChange.Transform | SceneObjectChange.Bounds;
		if ( (change & Placement) != 0 )
		{
			// Unplaced custom objects may report zero scale; native uses identity.
			var transform = sceneObject.Transform;
			custom.Transform = transform == default ? Transform.Zero : transform;

			// Bounds as kept (WorldBounds): SceneObject.Bounds collapses infinity to a point.
			// Use large finite bounds to keep culling math valid.
			var bounds = sceneObject.WorldBounds;
			if ( !float.IsFinite( bounds.Mins.x + bounds.Mins.y + bounds.Mins.z + bounds.Maxs.x + bounds.Maxs.y + bounds.Maxs.z ) || bounds.Size.x > 1e9f )
				bounds = new BBox( new Vector3( -1e9f ), new Vector3( 1e9f ) );

			custom.LocalBounds = ToLocal( bounds, custom.Transform );
		}

		const SceneObjectChange State = SceneObjectChange.Tint | SceneObjectChange.Flags | SceneObjectChange.Visibility;
		if ( (change & State) != 0 )
		{
			custom.Tint = sceneObject.ColorTint;
			custom.IsOpaque = sceneObject.Flags.IsOpaque && !sceneObject.Flags.IsTranslucent;
			custom.WantsFrameBufferCopy = sceneObject.Flags.WantsFrameBufferCopy;
			custom.DepthPrepass = !sceneObject.Flags.HasFlag( SceneObjectFlags.NoZPrepass );
			custom.CastShadows = sceneObject.Flags.CastShadows;
			custom.Visible = sceneObject.RenderingEnabled;
			ApplyLayerFlags( custom, sceneObject.Flags );
			custom.LayerMatch = LayerMatchFor( sceneObject.LayerMatchToken );
		}
	}

	static readonly uint StaticOverlayMatch = new StringToken( "StaticOverlayLayer" ).Value;
	static readonly uint OverlayWithDepthMatch = new StringToken( "OverlayWithDepth" ).Value;
	static readonly uint OverlayWithoutDepthMatch = new StringToken( "OverlayWithoutDepth" ).Value;

	/// <summary>
	/// Mirror native blend/prepass flags and layer match (<c>CSceneObject::m_nLayerMatchID</c>).
	/// MeshRenderFeature owns classification.
	/// </summary>
	static void ApplyMeshLayers( SceneObject sceneObject, MeshObject mesh )
	{
		var flags = sceneObject.Flags;
		ApplyLayerFlags( mesh, flags );
		mesh.DrawsOpaque = flags.HasFlag( SceneObjectFlags.IsOpaque );
		mesh.DrawsTranslucent = flags.HasFlag( SceneObjectFlags.IsTranslucent );
		mesh.DepthPrepass = !flags.HasFlag( SceneObjectFlags.NoZPrepass );
		mesh.OcclusionCulling = !flags.HasFlag( SceneObjectFlags.NoOcclusionCulling );
		mesh.WantsFrameBufferCopy = flags.HasFlag( SceneObjectFlags.WantsFrameBufferCopyTexture );
		mesh.Decal = flags.HasFlag( SceneObjectFlags.IsDecal );
		mesh.NeedsLightProbe = flags.HasFlag( SceneObjectFlags.NeedsLightProbe );
		mesh.LightGroup = (uint)sceneObject.LightGroup;
		mesh.LightProbeHandshake = sceneObject.ResolvesDraws ? 0 : SceneNative.LightProbeHandshake( sceneObject );

		mesh.LayerMatch = LayerMatchFor( sceneObject.LayerMatchToken );
		mesh.RenderOrder = mesh.LayerMatch == LayerMatch.StaticOverlay ? sceneObject.RenderOrder : 0;
	}

	/// <summary>
	/// Describe unusual mesh flags, fading or deformation; empty for ordinary meshes.
	/// </summary>
	static string LayerDescription( MeshObject mesh )
	{
		var parts = new List<string>();
		if ( !mesh.GameLayers ) parts.Add( "not game" );
		if ( mesh.Overlay ) parts.Add( "overlay" );
		if ( mesh.Bloom ) parts.Add( "bloom" );
		if ( mesh.AfterUI ) parts.Add( "after UI" );
		if ( mesh.Decal ) parts.Add( "decal" );
		if ( mesh.LayerMatch != LayerMatch.None ) parts.Add( $"matched {mesh.LayerMatch}" );
		if ( mesh.Tint.a < 1 ) parts.Add( $"faded {mesh.Tint.a:0.##}" );
		if ( !mesh.DrawsOpaque && !mesh.DrawsTranslucent ) parts.Add( "neither opaque nor translucent" );
		if ( mesh.WantsFrameBufferCopy ) parts.Add( "reads frame buffer" );
		if ( mesh.IsDeformed ) parts.Add( $"{mesh.DeformationVolumes.Length} deformation volumes" );
		return parts.Count == 0 ? "" : $" ({string.Join( ", ", parts )})";
	}

	/// <summary>
	/// Translate native layer-match IDs; MeshRenderFeature decides membership.
	/// </summary>
	static LayerMatch LayerMatchFor( uint match )
	{
		if ( match == 0 ) return LayerMatch.None;
		if ( match == StaticOverlayMatch ) return LayerMatch.StaticOverlay;
		if ( match == OverlayWithDepthMatch ) return LayerMatch.OverlayWithDepth;
		if ( match == OverlayWithoutDepthMatch ) return LayerMatch.OverlayWithoutDepth;
		return LayerMatch.Unsupported;
	}

	/// <summary>
	/// Mirror game, overlay and effect flags from native <c>RenderOptions</c>, and whether cube captures leave it out.
	/// </summary>
	static void ApplyLayerFlags( RenderObject obj, SceneObject.SceneObjectFlagAccessor flags )
	{
		obj.GameLayers = !flags.HasFlag( SceneObjectFlags.ExcludeGameLayer );
		obj.Overlay = flags.HasFlag( SceneObjectFlags.GameOverlayLayer );
		obj.Bloom = flags.HasFlag( SceneObjectFlags.EffectsBloomLayer );
		obj.AfterUI = flags.HasFlag( SceneObjectFlags.UIOverlayLayer );
		obj.HiddenInCaptures = flags.HasFlag( SceneObjectFlags.HideInCubemaps );
	}

	/// <summary>
	/// The cube a probe reflects. A realtime probe's cube is made when it's first captured (the envmap scheduler captures the
	/// probes that are seen), not for every probe a map has: it reflects black until then, as a cube cleared for it did.
	/// </summary>
	public GpuTexture ProbeCube( SceneCubemap cubemap )
	{
		var texture = cubemap.Texture;
		if ( LazyCaptureCubes && MirrorContent.IsCaptureCube( texture ) )
		{
			var made = MirrorContent.MadeCaptureCube( texture );
			if ( made is null ) uncaptured[cubemap] = texture;
			else uncaptured.Remove( cubemap );
			return made;
		}

		uncaptured.Remove( cubemap );
		return TextureFor( texture, cubemap, SceneObjectChange.Settings );
	}

	/// <summary>
	/// Omit empty attributes to preserve instancing (<c>CBaseSceneObjectDesc::BindMaterial</c>), and let objects whose
	/// attributes hold the same values share one set (<see cref="SharedAttributes"/>), so they instance and keep the material
	/// bound between them as objects with none do. Values change in place without notice, so they're read each render that
	/// draws the object, as native reads them as it draws: reading every object's each sync cost Mow the Lawn half a
	/// millisecond a frame, nearly all of it its 3,000 empty sets' memory.
	/// </summary>
	public void Refresh( MeshObject mesh )
	{
		var values = attributed.TryGetValue( mesh, out var attributes ) ? attributes.ManagedValues : null;
		if ( values is null || values.IsEmpty )
		{
			mesh.Attributes = null;
			sharing.Remove( mesh );
			return;
		}

		mesh.Attributes = MirrorContent.ObjectParameters( SharedAttributes( mesh, attributes, values ) );
	}

	// The attribute set each object draws with, someone's own whose values are the same as its own, and the sets lately shared
	readonly Dictionary<MeshObject, RenderAttributes> sharing = new();
	readonly List<RenderAttributes> shareable = new();

	/// <summary>
	/// How many of the sets lately shared a changed object's attributes are compared with before it draws with its own. Bounds the
	/// cost of an object whose attributes change every frame.
	/// </summary>
	const int MaxShareable = 32;

	/// <summary>
	/// The attribute set an object draws with: the one it shared last sync, while it still holds the same values as its own, or
	/// another of the same values, or its own. A dresser gives every renderer of a citizen <c>skin_tint</c> and <c>skin_age</c>,
	/// most often the defaults, so a crowd's bodies and clothing all have attributes, which made each draw alone and rebind its
	/// material. Values can change in place on either side, so every object compares its own with the set it shares each sync.
	/// </summary>
	RenderAttributes SharedAttributes( MeshObject mesh, RenderAttributes own, ManagedRenderAttributes values )
	{
		if ( sharing.TryGetValue( mesh, out var shared ) && SameValues( values, shared ) ) return shared;

		for ( int i = 0; i < shareable.Count; i++ )
		{
			var candidate = shareable[i];
			if ( candidate.ManagedValues is not { IsEmpty: false } )
			{
				shareable.RemoveAt( i-- );
				continue;
			}

			if ( !SameValues( values, candidate ) ) continue;

			sharing[mesh] = candidate;
			return candidate;
		}

		// Its own, for the next to share
		sharing[mesh] = own;
		shareable.Insert( 0, own );
		if ( shareable.Count > MaxShareable ) shareable.RemoveAt( shareable.Count - 1 );
		return own;
	}

	static bool SameValues( ManagedRenderAttributes values, RenderAttributes other ) => other.ManagedValues is { } theirs && values.SameValues( theirs );

	/// <summary>
	/// Refresh dirty sun state, per-view sky selection and camera lighting (<c>CameraRenderer.Configure</c>).
	/// Uses the first enabled sun; native uses the last encountered by its binner.
	/// </summary>
	void SyncLighting( SceneCamera camera, bool skyboxView )
	{
		var lighting = World.Lighting;

		if ( sunDirty )
		{
			sunDirty = false;

			SceneDirectionalLight sun = null;
			foreach ( var candidate in suns )
			{
				if ( candidate.IsValid() && candidate.RenderingEnabled )
				{
					sun = candidate;
					break;
				}
			}

			if ( sun is not null )
			{
				lighting.SunColor = sun.LightColor;
				// Map suns can have identity transforms: their direction is the description's (SceneLight.SetWorldDirection)
				lighting.SunDirection = -sun.WorldDirection;
				lighting.SunShadows = sun.ShadowsEnabled;
				lighting.SunShadowCascades = sun.ShadowCascadeCount;
				lighting.SunShadowSplitRatio = sun.ShadowCascadeSplitRatio;
				lighting.SunShadowHardness = sun.ShadowHardness;
				lighting.SunShadowBias = sun.ShadowBias;

				// Lightmaps and probes reference the sun by bake index.
				LightPacking.Pack( sun, out var packed, out var flags, out var bakeIndex );
				lighting.SunBakeIndex = bakeIndex;
				lighting.SunBaked = bakeIndex >= 0 ? packed : null;

				var baked = (flags & LightPacking.TypeFlagsBaked) != 0;
				// A baked sun still renders cascades, without its static casters: native's binner only asks local lights for
				// mixed shadows (CLightBinnerStandard::AddLight), and volumetric fog is lit by the sun only through them
				lighting.SunShadowsBaked = baked && !sun.IncludeStaticShadowCasters;
				lighting.SunContactShadows = sun.ContactShadows;
				lighting.SunFogStrength = sun.FogStrength;
			}
			else
			{
				lighting.SunColor = Color.Black;
				lighting.SunBakeIndex = -1;
				lighting.SunBaked = null;
				lighting.SunShadowsBaked = false;
				lighting.SunContactShadows = false;
			}
		}

		// Sky selection is per view: cameras can exclude different skies without changing the world.
		// Native's child skybox view does not inherit the parent camera's object-tag filters.
		var sky = skyboxView ? source.GetSky() : source.GetSky( camera.RenderTags, camera.ExcludeTags );
		lighting.SkyMaterial = WorldMaterial( sky?.Material );
		if ( sky is not null )
		{
			lighting.SkyTint = sky.SkyTint;
			lighting.SkyRotation = sky.Transform.Rotation;
			lighting.SkyFog = new SkyFog( (SkyFogType)sky.FogParams.FogType, sky.FogParams.FogMinStart, sky.FogParams.FogMinEnd, sky.FogParams.FogMaxStart, sky.FogParams.FogMaxEnd );
		}

		lighting.AmbientColor = source.AmbientLightColor + camera.AmbientLightColor;
		lighting.GradientFog = source.GradientFog.ToBorealis();

		// Per-view wind (SetWindParams).
		SceneNative.ReadWorldWind( source, out var windStrength, out var windDirection );
		lighting.WindStrengthFreq = windStrength;
		lighting.WindDirection = windDirection;

		var from = camera.CubemapFog;
		var to = lighting.CubemapFog;
		to.Enabled = from.Enabled;
		to.Texture = WorldTexture( from.Texture );
		to.StartDistance = from.StartDistance;
		to.EndDistance = from.EndDistance;
		to.FalloffExponent = from.FalloffExponent;
		to.LodBias = from.LodBias;
		to.HeightWidth = from.HeightWidth;
		to.HeightStart = from.HeightStart;
		to.HeightExponent = from.HeightExponent;
		to.Tint = from.Tint;
		to.Transform = from.Transform;

		// Camera fog settings (SceneCamera.GatherVolumetricFog).
		var fogFrom = camera.VolumetricFog;
		var fogTo = lighting.VolumetricFog;
		fogTo.Enabled = fogFrom.Enabled;
		fogTo.Anisotropy = fogFrom.Anisotropy;
		fogTo.Scattering = fogFrom.Scattering;
		fogTo.DrawDistance = fogFrom.DrawDistance;
		fogTo.FadeInStart = fogFrom.FadeInStart;
		fogTo.FadeInEnd = fogFrom.FadeInEnd;
		fogTo.IndirectStrength = fogFrom.IndirectStrength;
		fogTo.BakedIndirectTexture = WorldTexture( fogFrom.BakedIndirectTexture );

		SyncFogVolumes();
	}

	int fogVolumesVersion = -1;
	readonly List<FogVolume> fogVolumePool = new();

	/// <summary>
	/// Refresh changed fog volumes in native order, reusing pooled objects (<c>SceneWorld.FogVolumes</c>).
	/// </summary>
	void SyncFogVolumes()
	{
		if ( source.FogVolumesVersion == fogVolumesVersion ) return;
		fogVolumesVersion = source.FogVolumesVersion;

		var volumes = World.FogVolumes;
		volumes.Clear();
		foreach ( var from in source.FogVolumes )
		{
			if ( fogVolumePool.Count <= volumes.Count ) fogVolumePool.Add( new FogVolume() );
			var to = fogVolumePool[volumes.Count];
			to.Transform = from.Transform;
			to.Bounds = from.BoundingBox;
			to.Strength = from.FogStrength;
			to.Exponent = from.FalloffExponent;
			to.Color = from.Color;
			to.Spherical = false;
			volumes.Add( to );
		}
	}

	/// <summary>
	/// Summarize mirrored lighting and models for parity diagnostics.
	/// </summary>
	public string Describe()
	{
		var lighting = World.Lighting;
		var text = new System.Text.StringBuilder();
		var others = suns.Count( x => x.IsValid() && x.RenderingEnabled ) - 1;
		text.Append( $"sun {lighting.SunColor} towards {lighting.SunDirection} (shadows {lighting.SunShadows}, {lighting.SunShadowCascades} cascades{(others > 0 ? $", {others} more: " + string.Join( ", ", suns.Where( x => x.IsValid() && x.RenderingEnabled ).Skip( 1 ).Select( x => $"{x.LightColor} towards {-x.WorldDirection}" ) ) : "")}), ambient {lighting.AmbientColor}, gradient fog {lighting.GradientFog.Enabled}, cubemap fog {lighting.CubemapFog.Enabled}, volumetric fog {lighting.VolumetricFog.Enabled} with {World.FogVolumes.Count} volumes" );

		foreach ( var obj in World.Objects )
		{
			switch ( obj )
			{
				case LightObject light:
					text.Append( $"; {light.Kind} light at {light.Transform.Position} colour {light.Color} radius {light.Radius} attenuation {light.Attenuation} shadows {light.CastShadows}{(light.Baked ? $" baked{(light.MixedShadows ? " with mixed shadows" : "")}" : "")}{(light.BakeIndex >= 0 ? $" bake index {light.BakeIndex}" : "")}{(light.NativePacked is null ? "" : " (native packed)")}" );
					break;
				case EnvMapObject envMap:
					text.Append( $"; probe {(envMap.Cubemap is not { } cube ? "(none)" : $"{cube.Width}px {cube.Format}")} box {envMap.ProjectionBounds} priority {envMap.Priority} tint {envMap.Tint}" );
					break;
			}
		}

		var meshes = World.Objects.ToArray().OfType<MeshObject>().ToArray();
		text.Append( $"; {meshes.Count( x => x.LightmapAttributes is not null )} of {meshes.Length} meshes lightmapped, {meshes.Count( x => x.Mesh is { HasLightmappedDraws: true } )} with lightmapped draws; sun bake index {lighting.SunBakeIndex}{(lighting.SunBaked is { } baked ? $", packed towards {baked.Row0.x:0.###},{baked.Row1.x:0.###},{baked.Row2.x:0.###} color {baked.Color}" : "")}" );
		// Compare probe packing within float tolerance, excluding setup-time cubemap indices.
		var probes = 0;
		var probesAsNative = 0;
		foreach ( var sceneObject in source.SceneObjects )
		{
			if ( Find( sceneObject ) is not EnvMapObject envMap ) continue;
			probes++;
			if ( SceneNative.PacksAsNative( sceneObject, envMap ) ) probesAsNative++;
		}
		text.Append( $"; {probesAsNative} of {probes} envmap probes packed as native packs them" );

		var volumes = World.LightProbeVolumes;
		text.Append( $"; {volumes.Count} light probe volumes ({volumes.Count( x => x.Attributes is not null )} with constants, light groups {string.Join( " ", volumes.SelectMany( x => x.LightGroups ).Distinct() )}), {meshes.Count( x => x.NeedsLightProbe )} meshes need one, {meshes.Count( x => x.NeedsLightProbe && Borealis.LightProbeVolume.Choose( volumes, World.BoundsCenter[x.Index], World.BoundsCenter[x.Index], World.BoundsExtents[x.Index], x.LightGroup ) >= 0 )} find one" );

		// Report unusual layer membership per model.
		foreach ( var group in meshes.GroupBy( x => (x.Mesh?.Geometry?.Name ?? "(hidden)", LayerDescription( x )) ) )
			text.Append( $"; {group.Count()}x {group.Key.Item1}{group.Key.Item2}" );

		foreach ( var custom in World.Objects.ToArray().OfType<BridgeCustomObject>() )
			text.Append( $"; {custom.Source.GetType().Name} {(custom.IsOpaque ? "opaque" : "translucent")}{(custom.CastShadows ? " casting shadows" : "")}{(custom.Visible ? "" : " hidden")} bounds {custom.LocalBounds}" );

		return text.ToString();
	}

	/// <summary>
	/// A scene object's tags as string tokens (<see cref="SceneObject.TagTokens"/>).
	/// </summary>
	static uint[] TagsOf( SceneObject sceneObject )
	{
		var tokens = sceneObject.TagTokens;
		return tokens.Count == 0 ? [] : [.. tokens];
	}

	/// <summary>
	/// A world box in an object's space: the box around its corners taken back through the transform.
	/// </summary>
	static BBox ToLocal( BBox world, Transform transform )
	{
		var mins = new Vector3( float.MaxValue );
		var maxs = new Vector3( float.MinValue );

		for ( int i = 0; i < 8; i++ )
		{
			var corner = new Vector3( (i & 1) == 0 ? world.Mins.x : world.Maxs.x, (i & 2) == 0 ? world.Mins.y : world.Maxs.y, (i & 4) == 0 ? world.Mins.z : world.Maxs.z );
			var local = transform.PointToLocal( corner );
			mins = Vector3.Min( mins, local );
			maxs = Vector3.Max( maxs, local );
		}

		return new BBox( mins, maxs );
	}
}
