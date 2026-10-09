using Editor.MapDoc;
using Editor.MapEditor;
using Editor.MeshEditor;
using System;
using System.Text.Json.Nodes;

namespace Editor.Mcp;

internal static partial class HammerTools
{
	[McpTool.ReadOnly( "hammer_list_primitives" )]
	[Description( "The primitive shapes hammer_create_primitive can build - the same ones as Hammer's block tool (box, stairs, cylinder, sphere, doorway...) - with each one's options, their types and defaults." )]
	public static object ListPrimitives()
	{
		return PrimitiveTypes().Select( x =>
		{
			var instance = x.Create<PrimitiveBuilder>();

			return new
			{
				Type = x.Title,
				x.ClassName,
				x.Description,
				instance.Is2D,
				Options = OptionsOf( x ).Select( p => new
				{
					p.Name,
					Type = p.PropertyType.Name,
					Default = p.GetValue( instance )?.ToString(),
					p.Description
				} ).ToArray()
			};
		} ).ToArray();
	}

	[McpTool( "hammer_create_primitive" )]
	[Description( "Build brush geometry fitted to a box, like dragging out Hammer's block tool - a box by default, or any shape hammer_list_primitives lists (stairs, cylinder, sphere...). Rooms are boxes: floor, walls and ceiling, or a 'Hollow' box. The user can undo this. Returns the new mesh node." )]
	public static MapNodeInfo CreatePrimitive(
		[Description( "One corner of the bounds, as 'x,y,z'." )] string mins,
		[Description( "The opposite corner of the bounds, as 'x,y,z'." )] string maxs,
		[Description( "The shape, by name from hammer_list_primitives - 'Box', 'Stairs', 'Cylinder'..." )] string type = "Box",
		[Description( "Material for every face. Empty uses the material active in Hammer's material browser." )] string material = "",
		[Description( "The shape's options as a json object, e.g. {\"NumberOfSteps\": 8} for stairs or {\"Options\": \"Hollow\"} for a room. hammer_list_primitives lists them." )] JsonObject options = null )
	{
		var map = ActiveMap();
		var td = FindPrimitiveType( type );
		var builder = td.Create<PrimitiveBuilder>();
		var materialPath = string.IsNullOrWhiteSpace( material ) ? null : FindMaterial( material );

		// Orienting to the camera makes the same call build different geometry depending on where the
		// user is looking - agents want the same result every time
		if ( OptionsOf( td ).FirstOrDefault( x => x.Name == "AlignToCamera" ) is { } align && !(options?.ContainsKey( "AlignToCamera" ) ?? false) )
			align.SetValue( builder, false );

		ApplyOptions( td, builder, options );

		builder.SetFromBox( BBox.FromPoints( [Vector3.Parse( mins ), Vector3.Parse( maxs )] ) );

		var polygons = new PrimitiveBuilder.PolygonMesh();
		builder.Build( polygons );

		if ( polygons.Faces.Count == 0 )
			throw new Exception( $"That {td.Title} built no faces - check its bounds and options" );

		if ( materialPath is not null )
			polygons.Faces.ForEach( x => x.Material = materialPath );

		var mesh = new MapMesh( map );
		mesh.ConstructFromPolygons( polygons );

		History.MarkUndoPosition( $"Create {td.Title}" );
		History.KeepNew( mesh );

		return Info( mesh );
	}

	[McpTool( "hammer_create_mesh" )]
	[Description( "Build brush geometry from raw polygons - for shapes the primitives don't cover, like ramps, arches or custom trim. Faces index into the vertex list and are wound counter-clockwise seen from the side they face (the right-hand rule gives the outward normal). Vertices shared by faces get welded. The user can undo this. Returns the new mesh node." )]
	public static MapNodeInfo CreateMesh(
		[Description( "World space vertex positions, each 'x,y,z'." )] Vector3[] vertices,
		[Description( "The polygons, each at least 3 vertex indices." )] MeshFace[] faces,
		[Description( "Material for faces that don't set their own. Empty uses the material active in Hammer's material browser." )] string material = "" )
	{
		var map = ActiveMap();

		if ( vertices is null || vertices.Length < 3 )
			throw new Exception( "Give at least 3 vertices" );

		if ( faces is null || faces.Length == 0 )
			throw new Exception( "Give at least one face like {\"indices\": [0, 1, 2]}" );

		var defaultMaterial = string.IsNullOrWhiteSpace( material ) ? null : FindMaterial( material );
		var polygons = new PrimitiveBuilder.PolygonMesh();
		polygons.Vertices.AddRange( vertices );

		for ( int i = 0; i < faces.Length; i++ )
		{
			var indices = faces[i]?.Indices;

			if ( indices is null || indices.Length < 3 )
				throw new Exception( $"faces[{i}] needs at least 3 indices" );

			if ( indices.FirstOrDefault( x => x < 0 || x >= vertices.Length, -1 ) is var bad && bad != -1 )
				throw new Exception( $"faces[{i}] uses vertex {bad}, but there are only {vertices.Length} vertices" );

			var face = polygons.AddFace( indices );
			face.Material = string.IsNullOrWhiteSpace( faces[i].Material ) ? defaultMaterial : FindMaterial( faces[i].Material );
		}

		var mesh = new MapMesh( map );
		mesh.ConstructFromPolygons( polygons );

		History.MarkUndoPosition( "Create Mesh" );
		History.KeepNew( mesh );

		return Info( mesh );
	}

	/// <summary>
	/// One polygon of a hammer_create_mesh mesh.
	/// </summary>
	public class MeshFace
	{
		[Description( "Indices into the vertex list, counter-clockwise seen from the side the face points." )]
		public required int[] Indices { get; set; }

		[Description( "This face's material. Empty uses the mesh's material." )]
		public string Material { get; set; }
	}

	static TypeDescription[] PrimitiveTypes()
	{
		return EditorTypeLibrary.GetTypes<PrimitiveBuilder>()
			.Where( x => !x.IsAbstract && !x.IsInterface )
			.OrderBy( x => x.Title )
			.ToArray();
	}

	static TypeDescription FindPrimitiveType( string name )
	{
		var all = PrimitiveTypes();
		name = name?.Trim() ?? "";

		return all.FirstOrDefault( x => string.Equals( x.Title, name, StringComparison.OrdinalIgnoreCase )
				|| string.Equals( x.ClassName, name, StringComparison.OrdinalIgnoreCase )
				|| string.Equals( x.Name, name, StringComparison.OrdinalIgnoreCase )
				|| string.Equals( x.ClassName, $"{name}Primitive", StringComparison.OrdinalIgnoreCase ) )
			?? throw new Exception( $"No primitive '{name}'. There's {string.Join( ", ", all.Select( x => x.Title ) )}" );
	}

	/// <summary>
	/// A primitive's user facing options - what the block tool's property sheet shows.
	/// </summary>
	static IEnumerable<PropertyDescription> OptionsOf( TypeDescription td )
	{
		return td.Properties.Where( x => !x.IsStatic && x.IsPublic && x.CanWrite && x.IsSetMethodPublic && !x.HasAttribute<HideAttribute>() );
	}

	static void ApplyOptions( TypeDescription td, object target, JsonObject options )
	{
		if ( options is null )
			return;

		var all = OptionsOf( td ).ToArray();

		foreach ( var (key, value) in options )
		{
			var property = all.FirstOrDefault( x => string.Equals( x.Name, key, StringComparison.OrdinalIgnoreCase ) )
				?? throw new Exception( $"{td.Title} has no option '{key}'. It has: {string.Join( ", ", all.Select( x => x.Name ) )}" );

			try
			{
				property.SetValue( target, Json.FromNode( value, property.PropertyType ) );
			}
			catch ( Exception e )
			{
				throw new Exception( $"Couldn't convert '{key}' to {property.PropertyType.Name} - {e.Message}" );
			}
		}
	}

	/// <summary>
	/// A material asset's path, or an error saying how to find one.
	/// </summary>
	static string FindMaterial( string path )
	{
		var asset = FindAsset( path );

		if ( asset.AssetType != AssetType.Material )
			throw new Exception( $"'{path}' is a {asset.AssetType?.FriendlyName}, not a material - asset_search type:vmat finds materials" );

		return asset.Path;
	}
}
