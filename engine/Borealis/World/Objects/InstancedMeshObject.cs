namespace Borealis;

/// <summary>
/// One model drawn at many placements as one object: every placement is culled and gets its LOD on its own. A device with
/// resident meshes (<see cref="GpuDevice.DrawsMeshesResident"/>) has its GPU do that and multi-draw them; any view or device
/// that doesn't draw it resident takes the mesh feature's own path, which culls the placements on the CPU and draws each LOD's
/// as one instanced run (MeshRenderFeature.Instances.cs). Nothing per object but a tint and the layer flags, so it can always
/// be resident; a model with a skeleton or a draw that blends isn't drawn.
/// </summary>
public sealed class InstancedMeshObject : RenderObject
{
	RenderMesh mesh;
	Transform[] instances = [];
	Color[] tints = [];

	/// <summary>
	/// What to draw. Null draws nothing.
	/// </summary>
	public RenderMesh Mesh
	{
		get => mesh;
		set
		{
			if ( ReferenceEquals( mesh, value ) ) return;
			mesh = value;
			Replaced();
			UpdateAnimation();
			DrawChanged();
		}
	}

	internal override RenderMesh.LodAnimation MaterialAnimation => mesh is null ? default : mesh.Animation;

	/// <summary>
	/// Placements in this object's space (its <see cref="RenderObject.Transform"/> applies on top).
	/// </summary>
	public int Count => instances.Length;

	/// <summary>
	/// Colour multiplier of every placement. Alpha is ignored.
	/// </summary>
	public Color Tint
	{
		get;
		set
		{
			if ( field.Equals( value ) ) return;
			field = value;
			Changed( WorldNote.Entry );
		}
	} = Color.White;

	/// <summary>
	/// Drawn into shadow maps.
	/// </summary>
	public bool CastShadows
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			ShadowChanged();
		}
	} = true;

	/// <summary>
	/// Replace every placement, copying them. <paramref name="tints"/> is a colour per placement (alpha ignored), or empty for none.
	/// </summary>
	public void SetInstances( ReadOnlySpan<Transform> transforms, ReadOnlySpan<Color> tints )
	{
		if ( !tints.IsEmpty && tints.Length != transforms.Length ) throw new ArgumentException( "A tint per transform, or none", nameof( tints ) );
		Adopt( transforms.ToArray(), tints.ToArray() );
	}

	/// <summary>
	/// <see cref="SetInstances"/> taking the arrays, which the caller never changes after.
	/// </summary>
	internal void Adopt( Transform[] transforms, Color[] tints )
	{
		var wasEmpty = instances.Length == 0;
		instances = transforms;
		this.tints = tints;
		Replaced();

		// Whether it has anything to draw decides whether it's a resident member at all
		if ( wasEmpty != (transforms.Length == 0) ) DrawChanged();
	}

	/// <summary>
	/// Moved each time the placements or the mesh change: what a kept run of transform entries and a resident owner are made for.
	/// </summary>
	internal int Version { get; private set; }

	/// <summary>
	/// Count a change to the placements and put <see cref="RenderObject.LocalBounds"/> on their union, which moves the object's
	/// transform entry version too (<see cref="RenderWorld.EntryVersions"/>).
	/// </summary>
	void Replaced()
	{
		Version++;
		placed = null;
		var bounds = default( BBox );
		if ( mesh is not null && instances.Length > 0 )
		{
			var local = mesh.Bounds;
			for ( int i = 0; i < instances.Length; i++ )
			{
				RenderWorld.TransformBounds( RenderWorld.ToMatrix( instances[i] ), local, out var center, out var extents );
				var box = new BBox( center - extents, center + extents );
				bounds = i == 0 ? box : bounds.AddBBox( box );
			}
		}

		LocalBounds = bounds;
	}

	/// <summary>
	/// The placements in world space: each one's bounds and LOD scale (its largest scale), made again when the placements, the
	/// mesh or the object's transform change. Never changed once made, so a worker may read one.
	/// </summary>
	internal sealed class Placement
	{
		public Vector3[] Centers, Extents;
		public float[] Scales;
		public int Version, TransformVersion;
	}

	Placement placed;

	internal Placement Placed
	{
		get
		{
			if ( placed is { } made && made.Version == Version && made.TransformVersion == TransformVersion ) return made;

			var count = mesh is null ? 0 : instances.Length;
			var centers = new Vector3[count];
			var extents = new Vector3[count];
			var scales = new float[count];
			for ( int i = 0; i < count; i++ )
			{
				var world = Transform.ToWorld( instances[i] );
				RenderWorld.TransformBounds( RenderWorld.ToMatrix( world ), mesh.Bounds, out centers[i], out extents[i] );
				var scale = world.Scale;
				scales[i] = MathF.Max( MathF.Abs( scale.x ), MathF.Max( MathF.Abs( scale.y ), MathF.Abs( scale.z ) ) );
			}

			return placed = new Placement { Centers = centers, Extents = extents, Scales = scales, Version = Version, TransformVersion = TransformVersion };
		}
	}

	/// <summary>
	/// Entries per placement in its kept run: one, or one per draw of every LOD for a model with draw tints, each its own tint.
	/// </summary>
	internal int EntriesPerInstance
	{
		get
		{
			if ( mesh is not { HasDrawTints: true } ) return 1;
			var count = 0;
			for ( int lod = 0; lod < mesh.LodCount; lod++ ) count += mesh.DrawsForLod( lod ).Length;
			return count;
		}
	}

	/// <summary>
	/// Entries of the whole kept run (<see cref="Gpu.TransformBuffer.KeptRunFor"/>).
	/// </summary>
	internal int RunLength => mesh is null ? 0 : instances.Length * EntriesPerInstance;

	/// <summary>
	/// Write every entry of the run into <paramref name="transforms"/>: kept ones from slot <paramref name="first"/>, or frame
	/// entries (<paramref name="kept"/> off), which the buffer gives one after another, the first at <paramref name="first"/>.
	/// </summary>
	internal void WriteEntries( Gpu.TransformBuffer transforms, int first, bool kept )
	{
		var per = EntriesPerInstance;
		var tint = Tint;
		var slot = first;
		for ( int i = 0; i < instances.Length; i++ )
		{
			var matrix = Matrix3x4.From( Transform.ToWorld( instances[i] ) );
			var own = tints.Length == 0 ? new Color( tint.r, tint.g, tint.b ) : new Color( tint.r * tints[i].r, tint.g * tints[i].g, tint.b * tints[i].b );
			if ( per == 1 )
			{
				Put( slot++, matrix, own );
				continue;
			}

			// A tint per draw, as a mesh with draw tints has (TransformBuffer.TintedSlotsFor)
			for ( int lod = 0; lod < mesh.LodCount; lod++ )
			{
				foreach ( ref readonly var draw in mesh.DrawsForLod( lod ) )
				{
					var drawTint = draw.Tint ?? Vector3.One;
					Put( slot++, matrix, new Color( own.r * drawTint.x, own.g * drawTint.y, own.b * drawTint.z ) );
				}
			}
		}

		void Put( int at, in Matrix3x4 matrix, Color color )
		{
			if ( kept ) transforms.WriteKept( at, matrix, color );
			else transforms.Add( matrix, color );
		}
	}
}
