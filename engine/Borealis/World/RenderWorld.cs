namespace Borealis;

/// <summary>
/// Render objects and dense bounds/transform arrays, equivalent to native's <c>CSceneWorld</c>.
/// Objects update arrays on change so culling avoids chasing references.
/// </summary>
public sealed class RenderWorld
{
	const int InitialCapacity = 64;

	/// <summary>
	/// How many sub-worlds a world can hold (<see cref="RenderObject.SubWorld"/>): one bit each of <see cref="RenderView.SubWorlds"/>.
	/// </summary>
	public const int MaxSubWorlds = 64;

	RenderObject[] _objects = new RenderObject[InitialCapacity];
	Vector3[] _boundsCenters = new Vector3[InitialCapacity];
	Vector3[] _boundsExtents = new Vector3[InitialCapacity];
	Matrix[] _localToWorld = new Matrix[InitialCapacity];
	int[] _treeLeaves = new int[InitialCapacity];
	int[] _drawVersions = new int[InitialCapacity];
	int[] _entryVersions = new int[InitialCapacity];
	long[] _changedAt = new long[InitialCapacity];
	byte[] _subWorlds = new byte[InitialCapacity];

	/// <summary>
	/// Spatial index of size-culled objects.
	/// </summary>
	internal SpatialTree Tree { get; } = new();

	/// <summary>
	/// Dynamic-only spatial index for baked-light shadows and cached lights' dynamic passes.
	/// </summary>
	internal SpatialTree DynamicTree { get; } = new();

	// Each object's leaf in DynamicTree, or NotAdded
	int[] _dynamicTreeLeaves = new int[InitialCapacity];

	// When each object last moved or changed how it casts (SettledCasters.Now), and how often it did after it had settled
	ulong[] _movedStamps = new ulong[InitialCapacity];
	byte[] _settleMoves = new byte[InitialCapacity];

	/// <summary>
	/// When each object last moved or changed how it casts, by index, as a <see cref="Shadows.SettledCasters"/> stamp; it
	/// counts as settled once that's old enough.
	/// </summary>
	internal ReadOnlySpan<ulong> MovedStamps => _movedStamps.AsSpan( 0, Count );

	/// <summary>
	/// Where settled casters that aren't static moved or changed, so the sun's cached far cascade, which holds them, is redrawn
	/// when a change reaches it.
	/// </summary>
	internal StaticCasterChanges SettledChanges { get; } = new();

	/// <summary>
	/// Objects that are never size culled - lights - by index. Few, so views just test them all.
	/// </summary>
	readonly List<int> _unculledObjectIndices = [];

	internal ReadOnlySpan<int> Unculled => System.Runtime.InteropServices.CollectionsMarshal.AsSpan( _unculledObjectIndices );

	readonly List<Type> _objectTypes = [];

	/// <summary>
	/// All types ever added, used to warm feature caches before parallel culling.
	/// </summary>
	internal ReadOnlySpan<Type> ObjectTypes => System.Runtime.InteropServices.CollectionsMarshal.AsSpan( _objectTypes );

	// A leaf in the tree, or one of these
	const int NotAdded = -1;
	const int NotInTree = -2;

	/// <summary>
	/// Where static shadow casters changed, so a light's static shadow cache is redrawn when a change reaches it.
	/// </summary>
	internal StaticCasterChanges StaticChanges { get; } = new();

	/// <summary>
	/// The sun and ambient light the world is drawn with.
	/// </summary>
	public SceneLighting Lighting { get; } = new();

	/// <summary>
	/// Optional 3D skybox; null uses this world's 2D sky.
	/// </summary>
	public Skybox3D Skybox3D { get; set; }

	/// <summary>
	/// Probe volumes for <see cref="MeshObject.NeedsLightProbe"/> objects. Insertion order breaks selection ties.
	/// </summary>
	public List<LightProbeVolume> LightProbeVolumes { get; } = [];

	/// <summary>
	/// The probe volumes its views choose from: its own (<see cref="LightProbeVolumes"/>), then those of the worlds it includes
	/// (<see cref="Include"/>) as stand-ins with their number (<see cref="LightProbeVolume.SubWorld"/>), which light only views
	/// drawing them, as native's binner takes the volumes of every world a view renders. Brought up to date by
	/// <see cref="RefreshDrawnVolumes"/> before a render.
	/// </summary>
	internal List<LightProbeVolume> DrawnLightProbeVolumes => drawnVolumes ?? LightProbeVolumes;

	// The volumes drawn when an included world has some (else null), and for each stand-in the volume it stands for
	List<LightProbeVolume> drawnVolumes;
	readonly List<LightProbeVolume> drawnSources = [];
	readonly Dictionary<LightProbeVolume, (LightProbeVolume StandIn, int Version)> standIns = new( ReferenceEqualityComparer.Instance );

	/// <summary>
	/// Bring <see cref="DrawnLightProbeVolumes"/> up to date with this world's volumes and its included worlds': stand-ins made
	/// for new ones and copied again when the volume they stand for changed. Main thread, before a render; nothing's made
	/// while nothing changes.
	/// </summary>
	internal void RefreshDrawnVolumes()
	{
		var included = 0;
		for ( int i = 0; i < this.included.Length; i++ )
		{
			if ( this.included[i] is { } other ) included += other.LightProbeVolumes.Count;
		}

		if ( included == 0 )
		{
			drawnVolumes = null;
			if ( standIns.Count > 0 ) standIns.Clear();
			return;
		}

		// The same volumes in the same order keep the list; each stand-in is copied again only when its volume changed
		var same = drawnSources.Count == LightProbeVolumes.Count + included;
		var n = 0;
		foreach ( var own in LightProbeVolumes ) same &= n < drawnSources.Count && ReferenceEquals( drawnSources[n++], own );
		for ( int i = 0; i < this.included.Length; i++ )
		{
			if ( this.included[i] is not { } other ) continue;
			foreach ( var volume in other.LightProbeVolumes ) same &= n < drawnSources.Count && ReferenceEquals( drawnSources[n++], volume );
		}

		if ( !same )
		{
			drawnVolumes ??= [];
			drawnVolumes.Clear();
			drawnSources.Clear();
			drawnSources.AddRange( LightProbeVolumes );
			drawnVolumes.AddRange( LightProbeVolumes );
			for ( int i = 0; i < this.included.Length; i++ )
			{
				if ( this.included[i] is not { } other ) continue;
				foreach ( var volume in other.LightProbeVolumes )
				{
					if ( !standIns.TryGetValue( volume, out var standIn ) ) standIns[volume] = standIn = (new LightProbeVolume(), int.MinValue);
					standIn.StandIn.SubWorld = i + 1;
					standIns[volume] = standIn;
					drawnSources.Add( volume );
					drawnVolumes.Add( standIn.StandIn );
				}
			}

			// Stand-ins for volumes that went
			if ( standIns.Count > drawnSources.Count - LightProbeVolumes.Count )
			{
				var gone = new List<LightProbeVolume>();
				foreach ( var (volume, _) in standIns )
				{
					if ( !drawnSources.Contains( volume ) ) gone.Add( volume );
				}

				foreach ( var volume in gone ) standIns.Remove( volume );
			}
		}

		for ( int k = LightProbeVolumes.Count; k < drawnSources.Count; k++ )
		{
			var volume = drawnSources[k];
			var standIn = standIns[volume];
			if ( standIn.Version == volume.Version ) continue;
			standIn.StandIn.CopyFrom( volume );
			standIns[volume] = (standIn.StandIn, volume.Version);
		}
	}

	/// <summary>
	/// Fog volumes in native order, which determines tint in overlaps. Requires enabled volumetric fog.
	/// </summary>
	public List<FogVolume> FogVolumes { get; } = [];

	/// <summary>
	/// Number of objects in the world.
	/// </summary>
	public int Count { get; private set; }

	/// <summary>
	/// How many renders of this world are under way (<see cref="RenderSystem.Render(RenderWorld, RenderView, ViewTarget)"/>):
	/// while any is, its objects mustn't change, so a host applies changes before or after.
	/// </summary>
	public int Rendering { get; internal set; }

	internal ReadOnlySpan<RenderObject> Objects => _objects.AsSpan( 0, Count );
	internal ReadOnlySpan<Vector3> BoundsCenter => _boundsCenters.AsSpan( 0, Count );
	internal ReadOnlySpan<Vector3> BoundsExtents => _boundsExtents.AsSpan( 0, Count );
	internal ReadOnlySpan<Matrix> LocalToWorld => _localToWorld.AsSpan( 0, Count );

	/// <summary>
	/// Each object's sub-world here, by index: 0 for the world's own, else the number a view's <see cref="RenderView.SubWorlds"/>
	/// shows it by - its <see cref="RenderObject.SubWorld"/>, or for another world's object the number <see cref="Include"/> gave
	/// that world.
	/// </summary>
	internal ReadOnlySpan<byte> SubWorlds => _subWorlds.AsSpan( 0, Count );

	/// <summary>
	/// Each object's draw version, by index: a number no other object or change has had, given again whenever something
	/// that decides how the object draws changes (<see cref="DrawChanged"/>), and when it moves to another index. What a
	/// render system keeps of an object between frames is good while the version it was made at is still the object's.
	/// </summary>
	internal ReadOnlySpan<int> DrawVersions => _drawVersions.AsSpan( 0, Count );

	static int s_lastDrawVersion;

	static int NextDrawVersion() => System.Threading.Interlocked.Increment( ref s_lastDrawVersion );

	/// <summary>
	/// Note a change to how an object draws: its mesh, materials, attributes, flags or layers.
	/// </summary>
	internal void DrawChanged( RenderObject obj )
	{
		var index = obj.IndexIn( this );
		if ( index < 0 ) return;
		_drawVersions[index] = NextDrawVersion();
		Changed( index );
	}

	/// <summary>
	/// Counts up at every change to an object in place: how it draws, what its transform entry holds, how it casts, its
	/// lightmap or fragments (<see cref="ChangedAt"/>). Adding and removing objects counts <see cref="Structure"/> instead.
	/// </summary>
	internal long Changes { get; private set; }

	/// <summary>
	/// By index, <see cref="Changes"/> at each object's last change: what keeps something of some objects between frames looks
	/// at only those, and at none while <see cref="Changes"/> hasn't moved.
	/// </summary>
	internal ReadOnlySpan<long> ChangedAt => _changedAt.AsSpan( 0, Count );

	void Changed( int index ) => _changedAt[index] = ++Changes;

	/// <summary>
	/// Note a change to an object that moves neither of its versions: an aggregate's lightmap or fragments.
	/// </summary>
	internal void ObjectChanged( RenderObject obj )
	{
		var index = obj.IndexIn( this );
		if ( index >= 0 ) Changed( index );
	}

	/// <summary>
	/// Each object's transform entry version, by index: counts up whenever what its transform entry holds may have changed - its
	/// transform, its bounds or its tint - and when another object takes the index. A render system rewrites an object's kept
	/// entry when this isn't the version it wrote (<see cref="Gpu.TransformBuffer.KeptSlotFor"/>).
	/// </summary>
	internal ReadOnlySpan<int> EntryVersions => _entryVersions.AsSpan( 0, Count );

	/// <summary>
	/// Note a change to what an object's transform entry holds other than its transform: its tint.
	/// </summary>
	internal void EntryChanged( RenderObject obj )
	{
		var index = obj.IndexIn( this );
		if ( index < 0 ) return;
		_entryVersions[index]++;
		Changed( index );
	}

	/// <summary>
	/// Mesh objects whose attributes are read as they draw (<see cref="MeshObject.AttributesFrom"/>): a world without any
	/// skips looking for them.
	/// </summary>
	internal int AttributeSources { get; set; }

	/// <summary>
	/// Counts up whenever an object is added or removed (so indices may have moved): what keeps a list of some of the
	/// world's objects by index finds out it must look again.
	/// </summary>
	internal int Structure { get; private set; }

	/// <summary>
	/// Add an object. An object can be in one world at a time; worlds drawing this one as a sub-world (<see cref="Include"/>)
	/// hold it too.
	/// </summary>
	public void Add( RenderObject obj )
	{
		ArgumentNullException.ThrowIfNull( obj );
		if ( obj.World is not null ) throw new InvalidOperationException( "Render object is already in a world" );

		obj.World = this;
		Insert( obj, obj.SubWorld );
		foreach ( var (host, subWorld) in drawnIn ) host.Insert( obj, subWorld );
	}

	/// <summary>
	/// Hold an object at the end of the dense arrays: one of this world's own (its <see cref="RenderObject.World"/> is this), or
	/// one of a world drawn here as sub-world <paramref name="subWorld"/>.
	/// </summary>
	void Insert( RenderObject obj, int subWorld )
	{
		var type = obj.GetType();
		if ( !_objectTypes.Contains( type ) ) _objectTypes.Add( type );

		if ( Count == _objects.Length )
		{
			var capacity = _objects.Length * 2;
			Array.Resize( ref _objects, capacity );
			Array.Resize( ref _boundsCenters, capacity );
			Array.Resize( ref _boundsExtents, capacity );
			Array.Resize( ref _localToWorld, capacity );
			Array.Resize( ref _treeLeaves, capacity );
			Array.Resize( ref _drawVersions, capacity );
			Array.Resize( ref _entryVersions, capacity );
			Array.Resize( ref _changedAt, capacity );
			Array.Resize( ref _subWorlds, capacity );
			Array.Resize( ref _dynamicTreeLeaves, capacity );
			Array.Resize( ref _movedStamps, capacity );
			Array.Resize( ref _settleMoves, capacity );
		}

		var index = Count++;
		if ( ReferenceEquals( obj.World, this ) ) obj.Index = index;
		else obj.AddHost( this, index );

		Structure++;
		if ( obj is MeshObject { AttributesFrom: not null } ) AttributeSources++;
		_objects[index] = obj;
		_subWorlds[index] = (byte)subWorld;
		_treeLeaves[index] = NotAdded;
		_dynamicTreeLeaves[index] = NotAdded;
		_settleMoves[index] = 0;
		_drawVersions[index] = NextDrawVersion();
		_entryVersions[index]++;
		Sync( obj );
	}

	/// <summary>
	/// Remove an object, from the worlds drawing this one as a sub-world too. The last object moves into its slot, so indices
	/// aren't stable across removes.
	/// </summary>
	public void Remove( RenderObject obj )
	{
		ArgumentNullException.ThrowIfNull( obj );
		if ( obj.World != this ) return;

		for ( int i = obj.HostCount - 1; i >= 0; i-- ) obj.HostAt( i ).Delete( obj );
		Delete( obj );
		obj.World = null;
		obj.Index = -1;
	}

	/// <summary>
	/// Let go of an object held here, the last one moving into its slot.
	/// </summary>
	void Delete( RenderObject obj )
	{
		var index = obj.IndexIn( this );
		if ( index < 0 ) return;

		var last = --Count;
		Structure++;

		if ( obj.IsStatic ) StaticCasterChanged( obj );
		else if ( IsSettledCaster( obj, index ) ) SettledChanges.Add( _boundsCenters[index], _boundsExtents[index] );

		if ( _treeLeaves[index] >= 0 ) Tree.Remove( _treeLeaves[index] );
		else _unculledObjectIndices.Remove( index );
		if ( _dynamicTreeLeaves[index] >= 0 ) DynamicTree.Remove( _dynamicTreeLeaves[index] );

		if ( index != last )
		{
			var moved = _objects[last];
			_objects[index] = moved;
			_boundsCenters[index] = _boundsCenters[last];
			_boundsExtents[index] = _boundsExtents[last];
			_localToWorld[index] = _localToWorld[last];
			_subWorlds[index] = _subWorlds[last];
			_treeLeaves[index] = _treeLeaves[last];
			_dynamicTreeLeaves[index] = _dynamicTreeLeaves[last];
			_movedStamps[index] = _movedStamps[last];
			_settleMoves[index] = _settleMoves[last];
			_drawVersions[index] = NextDrawVersion();
			_entryVersions[index] = _entryVersions[last] + 1;
			Changed( index );
			moved.SetIndexIn( this, index );

			if ( _treeLeaves[index] >= 0 ) Tree.SetItem( _treeLeaves[index], index );
			else _unculledObjectIndices[_unculledObjectIndices.IndexOf( last )] = index;
			if ( _dynamicTreeLeaves[index] >= 0 ) DynamicTree.SetItem( _dynamicTreeLeaves[index], index );
		}

		_objects[last] = null;
		if ( obj is MeshObject { AttributesFrom: not null } ) AttributeSources--;
		if ( !ReferenceEquals( obj.World, this ) ) obj.RemoveHost( this );
	}

	/// <summary>
	/// Set one of this world's own objects' sub-world (<see cref="RenderObject.SubWorld"/>).
	/// </summary>
	internal void SetSubWorld( RenderObject obj, int subWorld )
	{
		if ( ReferenceEquals( obj.World, this ) && obj.Index >= 0 ) _subWorlds[obj.Index] = (byte)subWorld;
	}

	// The worlds drawing this one as a sub-world, with the number each gave it, and those this one draws, by number - 1
	readonly List<(RenderWorld Host, int SubWorld)> drawnIn = [];
	readonly RenderWorld[] included = new RenderWorld[MaxSubWorlds];

	/// <summary>
	/// Draw another world's objects in this one's views as a sub-world: this world holds them too (the same objects, kept in
	/// step as they change, come and go), and views of it show them while their <see cref="RenderView.SubWorlds"/> has the
	/// number's bit. Returns the number, the one it has already if it's drawn here, or 0 when this world draws
	/// <see cref="MaxSubWorlds"/> already. Its lighting, probe volumes and fog aren't used: this world's light them.
	/// </summary>
	public int Include( RenderWorld other )
	{
		ArgumentNullException.ThrowIfNull( other );
		if ( other == this ) throw new ArgumentException( "A world can't include itself", nameof( other ) );

		var free = -1;
		for ( int i = 0; i < included.Length; i++ )
		{
			if ( included[i] == other ) return i + 1;
			if ( free < 0 && included[i] is null ) free = i;
		}

		if ( free < 0 ) return 0;
		included[free] = other;
		var subWorld = free + 1;
		other.drawnIn.Add( (this, subWorld) );

		// Its own objects, not those of worlds it draws
		foreach ( var obj in other.Objects )
		{
			if ( ReferenceEquals( obj.World, other ) ) Insert( obj, subWorld );
		}

		return subWorld;
	}

	/// <summary>
	/// The number <see cref="Include"/> gave another world here, or 0 when it isn't drawn here.
	/// </summary>
	public int SubWorldOf( RenderWorld other )
	{
		var i = other is null ? -1 : Array.IndexOf( included, other );
		return i < 0 ? 0 : i + 1;
	}

	/// <summary>
	/// Stop drawing a world <see cref="Include"/> drew here: this world lets go of its objects.
	/// </summary>
	public void Exclude( RenderWorld other )
	{
		var i = other is null ? -1 : Array.IndexOf( included, other );
		if ( i < 0 ) return;

		included[i] = null;
		other.drawnIn.Remove( (this, i + 1) );
		foreach ( var obj in other.Objects )
		{
			if ( ReferenceEquals( obj.World, other ) ) Delete( obj );
		}
	}

	/// <summary>
	/// Stop drawing this world in every world that includes it, and every world it includes here (<see cref="Include"/>).
	/// </summary>
	public void ExcludeEverywhere()
	{
		for ( int i = drawnIn.Count - 1; i >= 0; i-- ) drawnIn[i].Host.Exclude( this );
		foreach ( var other in included )
		{
			if ( other is not null ) Exclude( other );
		}
	}

	/// <summary>
	/// Copy an object's transform and bounds into the dense arrays. Objects call this when they change.
	/// </summary>
	internal void Sync( RenderObject obj )
	{
		var index = obj.IndexIn( this );
		if ( index < 0 ) return;

		// A static caster that was already here leaves its old place too
		var logged = obj.IsStatic && CastsShadows( obj );
		if ( logged && _treeLeaves[index] != NotAdded ) StaticChanges.Add( _boundsCenters[index], _boundsExtents[index] );

		// So does a settled one, which is drawn every frame again from now
		var settled = !obj.IsStatic && _treeLeaves[index] != NotAdded && IsSettledCaster( obj, index );
		if ( settled ) SettledChanges.Add( _boundsCenters[index], _boundsExtents[index] );
		Moved( index, settled );

		var matrix = ToMatrix( obj.Transform );
		_localToWorld[index] = matrix;
		_entryVersions[index]++;
		Changed( index );

		// Transform centre/extents to avoid eight corner transforms.
		var local = obj.LocalBounds;
		var center = matrix.Transform( local.Center );
		var halfSize = local.Size * 0.5f;
		var extents = new Vector3(
			MathF.Abs( matrix.M11 ) * halfSize.x + MathF.Abs( matrix.M21 ) * halfSize.y + MathF.Abs( matrix.M31 ) * halfSize.z,
			MathF.Abs( matrix.M12 ) * halfSize.x + MathF.Abs( matrix.M22 ) * halfSize.y + MathF.Abs( matrix.M32 ) * halfSize.z,
			MathF.Abs( matrix.M13 ) * halfSize.x + MathF.Abs( matrix.M23 ) * halfSize.y + MathF.Abs( matrix.M33 ) * halfSize.z );

		_boundsCenters[index] = center;
		_boundsExtents[index] = extents;
		if ( logged ) StaticChanges.Add( center, extents );
		if ( settled ) SettledChanges.Add( center, extents );

		var leaf = _treeLeaves[index];
		if ( leaf >= 0 ) Tree.Move( leaf, center - extents, center + extents );
		else if ( leaf == NotAdded && obj.SizeCulled ) _treeLeaves[index] = Tree.Add( center - extents, center + extents, index );
		else if ( leaf == NotAdded )
		{
			_treeLeaves[index] = NotInTree;
			_unculledObjectIndices.Add( index );
		}

		SyncDynamic( obj );
	}

	/// <summary>
	/// Keep an object in <see cref="DynamicTree"/> while it's in <see cref="Tree"/> and not static, at its current bounds.
	/// </summary>
	internal void SyncDynamic( RenderObject obj )
	{
		var index = obj.IndexIn( this );
		if ( index < 0 ) return;
		var leaf = _dynamicTreeLeaves[index];
		var wanted = _treeLeaves[index] >= 0 && !obj.IsStatic;

		if ( !wanted )
		{
			if ( leaf >= 0 ) DynamicTree.Remove( leaf );
			_dynamicTreeLeaves[index] = NotAdded;
			return;
		}

		var center = _boundsCenters[index];
		var extents = _boundsExtents[index];
		if ( leaf >= 0 ) DynamicTree.Move( leaf, center - extents, center + extents );
		else _dynamicTreeLeaves[index] = DynamicTree.Add( center - extents, center + extents, index );
	}

	/// <summary>
	/// Note a change to how a static caster casts, or to whether it's static, where it is now.
	/// </summary>
	internal void StaticCasterChanged( RenderObject obj )
	{
		var index = obj.IndexIn( this );
		if ( index < 0 ) return;
		Changed( index );
		if ( CastsShadows( obj ) ) StaticChanges.Add( _boundsCenters[index], _boundsExtents[index] );
	}

	/// <summary>
	/// Note a change to how a caster casts: a static one's in <see cref="StaticChanges"/>, a settled one's in
	/// <see cref="SettledChanges"/>, and any that isn't static starts settling again.
	/// </summary>
	internal void CasterChanged( RenderObject obj )
	{
		if ( obj.IsStatic )
		{
			StaticCasterChanged( obj );
			return;
		}

		var index = obj.IndexIn( this );
		if ( index < 0 ) return;
		var settled = IsSettledCaster( obj, index );
		if ( settled ) SettledChanges.Add( _boundsCenters[index], _boundsExtents[index] );
		Moved( index, settled );
	}

	/// <summary>
	/// An object that isn't static moved or changed now: it settles again <see cref="Shadows.SettledCasters.Seconds"/> from
	/// now, or, if it has moved after settling too often, <see cref="Shadows.SettledCasters.RestlessSeconds"/>. A restless one
	/// that stayed put that long starts over.
	/// </summary>
	void Moved( int index, bool wasSettled )
	{
		if ( wasSettled && (_movedStamps[index] & Shadows.SettledCasters.Restless) != 0 ) _settleMoves[index] = 0;
		if ( wasSettled && _settleMoves[index] < byte.MaxValue ) _settleMoves[index]++;
		var restless = _settleMoves[index] >= Shadows.SettledCasters.MaxSettles ? Shadows.SettledCasters.Restless : 0;
		_movedStamps[index] = Shadows.SettledCasters.Now | restless;
		Changed( index );
	}

	/// <summary>
	/// A caster that isn't static and counts as settled now.
	/// </summary>
	bool IsSettledCaster( RenderObject obj, int index ) => Shadows.SettledCasters.Settled( _movedStamps[index] ) && CastsShadows( obj );

	/// <summary>
	/// Lights, environment maps and decals never draw into shadow maps.
	/// </summary>
	static bool CastsShadows( RenderObject obj ) => obj is not (LightObject or EnvMapObject or DecalObject);

	internal static Matrix ToMatrix( in Transform transform ) => Matrix.FromTransform( transform );
}
