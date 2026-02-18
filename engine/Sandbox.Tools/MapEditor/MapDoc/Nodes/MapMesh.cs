using Editor.MeshEditor;
using NativeEngine;
using NativeMapDoc;
using System;
using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;

namespace Editor.MapDoc;

internal struct TransformOperationScope : IDisposable
{
	private CMapMesh mapMesh;
	public TransformOperationScope( CMapMesh mesh, TransformOperationMode mode, TransformFlags flags )
	{
		mapMesh = mesh;
		mesh.BeginTransformOperation( mode, flags );
	}

	public void Dispose()
	{
		mapMesh.EndTransformOperation();
	}
}

/// <summary>
/// MapMesh is the Hammer map node which represents editable mesh geometry in a Hammer map.
/// This is the map node that is created when using the hammer geometry editing tools.
/// </summary>
[Display( Name = "Mesh" ), Icon( "foundation" )]
public class MapMesh : MapNode
{
	internal CMapMesh meshNative;

	internal MapMesh( HandleCreationData _ ) { }

	public MapMesh( MapDocument mapDocument = null )
	{
		ThreadSafe.AssertIsMainThread();

		// Default to the active map document if none specificed
		mapDocument ??= MapEditor.Hammer.ActiveMap;

		Assert.IsValid( mapDocument );

		using ( var h = IHandle.MakeNextHandle( this ) )
		{
			mapDocument.native.CreateEmptyMesh( true ); // meh
		}
	}

	internal override void OnNativeInit( CMapNode ptr )
	{
		base.OnNativeInit( ptr );

		meshNative = (CMapMesh)ptr;
	}

	internal override void OnNativeDestroy()
	{
		base.OnNativeDestroy();

		meshNative = default;
	}

	/// <summary>
	/// Assigns the specified material to the entire mesh
	/// </summary>
	public void SetMaterial( Material material )
	{
		ArgumentNullException.ThrowIfNull( material );
		meshNative.AssignMaterialToMesh( material.Name );
	}

	/// <summary>
	/// Constructs the mesh from the given <see cref="PrimitiveBuilder.PolygonMesh"/> builder.
	/// </summary>
	public unsafe void ConstructFromPolygons( PrimitiveBuilder.PolygonMesh mesh )
	{
		ArgumentNullException.ThrowIfNull( mesh );

		// Construct data streams from our lists
		var vertexPositions = mesh.Vertices.Select( v => v ).ToList();
		var vertexTexCoords = mesh.Vertices.Select( v => Vector2.Zero ).ToList();
		var faceIndices = new List<int>( mesh.Faces.Sum( f => f.Indices.Count ) ); // Array of indices specifying which vertices are used by each face
		var faceVertexCounts = new List<int>( mesh.Faces.Count ); // Number of vertices used by each face
		var faceMaterials = new List<IntPtr>( mesh.Faces.Count );

		foreach ( var face in mesh.Faces )
		{
			faceIndices.AddRange( face.Indices );
			faceVertexCounts.Add( face.Indices.Count );

			var material = (!string.IsNullOrEmpty( face.Material ) ? Material.Load( face.Material ) : null) ?? MapEditor.Hammer.CurrentMaterial ?? Material.Load( "materials/dev/reflectivity_30.vmat" ); // didn't specify a material? assign hammer current material
			faceMaterials.Add( material.native );
		}

		fixed ( Vector3* vertexPositionPtr = CollectionsMarshal.AsSpan( vertexPositions ) )
		fixed ( int* faceIndicesPtr = CollectionsMarshal.AsSpan( faceIndices ) )
		fixed ( Vector2* vertexTexCoordsPtr = CollectionsMarshal.AsSpan( vertexTexCoords ) )
		fixed ( int* faceVertexCountsPtr = CollectionsMarshal.AsSpan( faceVertexCounts ) )
		fixed ( IntPtr* faceMaterialsPtr = CollectionsMarshal.AsSpan( faceMaterials ) )
		{
			meshNative.ConstructFromData(
				mesh.Vertices.Count, (IntPtr)vertexPositionPtr, (IntPtr)vertexTexCoordsPtr,
				faceIndices.Count, (IntPtr)faceIndicesPtr,
				mesh.Faces.Count, (IntPtr)faceVertexCountsPtr, (IntPtr)faceMaterialsPtr,
				true, 0.001f
			);
		}
	}

	/// <summary>
	/// Get all material assets used on this mesh
	/// </summary>
	public IEnumerable<Asset> GetFaceMaterialAssets()
	{
		var strs = CUtlVectorString.Create( 16, 16 );
		meshNative.GetFaceMaterials( strs );

		List<Asset> assets = new();

		for ( int i = 0; i < strs.Count(); i++ )
		{
			var asset = AssetSystem.FindByPath( strs.Element( i ) );
			if ( asset == null ) continue;

			assets.Add( asset );
		}

		strs.DeleteThis();

		return assets.Distinct();
	}

	internal IDisposable TransformOperation( TransformOperationMode mode, TransformFlags flags ) => new TransformOperationScope( meshNative, mode, flags );
	internal void Transform( Matrix matrix, TransformFlags flags ) => meshNative.Transform( matrix, flags );
}
