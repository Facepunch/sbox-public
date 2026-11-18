using Borealis.Source2.Models;
using Borealis.Source2.Resources;
using System.IO;
using System.Linq;
using System.Numerics;

namespace Borealis.Source2.World;

/// <summary>
/// A compiled map's world (<c>maps/name/world.vwrld_c</c>): its world nodes, each a <c>.vwnod_c</c> of static scene
/// objects (a model and a transform) and aggregates (one model whose draw calls are already in world space, culled one
/// draw at a time). Both are KV3 in the <c>DATA</c> block (<c>WorldBuilderParams_t</c>, <c>WorldNode_t</c>). Entity lumps,
/// lightmaps, visibility and physics are named but not read. See the world section of docs/managed/borealis-gpu.md.
/// </summary>
public sealed class WorldFile
{
	/// <summary>The world's path, <c>maps/construct_25/world.vwrld</c>.</summary>
	public string Name { get; init; }

	/// <summary>Its world nodes, read.</summary>
	public IReadOnlyList<WorldNode> Nodes { get; init; }

	/// <summary>The entity lumps (<c>.vents</c>), which aren't read.</summary>
	public IReadOnlyList<string> EntityLumps { get; init; }

	/// <summary>The baked lighting textures (<c>m_worldLightingInfo.m_lightMaps</c>), which aren't read.</summary>
	public IReadOnlyList<string> Lightmaps { get; init; }

	/// <summary>The scale from lightmap uvs to the lightmaps (<c>m_vLightmapUvScale</c>); one where it isn't set.</summary>
	public Vector2 LightmapUvScale { get; init; } = Vector2.One;

	/// <summary>The bounds of every node, or zero for a world without any.</summary>
	public (Vector3 Mins, Vector3 Maxs) Bounds => Nodes.Count == 0 ? default
		: (Nodes.Select( x => x.Mins ).Aggregate( Vector3.Min ), Nodes.Select( x => x.Maxs ).Aggregate( Vector3.Max ));

	/// <summary>
	/// Read a world and its nodes from <paramref name="content"/> (a map's VPK added with
	/// <see cref="ContentFiles.AddPackage"/>, usually).
	/// </summary>
	public static WorldFile Load( ContentFiles content, string path )
	{
		var root = Data( content.Read( path ), path );
		var nodes = new List<WorldNode>();
		foreach ( var node in Kv.Objects( root.Get( "m_worldNodes" ) ) )
		{
			var prefix = (node.Get( "m_worldNodePrefix" ) as string ?? "").Replace( '\\', '/' );
			nodes.Add( WorldNode.Read( Data( content.Read( prefix + ".vwnod" ), prefix ), prefix, Kv.Vector3( node.Get( "m_vMinBounds" ) ), Kv.Vector3( node.Get( "m_vMaxBounds" ) ) ) );
		}

		var lighting = root.Get( "m_worldLightingInfo" ) as Dictionary<string, object>;
		return new WorldFile
		{
			Name = path,
			Nodes = nodes,
			EntityLumps = Kv.Strings( root.Get( "m_entityLumps" ) ),
			Lightmaps = Kv.Strings( lighting?.Get( "m_lightMaps" ) ),
			LightmapUvScale = lighting?.Get( "m_vLightmapUvScale" ) is object[] { Length: >= 2 } scale ? new Vector2( (float)Kv.Number( scale[0] ), (float)Kv.Number( scale[1] ) ) : Vector2.One,
		};
	}

	static Dictionary<string, object> Data( byte[] bytes, string path )
		=> Kv3.Read( ResourceFile.Read( bytes ).Block( "DATA" ) ) as Dictionary<string, object> ?? throw new InvalidDataException( $"{path}: DATA isn't an object" );
}

/// <summary>
/// One world node (<c>.vwnod_c</c>): static scene objects and aggregates.
/// </summary>
public sealed class WorldNode
{
	/// <summary>Its path without the extension, <c>maps/construct_25/worldnodes/node000</c>.</summary>
	public string Prefix { get; init; }

	/// <summary>Its bounds, from the world.</summary>
	public Vector3 Mins { get; init; }

	/// <summary>Its bounds, from the world.</summary>
	public Vector3 Maxs { get; init; }

	/// <summary>Scene objects (<c>m_sceneObjects</c>): a model each, placed.</summary>
	public IReadOnlyList<WorldSceneObject> SceneObjects { get; init; }

	/// <summary>Aggregates (<c>m_aggregateSceneObjects</c>): a model each whose draw calls are drawn one by one.</summary>
	public IReadOnlyList<WorldAggregate> Aggregates { get; init; }

	/// <summary>Layer names, which <see cref="WorldSceneObject.Layer"/> and <see cref="WorldAggregate.Layer"/> index.</summary>
	public IReadOnlyList<string> LayerNames { get; init; }

	internal static WorldNode Read( Dictionary<string, object> root, string prefix, Vector3 mins, Vector3 maxs )
	{
		var layers = Kv.Numbers( root.Get( "m_sceneObjectLayerIndices" ) );
		var objects = new List<WorldSceneObject>();
		foreach ( var o in Kv.Objects( root.Get( "m_sceneObjects" ) ) )
		{
			var index = objects.Count;
			objects.Add( new WorldSceneObject
			{
				Model = o.Get( "m_renderableModel" ) as string ?? "",
				Transform = Transform( o.Get( "m_vTransform" ) ),
				Tint = Vector4( o.Get( "m_vTintColor" ), System.Numerics.Vector4.One ),
				FadeStart = (float)Kv.Number( o.Get( "m_flFadeStartDistance" ), -1 ),
				FadeEnd = (float)Kv.Number( o.Get( "m_flFadeEndDistance" ) ),
				LodOverride = (int)Kv.Number( o.Get( "m_nLODOverride" ), -1 ),
				MeshGroupMask = o.Get( "m_meshGroupMask" ) is { } mask ? Kv.Mask( mask ) : ulong.MaxValue,
				Skin = o.Get( "m_skin" ) as string ?? "",
				TypeFlags = o.Get( "m_nObjectTypeFlags" )?.ToString() ?? "",
				Layer = index < layers.Length ? (int)layers[index] : 0,
				LightProbeVolume = (int)Kv.Number( o.Get( "m_nLightProbeVolumePrecomputedHandshake" ) ),
				OverlayRenderOrder = (int)Kv.Number( o.Get( "m_nOverlayRenderOrder" ) ),
				LightGroup = (uint)(long)Kv.Number( o.Get( "m_nLightGroup" ) ),
				LightingOrigin = o.Get( "m_vLightingOrigin" ) is object[] { Length: >= 3 } origin && Kv.Vector3( origin ) is var at && Math.Abs( at.X ) < 1e30f ? at : null,
			} );
		}

		var aggregates = new List<WorldAggregate>();
		foreach ( var a in Kv.Objects( root.Get( "m_aggregateSceneObjects" ) ) )
		{
			var bounds = a.Get( "m_vWorldBounds" ) as object[];
			var meshes = Kv.Objects( a.Get( "m_aggregateMeshes" ) ).Select( m =>
			{
				var b = m.Get( "m_vWorldBounds" ) as object[];
				return new AggregateMesh(
					(int)Kv.Number( m.Get( "m_nDrawDescriptorIndex" ) ?? m.Get( "m_nDrawCallIndex" ) ),
					Vector4( m.Get( "m_vTintColor" ), System.Numerics.Vector4.One ),
					Kv.Vector3( b?.ElementAtOrDefault( 0 ) ),
					Kv.Vector3( b?.ElementAtOrDefault( 1 ) ),
					(uint)Kv.Number( m.Get( "m_objectFlags" ) ),
					Kv.Number( m.Get( "m_bHasTransform" ) ) != 0 );
			} ).ToArray();

			aggregates.Add( new WorldAggregate
			{
				Model = a.Get( "m_renderableModel" ) as string ?? "",
				Mins = Kv.Vector3( bounds?.ElementAtOrDefault( 0 ) ),
				Maxs = Kv.Vector3( bounds?.ElementAtOrDefault( 1 ) ),
				Flags = (uint)Kv.Number( a.Get( "m_allFlags" ) ),
				Layer = (int)Kv.Number( a.Get( "m_nLayer" ) ),
				Meshes = meshes,
				FragmentTransforms = (a.Get( "m_fragmentTransforms" ) as object[] ?? []).Select( Transform ).ToArray(),
			} );
		}

		return new WorldNode
		{
			Prefix = prefix,
			Mins = mins,
			Maxs = maxs,
			SceneObjects = objects,
			Aggregates = aggregates,
			LayerNames = Kv.Strings( root.Get( "m_layerNames" ) ),
		};
	}

	/// <summary>A <c>matrix3x4_t</c> stored as three rows of four, or as twelve numbers.</summary>
	static Matrix4x4 Transform( object value ) => value is object[] rows && rows.All( x => x is object[] )
		? Kv.Matrix3x4( rows.SelectMany( x => (object[])x ).ToArray() )
		: Kv.Matrix3x4( value );

	/// <summary>A colour, with all zeroes (what the compiler writes for no tint) as <paramref name="none"/>.</summary>
	static Vector4 Vector4( object value, Vector4 none )
	{
		if ( value is not object[] { Length: >= 4 } v ) return none;
		var colour = new Vector4( (float)Kv.Number( v[0] ), (float)Kv.Number( v[1] ), (float)Kv.Number( v[2] ), (float)Kv.Number( v[3] ) );
		return colour == System.Numerics.Vector4.Zero ? none : colour;
	}
}

/// <summary>
/// A static scene object in a world node: a model, placed.
/// </summary>
public sealed record WorldSceneObject
{
	/// <summary>The model (<c>.vmdl</c>).</summary>
	public string Model { get; init; }

	/// <summary>Object to world, for row vectors (translation in the last row).</summary>
	public Matrix4x4 Transform { get; init; }

	/// <summary>Linear tint, white when the compiler wrote none.</summary>
	public Vector4 Tint { get; init; }

	/// <summary>Where it starts fading out; negative for never.</summary>
	public float FadeStart { get; init; }

	/// <summary>Where it has faded out; zero or less for never.</summary>
	public float FadeEnd { get; init; }

	/// <summary>A fixed LOD, or -1 to choose by screen size.</summary>
	public int LodOverride { get; init; }

	/// <summary>The mesh groups it draws, as written (see <see cref="MeshGroups"/>).</summary>
	public ulong MeshGroupMask { get; init; }

	/// <summary>
	/// The compiler leaves world geometry's mask unset, as 0 or as this fill pattern; any other value is the mask.
	/// </summary>
	public const ulong UnsetMeshGroupMask = 0xD0D0D0D0D0D0D0D0;

	/// <summary>
	/// The mesh groups it draws: <see cref="MeshGroupMask"/>, or <paramref name="modelDefault"/> where none was set. Native's
	/// world node keeps the model's own groups for a 0 mask (<c>MESH_GROUP_NOT_FOUND</c>).
	/// </summary>
	public ulong MeshGroups( ulong modelDefault ) => MeshGroupMask is 0 or UnsetMeshGroupMask ? modelDefault : MeshGroupMask;

	/// <summary>Its material group (skin) by name, or empty.</summary>
	public string Skin { get; init; }

	/// <summary><c>m_nObjectTypeFlags</c> as written: a flag name or a number.</summary>
	public string TypeFlags { get; init; }

	/// <summary>Its layer (<see cref="WorldNode.LayerNames"/>).</summary>
	public int Layer { get; init; }

	/// <summary>
	/// The light probe volume the compiler chose for it (<c>m_nLightProbeVolumePrecomputedHandshake</c>, the number in the
	/// volume's texture names), or 0 for none.
	/// </summary>
	public int LightProbeVolume { get; init; }

	/// <summary><c>m_nOverlayRenderOrder</c>: an overlay's place among the static overlays (<c>RenderOrder</c>), lowest first.</summary>
	public int OverlayRenderOrder { get; init; }

	/// <summary><c>m_nLightGroup</c>: the light group's token, or 0 for none.</summary>
	public uint LightGroup { get; init; }

	/// <summary><c>m_vLightingOrigin</c>: where it's lit from instead of its centre, or null where the compiler wrote none.</summary>
	public Vector3? LightingOrigin { get; init; }

	/// <summary><see cref="TypeFlags"/> as <c>ObjectTypeFlags_t</c> bits (<see cref="ObjectTypeFlags"/>).</summary>
	public ObjectTypeFlags Flags => ParseTypeFlags( TypeFlags );

	/// <summary><c>m_nObjectTypeFlags</c> from its number or its flag names.</summary>
	public static ObjectTypeFlags ParseTypeFlags( string flags )
	{
		if ( string.IsNullOrEmpty( flags ) ) return 0;
		if ( uint.TryParse( flags, out var number ) ) return (ObjectTypeFlags)number;

		uint value = 0;
		foreach ( var name in flags.Split( ['|', ' ', ','], StringSplitOptions.RemoveEmptyEntries ) )
		{
			value |= name switch
			{
				"OBJECT_TYPE_IMAGE_LOD" => 0x1u,
				"OBJECT_TYPE_GEOMETRY_LOD" => 0x2u,
				"OBJECT_TYPE_DECAL" => 0x4u,
				"OBJECT_TYPE_MODEL" => 0x8u,
				"OBJECT_TYPE_BLOCK_LIGHT" => 0x10u,
				"OBJECT_TYPE_NO_SHADOWS" => 0x20u,
				"OBJECT_TYPE_WORLDSPACE_TEXURE_BLEND" => 0x40u,
				"OBJECT_TYPE_DISABLED_IN_LOW_QUALITY" => 0x80u,
				"OBJECT_TYPE_NO_SUN_SHADOWS" => 0x100u,
				"OBJECT_TYPE_RENDER_WITH_DYNAMIC" => 0x200u,
				"OBJECT_TYPE_RENDER_TO_CUBEMAPS" => 0x400u,
				"OBJECT_TYPE_PRECOMPUTED_VISMEMBERS" => 0x4000u,
				_ => 0u,
			};
		}

		return (ObjectTypeFlags)value;
	}
}

/// <summary>A world object's <c>ObjectTypeFlags_t</c>, as the map compiler writes them.</summary>
[Flags]
public enum ObjectTypeFlags : uint
{
#pragma warning disable CS1591
	None = 0,
	ImageLod = 0x1,
	GeometryLod = 0x2,
	Decal = 0x4,
	Model = 0x8,
	BlockLight = 0x10,
	NoShadows = 0x20,
	WorldspaceTextureBlend = 0x40,
	DisabledInLowQuality = 0x80,
	NoSunShadows = 0x100,
	RenderWithDynamic = 0x200,
	RenderToCubemaps = 0x400,
	PrecomputedVisMembers = 0x4000,
#pragma warning restore CS1591
}

/// <summary>
/// An aggregate: one model (usually a world node mesh of one material) whose draw calls are drawn separately, each culled by
/// its own bounds. Draw calls without a transform are in world space.
/// </summary>
public sealed record WorldAggregate
{
	/// <summary>The model.</summary>
	public string Model { get; init; }

	/// <summary>Bounds of every draw.</summary>
	public Vector3 Mins { get; init; }

	/// <summary>Bounds of every draw.</summary>
	public Vector3 Maxs { get; init; }

	/// <summary>Flags every draw has (<c>m_allFlags</c>).</summary>
	public uint Flags { get; init; }

	/// <summary>Its layer.</summary>
	public int Layer { get; init; }

	/// <summary>Its draws.</summary>
	public IReadOnlyList<AggregateMesh> Meshes { get; init; }

	/// <summary>Transforms for the draws that have one, in order; empty when none do.</summary>
	public IReadOnlyList<Matrix4x4> FragmentTransforms { get; init; }
}

/// <summary>
/// One draw of an aggregate: a draw call of its model (numbered across meshes in order), its tint, bounds and flags.
/// </summary>
public readonly record struct AggregateMesh( int DrawCall, Vector4 Tint, Vector3 Mins, Vector3 Maxs, uint Flags, bool HasTransform );
