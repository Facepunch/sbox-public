using Borealis.Source2.Resources;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;

namespace Borealis.Source2.Models;

/// <summary>
/// A compiled model (<c>.vmdl_c</c>): its meshes with their decoded vertex and index buffers and draw calls, LODs and
/// their switch distances, mesh groups and the body groups made from them, material groups, bounds and the skeleton.
/// See the <c>.vmdl_c</c> section of docs/reference/borealis-gpu.md.
/// <para>
/// Read from the <c>DATA</c> block (KV3, <c>PermModelData_t</c>), the <c>CTRL</c> block (KV3, which blocks hold each
/// embedded mesh) and each mesh's <c>MDAT</c> (KV3, <c>CRenderMesh</c>), <c>MBUF</c> (buffers) and <c>MSHL</c> (cooked meshlets,
/// <see cref="CookedMeshlets"/>) blocks. Morph targets
/// (<c>MRPH</c>), animation (<c>ANIM</c>, <c>ASEQ</c>, <c>AGRP</c>), physics (<c>PHYS</c>), attachments and hit boxes
/// aren't read.
/// </para>
/// </summary>
public sealed class ModelFile
{
	/// <summary>The model's path, <c>models/dev/box.vmdl</c>.</summary>
	public string Name { get; init; }

	/// <summary>Flags from compiling (<c>ModelFlags_t</c>): translucency, skinned meshes, no shadows...</summary>
	public ModelFlags Flags { get; init; }

	/// <summary>Its meshes (<c>m_refMeshes</c>), which draws and masks index.</summary>
	public IReadOnlyList<ModelMesh> Meshes { get; init; }

	/// <summary>
	/// Each LOD's switch distance (<c>m_lodGroupSwitchDistances</c>), compared against 50 over the screen width of a
	/// radius 0.5 sphere (<c>CModel::LODLevelForScreenSize</c>); empty for a model without LODs.
	/// </summary>
	public IReadOnlyList<float> LodSwitchDistances { get; init; }

	/// <summary>
	/// How many LODs it draws: one past the highest LOD bit any mesh uses, or one when every mesh draws at every LOD
	/// (<c>CModel::ComputeMaxLODLevelUsedByModel</c>).
	/// </summary>
	public int LodCount { get; init; }

	/// <summary>Mesh group names, by mask bit (<c>Head_@0</c> is choice 0 of body group <c>Head</c>).</summary>
	public IReadOnlyList<string> MeshGroups { get; init; }

	/// <summary>The mesh groups drawn unless something chooses others.</summary>
	public ulong DefaultMeshGroupMask { get; init; }

	/// <summary>Body groups made from the mesh groups, as <c>CModel_Imp::SetupMeshGroups</c> makes them.</summary>
	public IReadOnlyList<BodyGroup> BodyGroups { get; init; }

	/// <summary>
	/// Material groups (skins): group 0 lists the materials the others replace, entry by entry. Empty when there's only
	/// the default.
	/// </summary>
	public IReadOnlyList<MaterialGroup> MaterialGroups { get; init; }

	/// <summary>The model's skeleton: bones, parents and bind pose.</summary>
	public ModelSkeleton Skeleton { get; init; }

	/// <summary>Bounds of every mesh (<c>CModel::GetMeshBounds</c>, what <c>Model.Bounds</c> returns).</summary>
	public Vector3 Mins { get; init; }

	/// <summary>Bounds of every mesh.</summary>
	public Vector3 Maxs { get; init; }

	/// <summary>The model's key values as KV3 text (<c>m_keyValueText</c>), or empty.</summary>
	public string KeyValues { get; init; }

	/// <summary>Whether any mesh is skinned.</summary>
	public bool IsSkinned => Meshes.Any( x => x.IsSkinned );

	/// <summary>Whether any mesh has morph targets (which aren't read).</summary>
	public bool HasMorphs => Meshes.Any( x => x.HasMorphs );

	/// <summary>Whether it has animation of its own embedded (which isn't read).</summary>
	public bool HasAnimation { get; init; }

	/// <summary>Whether it has physics embedded (which isn't read).</summary>
	public bool HasPhysics { get; init; }

	readonly short[] remapping;
	readonly ushort[] remappingStarts;

	ModelFile( short[] remapping, ushort[] remappingStarts )
	{
		this.remapping = remapping;
		this.remappingStarts = remappingStarts;
	}

	/// <summary>The same model with other meshes (as many, in the same order): a copy with its meshes cooked (<see cref="MeshletCooker.Cook"/>).</summary>
	internal ModelFile WithMeshes( IReadOnlyList<ModelMesh> meshes ) => Copy( Name, meshes, HasAnimation );

	/// <summary>This model as a model made on it reads (<see cref="Read(byte[], Func{string, byte[]}, Func{byte[]})"/>): the same, under that model's name, with its animation.</summary>
	ModelFile MadeOn( string name, bool hasAnimation ) => Copy( name, Meshes, HasAnimation || hasAnimation );

	ModelFile Copy( string name, IReadOnlyList<ModelMesh> meshes, bool hasAnimation ) => new( remapping, remappingStarts )
	{
		Name = name,
		Flags = Flags,
		Meshes = meshes,
		LodSwitchDistances = LodSwitchDistances,
		LodCount = LodCount,
		MeshGroups = MeshGroups,
		DefaultMeshGroupMask = DefaultMeshGroupMask,
		BodyGroups = BodyGroups,
		MaterialGroups = MaterialGroups,
		Skeleton = Skeleton,
		Mins = Mins,
		Maxs = Maxs,
		KeyValues = KeyValues,
		HasAnimation = hasAnimation,
		HasPhysics = HasPhysics,
	};

	/// <summary>
	/// Let go of the CPU copy of every buffer that can be read again from where it came from (a model read with <c>again</c>,
	/// see <see cref="Read(byte[], Func{string, byte[]}, Func{byte[]})"/>), once they're on the GPU. What reads one later (meshlet
	/// and level builds, traces, readbacks) decodes it again, and it's freed by the next collection.
	/// </summary>
	public void ReleaseBuffers()
	{
		foreach ( var mesh in Meshes )
		{
			foreach ( var buffer in mesh.VertexBuffers ) buffer.Release();
			foreach ( var buffer in mesh.IndexBuffers ) buffer.Release();
		}
	}

	/// <summary>
	/// The base model a model's key values name (<c>base_model_resource</c>, as ModelDoc compiles a model's base model), or
	/// null for none.
	/// </summary>
	internal static string BaseModel( string keyValues )
	{
		if ( string.IsNullOrEmpty( keyValues ) || !keyValues.Contains( "base_model_resource", StringComparison.Ordinal ) ) return null;
		var match = BaseModelKey.Match( keyValues );
		return match.Success && match.Groups[1].Length > 0 ? match.Groups[1].Value : null;
	}

	// base_model_resource = resource:"models/citizen/citizen.vmdl", the value's type prefix optional
	static readonly System.Text.RegularExpressions.Regex BaseModelKey = new( @"\bbase_model_resource\s*=\s*(?:[A-Za-z_]+:)?""([^""]*)""", System.Text.RegularExpressions.RegexOptions.CultureInvariant );

	/// <summary>
	/// The model bone each of a mesh's bones is (<c>m_remappingTable</c> from <c>m_remappingTableStarts[mesh]</c>);
	/// empty for a mesh without bones.
	/// </summary>
	public int[] MeshBoneToModelBone( int mesh )
	{
		var bones = Meshes[mesh].Bones.Count;
		if ( bones == 0 ) return [];
		if ( mesh >= remappingStarts.Length ) throw new InvalidDataException( $"{Name} has no bone remapping for mesh {mesh}" );

		var start = remappingStarts[mesh];
		var result = new int[bones];
		for ( int i = 0; i < bones; i++ ) result[i] = start + i < remapping.Length ? remapping[start + i] : -1;
		return result;
	}

	int[] aggregateDraws;

	/// <summary>
	/// The draw call (numbered across meshes in order) that a world aggregate's fragment draws, given the fragment's draw
	/// index (<c>m_nDrawDescriptorIndex</c>), or -1 for none. The map compiler gives aggregate mesh i the mesh id i and its
	/// fragment the draw index i, then the mesh compiler writes the draws grouped by vertex buffer, so a draw's place in
	/// the file can differ from its fragment's: native draws each draw for the fragment its <see cref="MeshDrawCall.MeshId"/>
	/// names (<c>CAggregateSceneObjectDesc::DrawArrayExt</c>). Draws whose ids aren't each of 0 to n - 1 once are taken in
	/// file order.
	/// </summary>
	public int AggregateDrawCall( int drawIndex )
	{
		var draws = aggregateDraws ??= AggregateDraws( Meshes );
		return (uint)drawIndex < (uint)draws.Length ? draws[drawIndex] : -1;
	}

	/// <summary>For each draw index, the draw call with that mesh id; file order where the ids don't number the draws.</summary>
	internal static int[] AggregateDraws( IReadOnlyList<ModelMesh> meshes )
	{
		var count = meshes.Sum( x => x.DrawCalls.Count );
		var byId = new int[count];
		Array.Fill( byId, -1 );

		var call = 0;
		var numbered = true;
		foreach ( var mesh in meshes )
		{
			foreach ( var draw in mesh.DrawCalls )
			{
				if ( (uint)draw.MeshId >= (uint)count || byId[draw.MeshId] >= 0 ) numbered = false;
				else byId[draw.MeshId] = call;
				call++;
			}
		}

		if ( numbered ) return byId;
		for ( int i = 0; i < count; i++ ) byId[i] = i;
		return byId;
	}

	/// <summary>Whether mesh <paramref name="mesh"/> draws at LOD <paramref name="lod"/>.</summary>
	public bool DrawsAtLod( int mesh, int lod ) => (Meshes[mesh].LodMask & (1 << lod)) != 0;

	/// <summary>The LOD <c>CModel::LODLevelForScreenSize</c> picks for a sphere of radius 0.5 this many pixels wide.</summary>
	public int LodForScreenSize( float screenWidthInPixels, float scale = 1 )
	{
		var metric = screenWidthInPixels > 0 ? 50.0f / screenWidthInPixels : 0;
		var lod = Math.Max( LodSwitchDistances.Count - 1, 0 );
		for ( ; lod > 0; lod-- )
		{
			var distance = LodSwitchDistances[lod] * scale;
			if ( distance > 0 && distance < metric ) break;
		}

		return lod;
	}

	/// <summary>
	/// The material a draw uses with material group <paramref name="group"/>: its own, or the group's entry where group
	/// 0 lists it (<c>CModel::GetMaterialReplacementForGroup</c>).
	/// </summary>
	public string MaterialFor( string material, int group )
	{
		if ( group <= 0 || group >= MaterialGroups.Count ) return material;
		var defaults = MaterialGroups[0].Materials;
		for ( int i = 0; i < defaults.Count && i < MaterialGroups[group].Materials.Count; i++ )
		{
			if ( string.Equals( defaults[i], material, StringComparison.OrdinalIgnoreCase ) ) return MaterialGroups[group].Materials[i];
		}

		return material;
	}

	/// <summary>
	/// Read a model from its <c>.vmdl_c</c> bytes. Meshes kept in <c>.vmesh_c</c> files of their own are read through
	/// <paramref name="external"/> (a compiled path to bytes, or null when it isn't there); without it they throw. A model
	/// made on a base model (<c>base_model_resource</c> in its key values) reads as its base, read through
	/// <paramref name="external"/> too, under its own name. <paramref name="again"/>, when the file can be read again, gives
	/// its bytes anew (as <paramref name="external"/> gives the others'): <see cref="ReleaseBuffers"/> may then let go of the
	/// buffers, which are decoded from them again when something reads one.
	/// </summary>
	public static ModelFile Read( byte[] data, Func<string, byte[]> external = null, Func<byte[]> again = null ) => Read( data, external, again, 0 );

	/// <summary>How deep base models may nest before a chain of them counts as a loop.</summary>
	const int MostBaseModels = 8;

	static ModelFile Read( byte[] data, Func<string, byte[]> external, Func<byte[]> again, int depth )
	{
		var resource = ResourceFile.Read( data );
		if ( Kv3.Read( resource.Block( "DATA" ) ) is not Dictionary<string, object> root ) throw new InvalidDataException( "Model DATA isn't an object" );
		var control = resource.Has( "CTRL" ) ? Kv3.Read( resource.Block( "CTRL" ) ) as Dictionary<string, object> : null;
		var name = root.Get( "m_name" ) as string;
		var info2 = root.Get( "m_modelInfo" ) as Dictionary<string, object>;

		// A model made on a base model (ModelDoc's base model, which adds animation to another model) is its base: meshes,
		// skeleton, groups and physics, with its own animation added (CModel_Imp::InitFromV0Data's InitFromModel)
		if ( BaseModel( info2?.Get( "m_keyValueText" ) as string ) is { } basePath )
		{
			if ( depth >= MostBaseModels ) throw new InvalidDataException( $"{name}'s base models nest more than {MostBaseModels} deep" );
			var bytes = external?.Invoke( basePath ) ?? throw new FileNotFoundException( $"{name} is made on {basePath}, which isn't there" );
			return Read( bytes, external, again is null ? null : () => external( basePath ), depth + 1 ).MadeOn( name, hasAnimation: control?.Get( "embedded_animation" ) is not null );
		}

		var blocks = BlockTable( resource );

		var references = root.Get( "m_refMeshes" ) as object[] ?? [];
		var meshGroupMasks = root.Get( "m_refMeshGroupMasks" ) as object[] ?? [];
		var lodMasks = Kv.Numbers( root.Get( "m_refLODGroupMasks" ) );

		// Embedded meshes by their index among m_refMeshes
		var embedded = new Dictionary<int, Dictionary<string, object>>();
		foreach ( var mesh in Kv.Objects( control?.Get( "embedded_meshes" ) ) )
			embedded[(int)Kv.Number( mesh.Get( "mesh_index" ), -1 )] = mesh;

		var meshes = new ModelMesh[references.Length];
		var reread = again is null ? null : new Reread( again );
		for ( int i = 0; i < meshes.Length; i++ )
		{
			var groups = i < meshGroupMasks.Length ? Kv.Mask( meshGroupMasks[i] ) : ulong.MaxValue;
			var lods = i < lodMasks.Length ? (byte)lodMasks[i] : (byte)0xFF;

			if ( embedded.TryGetValue( i, out var info ) )
			{
				var meshName = info.Get( "name" ) as string ?? $"mesh{i}";
				var dataBlock = Block( resource, blocks, info.Get( "data_block" ), "MDAT", meshName );
				var vbib = info.Get( "vbib_block" );
				var vbibBlock = Block( resource, blocks, vbib, "MBUF", meshName );
				var morphBlock = (int)Kv.Number( info.Get( "morph_block" ), -1 );
				var morphs = morphBlock >= 0 ? ReadMorphs( Block( resource, blocks, info.Get( "morph_block" ), "MRPH", meshName ) ) : null;
				var meshlets = Kv.Number( info.Get( "meshlet_block" ), -1 ) >= 0 ? Block( resource, blocks, info.Get( "meshlet_block" ), CookedMeshlets.BlockName, meshName ) : default;
				Func<ReadOnlyMemory<byte>> againBuffers = reread is null ? null : () => EmbeddedBuffers( reread, vbib, meshName );
				meshes[i] = ModelMesh.Read( dataBlock, vbibBlock, meshName, i, groups, lods, morphBlock >= 0, info.Get( "morph_texture" ) as string, morphs, meshlets, againBuffers );
			}
			else if ( references[i] is string path && path.Length > 0 )
			{
				var bytes = external?.Invoke( path ) ?? throw new FileNotFoundException( $"{name} uses mesh {path}, which isn't there" );
				var mesh = ResourceFile.Read( bytes );
				var buffers = mesh.Has( "VBIB" ) ? mesh.Block( "VBIB" ) : mesh.Block( "MBUF" );
				var morphs = mesh.Has( "MRPH" ) ? ReadMorphs( mesh.Block( "MRPH" ) ) : null;
				Func<ReadOnlyMemory<byte>> againBuffers = null;
				if ( again is not null )
				{
					var file = new Reread( () => external( path ) );
					againBuffers = () => ExternalBuffers( file );
				}

				meshes[i] = ModelMesh.Read( mesh.Block( "DATA" ), buffers, Path.GetFileNameWithoutExtension( path ), i, groups, lods, mesh.Has( "MRPH" ), null, morphs, mesh.Block( CookedMeshlets.BlockName ), againBuffers );
			}
			else
			{
				throw new InvalidDataException( $"{name} mesh {i} is neither embedded nor a reference" );
			}
		}

		var mins = new Vector3( float.MaxValue );
		var maxs = new Vector3( float.MinValue );
		foreach ( var mesh in meshes )
		{
			mins = Vector3.Min( mins, mesh.Mins );
			maxs = Vector3.Max( maxs, mesh.Maxs );
		}

		if ( meshes.Length == 0 ) mins = maxs = Vector3.Zero;

		var meshGroups = Kv.Strings( root.Get( "m_meshGroups" ) );
		var materialGroups = Kv.Objects( root.Get( "m_materialGroups" ) )
			.Select( x => new MaterialGroup( x.Get( "m_name" ) as string ?? "", Kv.Strings( x.Get( "m_materials" ) ) ) )
			.ToArray();

		return new ModelFile( Kv.Numbers( root.Get( "m_remappingTable" ) ).Select( x => (short)x ).ToArray(), Kv.Numbers( root.Get( "m_remappingTableStarts" ) ).Select( x => (ushort)x ).ToArray() )
		{
			Name = name,
			Flags = (ModelFlags)(uint)Kv.Number( info2?.Get( "m_nFlags" ) ),
			KeyValues = info2?.Get( "m_keyValueText" ) as string ?? "",
			Meshes = meshes,
			LodSwitchDistances = Kv.Numbers( root.Get( "m_lodGroupSwitchDistances" ) ).Select( x => (float)x ).ToArray(),
			LodCount = CountLods( lodMasks ),
			MeshGroups = meshGroups,
			DefaultMeshGroupMask = root.Get( "m_nDefaultMeshGroupMask" ) is { } mask ? Kv.Mask( mask ) : ulong.MaxValue,
			BodyGroups = MakeBodyGroups( meshGroups, meshes ),
			MaterialGroups = materialGroups,
			Skeleton = ModelSkeleton.Read( root.Get( "m_modelSkeleton" ) as Dictionary<string, object> ),
			Mins = mins,
			Maxs = maxs,
			HasAnimation = control?.Get( "embedded_animation" ) is not null,
			HasPhysics = control?.Get( "embedded_physics" ) is not null || Kv.Objects( root.Get( "m_refPhysicsData" ) ).Any(),
		};
	}

	/// <summary>A mesh's morph set, or null when its block can't be read.</summary>
	static MorphSet ReadMorphs( ReadOnlySpan<byte> block )
	{
		try
		{
			return MorphSet.Read( block );
		}
		catch ( InvalidDataException )
		{
			return null;
		}
	}

	/// <summary>Read a <c>.vmdl_c</c> from disk; external meshes are looked for beside it, under the same content root.</summary>
	public static ModelFile Load( string path ) => Read( File.ReadAllBytes( path ) );

	/// <summary>Read a model from compiled content by its path, with any external meshes it references.</summary>
	public static ModelFile Load( ContentFiles content, string path ) => Read( content.Read( path ), content.TryRead );

	/// <summary>Blocks in file order, which the control block's block numbers index.</summary>
	static List<(string Type, int Offset, int Size)> BlockTable( ResourceFile resource )
	{
		var data = resource.Data;
		var entry = 8 + BitConverter.ToInt32( data, 8 );
		var count = BitConverter.ToInt32( data, 12 );
		var blocks = new List<(string, int, int)>( count );
		for ( int i = 0; i < count; i++, entry += 12 )
			blocks.Add( (Encoding.ASCII.GetString( data, entry, 4 ), entry + 4 + BitConverter.ToInt32( data, entry + 4 ), BitConverter.ToInt32( data, entry + 8 )) );
		return blocks;
	}

	static ReadOnlySpan<byte> Block( ResourceFile resource, List<(string Type, int Offset, int Size)> blocks, object index, string type, string mesh )
	{
		var (offset, size) = BlockRange( blocks, index, type, mesh );
		return resource.Data.AsSpan( offset, size );
	}

	static (int Offset, int Size) BlockRange( List<(string Type, int Offset, int Size)> blocks, object index, string type, string mesh )
	{
		var i = (int)Kv.Number( index, -1 );
		if ( i < 0 || i >= blocks.Count || blocks[i].Type != type ) throw new InvalidDataException( $"Mesh {mesh}'s {type} block {i} isn't one" );
		return (blocks[i].Offset, blocks[i].Size);
	}

	/// <summary>The buffer block of an embedded mesh, from the model's file read again.</summary>
	static ReadOnlyMemory<byte> EmbeddedBuffers( Reread file, object index, string mesh )
	{
		var resource = ResourceFile.Read( file.Bytes() );
		var (offset, size) = BlockRange( BlockTable( resource ), index, "MBUF", mesh );
		return resource.Data.AsMemory( offset, size );
	}

	/// <summary>The buffer block of a mesh file of its own, read again.</summary>
	static ReadOnlyMemory<byte> ExternalBuffers( Reread file )
	{
		var resource = ResourceFile.Read( file.Bytes() );
		var (offset, size) = resource.Find( resource.Has( "VBIB" ) ? "VBIB" : "MBUF" );
		return resource.Data.AsMemory( offset, size );
	}

	/// <summary>
	/// A file's bytes, read again when buffers let go of are decoded again: held weakly, so the buffers decoded one after
	/// another (a model's builds) read the file once, and nothing holds it between them.
	/// </summary>
	sealed class Reread( Func<byte[]> read )
	{
		WeakReference<byte[]> last;

		public byte[] Bytes()
		{
			lock ( this )
			{
				if ( last is { } kept && kept.TryGetTarget( out var alive ) ) return alive;
				var bytes = read() ?? throw new FileNotFoundException( "A model's file isn't there to read its buffers again" );
				if ( last is null ) last = new( bytes );
				else last.SetTarget( bytes );
				return bytes;
			}
		}
	}

	static int CountLods( double[] lodMasks )
	{
		byte highest = 0;
		var all = 0;
		foreach ( var mask in lodMasks )
		{
			if ( (byte)mask == 0xFF ) all++;
			highest = Math.Max( highest, (byte)mask );
		}

		if ( all == lodMasks.Length || highest == 0 ) return 1;
		return 32 - BitOperations.LeadingZeroCount( (uint)highest );
	}

	/// <summary>
	/// Mesh groups named <c>Part_@N</c> are choice N of body group <c>Part</c>; each choice's mask is the union of the
	/// masks of meshes in it. A <c>Body</c> group holding mesh 0's mask comes first when mesh 0 isn't in any.
	/// </summary>
	static BodyGroup[] MakeBodyGroups( IReadOnlyList<string> meshGroups, ModelMesh[] meshes )
	{
		if ( meshes.Length == 0 ) return [];

		var groups = new List<(string Name, List<ulong> Choices)>();
		var meshZero = false;
		for ( int g = 0; g < meshGroups.Count; g++ )
		{
			var delimiter = meshGroups[g].IndexOf( "_@", StringComparison.OrdinalIgnoreCase );
			if ( delimiter < 0 ) continue;

			var part = meshGroups[g][..delimiter];
			var index = groups.FindIndex( x => string.Equals( x.Name, part, StringComparison.OrdinalIgnoreCase ) );
			if ( index < 0 )
			{
				index = groups.Count;
				groups.Add( (part, new List<ulong>()) );
			}

			ulong choice = 0;
			for ( int m = 0; m < meshes.Length; m++ )
			{
				if ( (meshes[m].MeshGroupMask & (1ul << g)) == 0 ) continue;
				choice |= meshes[m].MeshGroupMask;
				if ( m == 0 ) meshZero = true;
			}

			groups[index].Choices.Add( choice );
		}

		var result = groups.Select( x => new BodyGroup( x.Name, x.Choices ) ).ToList();
		if ( !meshZero ) result.Insert( 0, new BodyGroup( "Body", [meshes[0].MeshGroupMask] ) );
		return result.ToArray();
	}

	/// <inheritdoc/>
	public override string ToString() => Name;
}

/// <summary>A body group: its name and each choice's mesh group mask.</summary>
public sealed record BodyGroup( string Name, IReadOnlyList<ulong> Choices )
{
	/// <summary>Every mesh group any choice draws.</summary>
	public ulong Mask => Choices.Aggregate( 0ul, ( a, b ) => a | b );
}

/// <summary>A material group (skin): its name and materials, entry by entry against group 0's.</summary>
public sealed record MaterialGroup( string Name, IReadOnlyList<string> Materials );

/// <summary>
/// The model's skeleton (<c>ModelSkeletonData_t</c>): bone names, parents (-1 for roots), flags, bounding sphere radii
/// and the bind pose in parent space.
/// </summary>
public sealed class ModelSkeleton
{
	/// <summary>Bone names.</summary>
	public IReadOnlyList<string> Names { get; init; } = [];

	/// <summary>Each bone's parent, -1 for a root.</summary>
	public IReadOnlyList<int> Parents { get; init; } = [];

	/// <summary>Each bone's flags (<c>CModelSkeleton::BoneFlags_t</c>).</summary>
	public IReadOnlyList<uint> Flags { get; init; } = [];

	/// <summary>Each bone's bounding sphere radius.</summary>
	public IReadOnlyList<float> Spheres { get; init; } = [];

	/// <summary>Each bone's bind pose position, relative to its parent.</summary>
	public IReadOnlyList<Vector3> Positions { get; init; } = [];

	/// <summary>Each bone's bind pose rotation, relative to its parent.</summary>
	public IReadOnlyList<Quaternion> Rotations { get; init; } = [];

	/// <summary>How many bones.</summary>
	public int Count => Names.Count;

	/// <summary>A bone's index by name, or -1.</summary>
	public int IndexOf( string name )
	{
		for ( int i = 0; i < Names.Count; i++ )
		{
			if ( string.Equals( Names[i], name, StringComparison.OrdinalIgnoreCase ) ) return i;
		}

		return -1;
	}

	/// <summary>
	/// Every bone's bind pose in the model's space (parents come before children), as <c>System.Numerics</c> matrices
	/// (row vectors: a point is transformed by <c>Vector3.Transform( p, m )</c>).
	/// </summary>
	public Matrix4x4[] BindPose()
	{
		var result = new Matrix4x4[Count];
		for ( int i = 0; i < result.Length; i++ )
		{
			var local = Matrix4x4.CreateFromQuaternion( Rotations[i] ) * Matrix4x4.CreateTranslation( Positions[i] );
			var parent = Parents[i];
			result[i] = parent >= 0 && parent < i ? local * result[parent] : local;
		}

		return result;
	}

	internal static ModelSkeleton Read( Dictionary<string, object> skeleton )
	{
		if ( skeleton is null ) return new ModelSkeleton();
		return new ModelSkeleton
		{
			Names = Kv.Strings( skeleton.Get( "m_boneName" ) ),
			Parents = Kv.Numbers( skeleton.Get( "m_nParent" ) ).Select( x => (int)x ).ToArray(),
			Flags = Kv.Numbers( skeleton.Get( "m_nFlag" ) ).Select( x => (uint)(long)x ).ToArray(),
			Spheres = Kv.Numbers( skeleton.Get( "m_boneSphere" ) ).Select( x => (float)x ).ToArray(),
			Positions = (skeleton.Get( "m_bonePosParent" ) as object[] ?? []).Select( Kv.Vector3 ).ToArray(),
			Rotations = (skeleton.Get( "m_boneRotParent" ) as object[] ?? []).Select( Kv.Quaternion ).ToArray(),
		};
	}
}

/// <summary><c>ModelFlags_t</c>.</summary>
[Flags]
public enum ModelFlags : uint
{
#pragma warning disable CS1591
	None = 0,
	Translucent = 1 << 0,
	TranslucentTwoPass = 1 << 1,
	RuntimeCombined = 1 << 2,
	Source1Import = 1 << 3,
	PartChild = 1 << 4,
	HasSkinnedMeshes = 1 << 10,
	NoForcedFade = 1 << 11,
	ForcePhonemeCrossfade = 1 << 12,
	DoNotCastShadows = 1 << 17,
	NoAnimEvents = 1 << 20,
	AnimationDrivenFlexes = 1 << 21,
	ImplicitBindPoseSequence = 1 << 22,
	ModelDoc = 1 << 23,
#pragma warning restore CS1591
}

/// <summary>Reading KV3 values as the model's types.</summary>
internal static class Kv
{
	public static double Number( object value, double fallback = 0 ) => value switch
	{
		int i => i,
		uint u => u,
		long l => l,
		ulong ul => ul,
		float f => f,
		double d => d,
		bool b => b ? 1 : 0,
		_ => fallback,
	};

	/// <summary>A 64-bit mask, kept exact (a double would round it).</summary>
	public static ulong Mask( object value ) => value switch
	{
		long l => unchecked((ulong)l),
		ulong u => u,
		int i => unchecked((ulong)(long)i),
		uint u => u,
		_ => (ulong)Number( value ),
	};

	public static double[] Numbers( object value ) => value is object[] items ? items.Select( x => Number( x ) ).ToArray() : [];

	public static string[] Strings( object value ) => value is object[] items ? items.Select( x => x as string ?? "" ).ToArray() : [];

	public static IEnumerable<Dictionary<string, object>> Objects( object value ) => value is object[] items ? items.OfType<Dictionary<string, object>>() : [];

	public static Vector3 Vector3( object value ) => value is object[] { Length: >= 3 } v ? new Vector3( (float)Number( v[0] ), (float)Number( v[1] ), (float)Number( v[2] ) ) : default;

	public static Quaternion Quaternion( object value ) => value is object[] { Length: >= 4 } v ? new Quaternion( (float)Number( v[0] ), (float)Number( v[1] ), (float)Number( v[2] ), (float)Number( v[3] ) ) : System.Numerics.Quaternion.Identity;

	/// <summary>
	/// A <c>matrix3x4_t</c> (three rows of four, translation in the last column, for column vectors) as a
	/// <c>System.Numerics</c> matrix (row vectors, translation in the last row).
	/// </summary>
	public static Matrix4x4 Matrix3x4( object value )
	{
		if ( value is not object[] { Length: >= 12 } m ) return Matrix4x4.Identity;
		float F( int i ) => (float)Number( m[i] );
		return new Matrix4x4(
			F( 0 ), F( 4 ), F( 8 ), 0,
			F( 1 ), F( 5 ), F( 9 ), 0,
			F( 2 ), F( 6 ), F( 10 ), 0,
			F( 3 ), F( 7 ), F( 11 ), 1 );
	}

	public static PrimitiveKind Primitive( object value ) => value switch
	{
		string s => s.ToUpperInvariant() switch
		{
			"RENDER_PRIM_POINTS" => PrimitiveKind.Points,
			"RENDER_PRIM_LINES" => PrimitiveKind.Lines,
			"RENDER_PRIM_LINES_WITH_ADJACENCY" => PrimitiveKind.LinesWithAdjacency,
			"RENDER_PRIM_LINE_STRIP" => PrimitiveKind.LineStrip,
			"RENDER_PRIM_LINE_STRIP_WITH_ADJACENCY" => PrimitiveKind.LineStripWithAdjacency,
			"RENDER_PRIM_TRIANGLES" => PrimitiveKind.Triangles,
			"RENDER_PRIM_TRIANGLES_WITH_ADJACENCY" => PrimitiveKind.TrianglesWithAdjacency,
			"RENDER_PRIM_TRIANGLE_STRIP" => PrimitiveKind.TriangleStrip,
			"RENDER_PRIM_TRIANGLE_STRIP_WITH_ADJACENCY" => PrimitiveKind.TriangleStripWithAdjacency,
			"RENDER_PRIM_INSTANCED_QUADS" => PrimitiveKind.Instanced,
			_ => PrimitiveKind.Unknown,
		},
		null => PrimitiveKind.Triangles,
		_ => (PrimitiveKind)(int)Number( value ),
	};
}
