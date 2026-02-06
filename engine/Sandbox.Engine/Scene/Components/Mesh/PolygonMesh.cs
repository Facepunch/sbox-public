using HalfEdgeMesh;
using System.Runtime.InteropServices;

namespace Sandbox;

/// <summary>
/// An editable mesh made up of polygons, triangulated into a model
/// </summary>
[Expose]
public sealed partial class PolygonMesh : IJsonConvert
{
	private HalfEdgeMesh.Mesh Topology { get; init; } = new();

	private readonly List<FaceHandle> _triangleFaces = new();
	private readonly List<int> _meshIndices = new();
	private readonly List<Vector3> _meshVertices = new();
	private readonly List<byte> _meshTriangleMaterials = new();
	private readonly Dictionary<FaceHandle, FaceMesh> _meshFaces = new();
	private readonly Dictionary<int, Material> _materialsById = new();
	private readonly Dictionary<string, int> _materialIdsByName = new();
	private int _materialId = 0;
	private float _smoothingThreshold;

	[Expose]
	public enum EdgeSmoothMode
	{
		Default,
		Hard,
		Soft
	}

	private struct FaceMesh
	{
		public int VertexStart;
		public int IndexStart;
		public int VertexCount;
		public int IndexCount;
	}

	private class Submesh
	{
		public Mesh Mesh { get; set; }
		public List<MeshVertex> Vertices { get; init; } = new();
		public List<int> Indices { get; init; } = new();
		public List<float> UvDensity { get; set; } = new();
		public Material Material { get; set; }
		public int Index { get; set; }
	}

	private struct FaceData
	{
		public Vector3 TextureUAxis { get; set; }
		public Vector3 TextureVAxis { get; set; }
		public Vector2 TextureScale { get; set; }
		public Vector2 TextureOffset { get; set; }
		public int MaterialId { get; set; }
	}

	private Material DefaultMaterial = Material.Load( "materials/dev/reflectivity_30.vmat" );
	private Vector2 DefaultTextureSize => CalculateTextureSize( DefaultMaterial );

	private VertexData<Vector3> Positions { get; init; }
	private HalfEdgeData<Color32> Blends { get; init; }
	private HalfEdgeData<Color32> Colors { get; init; }
	private HalfEdgeData<Vector2> TextureCoord { get; init; }
	private FaceData<Vector3> TextureUAxis { get; init; }
	private FaceData<Vector3> TextureVAxis { get; init; }
	private FaceData<Vector2> TextureScale { get; init; }
	private FaceData<Vector2> TextureOffset { get; init; }
	private FaceData<int> MaterialIndex { get; init; }
	private HalfEdgeData<bool> EdgeSmoothing { get; init; }
	private HalfEdgeData<int> EdgeFlags { get; init; }

	private FaceData<Vector3> TextureOriginUnused { get; init; }
	private FaceData<Rotation> TextureRotationUnused { get; init; }
	private FaceData<float> TextureAngleUnused { get; init; }

	public VertexHandle VertexHandleFromIndex( int index ) => new( index, Topology );
	public HalfEdgeHandle HalfEdgeHandleFromIndex( int index ) => new( index, Topology );
	public FaceHandle FaceHandleFromIndex( int index ) => new( index, Topology );

	public void MergeMesh( PolygonMesh sourceMesh, Transform transform,
		out Dictionary<VertexHandle, VertexHandle> newVertices,
		out Dictionary<HalfEdgeHandle, HalfEdgeHandle> newHalfEdges,
		out Dictionary<FaceHandle, FaceHandle> newFaces )
	{
		Topology.AppendComponentsFromMesh( sourceMesh.Topology,
			out newVertices, out newHalfEdges, out newFaces );

		foreach ( var pair in newVertices )
		{
			Positions[pair.Value] = transform.PointToWorld( sourceMesh.Positions[pair.Key] );
		}

		foreach ( var pair in newHalfEdges )
		{
			TextureCoord[pair.Value] = sourceMesh.TextureCoord[pair.Key];
			EdgeSmoothing[pair.Value] = sourceMesh.EdgeSmoothing[pair.Key];
		}

		foreach ( var pair in newFaces )
		{
			TextureUAxis[pair.Value] = sourceMesh.TextureUAxis[pair.Key];
			TextureVAxis[pair.Value] = sourceMesh.TextureVAxis[pair.Key];
			TextureScale[pair.Value] = sourceMesh.TextureScale[pair.Key];
			TextureOffset[pair.Value] = sourceMesh.TextureOffset[pair.Key];

			SetFaceMaterial( pair.Value, sourceMesh.GetFaceMaterial( pair.Key ) );
		}

		IsDirty = true;
	}

	internal int GetFaceMaterialIndex( FaceHandle hFace )
	{
		return MaterialIndex[hFace];
	}

	private void SetFaceMaterialIndex( FaceHandle hFace, int materialIndex )
	{
		MaterialIndex[hFace] = materialIndex;
	}

	internal IEnumerable<Material> Materials => Enumerable.Range( 0, _materialsById.Count )
				.Select( x => _materialsById[x] );

	internal IEnumerable<Vector3> GetFaceVertexNormals()
	{
		foreach ( var hFace in Topology.FaceHandles )
		{
			ComputeFaceNormal( hFace, out var normal );
			var vertexCount = Topology.ComputeNumEdgesInFace( hFace );
			for ( var i = 0; i < vertexCount; ++i )
				yield return normal;
		}
	}

	internal IEnumerable<Vector2> GetFaceVertexTexCoords()
	{
		foreach ( var hFace in Topology.FaceHandles )
		{
			GetFaceVerticesConnectedToFace( hFace, out var hEdges );
			foreach ( var hEdge in hEdges )
				yield return TextureCoord[hEdge];
		}
	}

	/// <summary>
	/// Has there been changes to the mesh that need rebuilding?
	/// </summary>
	public bool IsDirty { get; internal set; }

	/// <summary>
	/// Has there been changes to the vertex data?
	/// </summary>
	internal bool IsVertexDataDirty => _dirtyHalfEdges.Count > 0;

	private Transform _transform = Transform.Zero;

	/// <summary>
	/// Where is the mesh in worldspace.
	/// </summary>
	public Transform Transform
	{
		get => _transform;
		set
		{
			if ( _transform == value )
				return;

			_transform = value;

			ComputeFaceTextureParametersFromCoordinates();

			IsDirty = true;
		}
	}

	/// <summary>
	/// Set transform without computing texture parameters from coordinates.
	/// </summary>
	public void SetTransform( Transform transform )
	{
		_transform = transform;
	}

	public PolygonMesh()
	{
		Positions = Topology.CreateVertexData<Vector3>( nameof( Positions ) );
		Blends = Topology.CreateHalfEdgeData<Color32>( nameof( Blends ) );
		Colors = Topology.CreateHalfEdgeData<Color32>( nameof( Colors ) );
		TextureCoord = Topology.CreateHalfEdgeData<Vector2>( nameof( TextureCoord ) );
		TextureUAxis = Topology.CreateFaceData<Vector3>( nameof( TextureUAxis ) );
		TextureVAxis = Topology.CreateFaceData<Vector3>( nameof( TextureVAxis ) );
		TextureScale = Topology.CreateFaceData<Vector2>( nameof( TextureScale ) );
		TextureOffset = Topology.CreateFaceData<Vector2>( nameof( TextureOffset ) );
		MaterialIndex = Topology.CreateFaceData<int>( nameof( MaterialIndex ) );
		EdgeSmoothing = Topology.CreateHalfEdgeData<bool>( nameof( EdgeSmoothing ) );
		EdgeFlags = Topology.CreateHalfEdgeData<int>( nameof( EdgeFlags ) );

		TextureOriginUnused = Topology.CreateFaceData<Vector3>( nameof( TextureOriginUnused ) );
		TextureRotationUnused = Topology.CreateFaceData<Rotation>( nameof( TextureRotationUnused ) );
		TextureAngleUnused = Topology.CreateFaceData<float>( nameof( TextureAngleUnused ) );
	}

	/// <summary>
	/// Called on serialize, goes through and cleans up any unused materials, and remaps material indices
	/// </summary>
	private void CleanupUnusedMaterials()
	{
		var usedIds = new HashSet<int>();
		foreach ( var face in Topology.FaceHandles )
		{
			int id = MaterialIndex[face];
			if ( id >= 0 ) usedIds.Add( id );
		}

		if ( usedIds.Count == _materialsById.Count )
			return;

		var remap = new int[_materialsById.Count];
		var newMaterials = new Dictionary<int, Material>( usedIds.Count );

		int newId = 0;
		foreach ( var oldId in usedIds )
		{
			remap[oldId] = newId;
			newMaterials[newId] = _materialsById[oldId];
			newId++;
		}

		foreach ( var face in Topology.FaceHandles )
		{
			int oldId = MaterialIndex[face];
			if ( oldId >= 0 )
				MaterialIndex[face] = remap[oldId];
		}

		_materialsById.Clear();
		_materialIdsByName.Clear();
		foreach ( var kv in newMaterials )
		{
			_materialsById[kv.Key] = kv.Value;
			_materialIdsByName[kv.Value.Name] = kv.Key;
		}

		_materialId = newId;
		IsDirty = true;
	}

	/// <summary>
	/// All of the vertex handles being used
	/// </summary>
	public IEnumerable<VertexHandle> VertexHandles => Topology.VertexHandles;

	/// <summary>
	/// All of the face handles being used
	/// </summary>
	public IEnumerable<FaceHandle> FaceHandles => Topology.FaceHandles;

	/// <summary>
	/// All of the half edge handles being used
	/// </summary>
	public IEnumerable<HalfEdgeHandle> HalfEdgeHandles => Topology.HalfEdgeHandles;

	/// <summary>
	/// Add a vertex to the topology
	/// </summary>
	public VertexHandle AddVertex( Vector3 position )
	{
		var hVertex = Topology.AddVertex();
		Positions[hVertex] = position;

		IsDirty = true;

		return hVertex;
	}

	/// <summary>
	/// Add multiple vertices to the topology
	/// </summary>
	public VertexHandle[] AddVertices( params Vector3[] positions )
	{
		if ( positions.Length <= 0 )
			return default;

		var hVertices = Topology.AddVertices( positions.Length ).ToArray();
		for ( int i = 0; i < positions.Length; i++ )
			Positions[hVertices[i]] = positions[i];

		IsDirty = true;

		return hVertices;
	}

	/// <summary>
	/// Connect these vertices to make a face
	/// </summary>
	public FaceHandle AddFace( params VertexHandle[] hVertices )
	{
		var hFace = Topology.AddFace( hVertices );
		if ( !hFace.IsValid )
			return hFace;

		MaterialIndex[hFace] = -1;

		IsDirty = true;

		return hFace;
	}

	/// <summary>
	/// Calculate bounds of all vertices
	/// </summary>
	public BBox CalculateBounds()
	{
		return BBox.FromPoints( GetVertexPositions() );
	}

	/// <summary>
	/// Calculate bounds of all transformed vertices
	/// </summary>
	public BBox CalculateBounds( Transform transform )
	{
		return BBox.FromPoints( GetVertexPositions().Select( x => transform.PointToWorld( x ) ) );
	}

	/// <summary>
	/// Scale all vertices
	/// </summary>
	public void Scale( Vector3 scale )
	{
		foreach ( var hVertex in Topology.VertexHandles )
		{
			Positions[hVertex] = Positions[hVertex] * scale;
		}

		ComputeFaceTextureCoordinatesFromParameters();

		IsDirty = true;
	}

	/// <summary>
	/// Assign a material to a face
	/// </summary>
	public void SetFaceMaterial( FaceHandle hFace, Material material )
	{
		var id = AddMaterial( material );
		if ( id == MaterialIndex[hFace] )
			return;

		MaterialIndex[hFace] = id;

		IsDirty = true;
	}

	/// <summary>
	/// Assign a material to a face
	/// </summary>
	public void SetFaceMaterial( FaceHandle hFace, string material )
	{
		SetFaceMaterial( hFace, Material.Load( material ) );
	}

	/// <summary>
	/// Get a material a face is using
	/// </summary>
	public Material GetFaceMaterial( FaceHandle hFace )
	{
		return GetMaterial( MaterialIndex[hFace] );
	}

	/// <summary>
	/// Get the smoothing of this edge
	/// </summary>
	public EdgeSmoothMode GetEdgeSmoothing( HalfEdgeHandle hEdge )
	{
		return (EdgeSmoothMode)EdgeFlags[hEdge];
	}

	/// <summary>
	/// Set the smoothing of this edge
	/// </summary>
	public void SetEdgeSmoothing( HalfEdgeHandle hEdge, EdgeSmoothMode mode )
	{
		EdgeFlags[hEdge] = (int)mode;
		IsDirty = true;
	}

	public void SetSmoothingAngle( float smoothingAngle )
	{
		var threshold = MathF.Cos( MathF.Min( smoothingAngle, 180.0f ).DegreeToRadian() );
		if ( threshold != _smoothingThreshold )
		{
			_smoothingThreshold = threshold;
			IsDirty = true;
		}
	}

	/// <summary>
	/// Convert a triangle index to a face handle
	/// </summary>
	public FaceHandle TriangleToFace( int triangle )
	{
		if ( triangle < 0 || triangle >= _triangleFaces.Count )
			return FaceHandle.Invalid;

		var hFace = _triangleFaces[triangle];
		if ( !hFace.IsValid )
			return FaceHandle.Invalid;

		return hFace;
	}

	/// <summary>
	/// Extrude multiple faces along an offset
	/// </summary>
	public void ExtrudeFaces( FaceHandle[] faces, out List<FaceHandle> newFaces, out List<FaceHandle> connectingFaces, Vector3 offset = default )
	{
		BevelFaces( faces, out newFaces, out connectingFaces, out _, true, offset );
	}

	/// <summary>
	/// Detatch multiple faces
	/// </summary>
	public void DetachFaces( FaceHandle[] faces, out List<FaceHandle> newFaces )
	{
		BevelFaces( faces, out newFaces, out _, out _, false );
	}

	private void BevelFaces( FaceHandle[] faces, out List<FaceHandle> newFaces, out List<FaceHandle> connectingFaces, out List<HalfEdgeHandle> connectingEdges, bool createConnectingFaces, Vector3 offset = default )
	{
		connectingEdges = [];

		var numFaces = faces.Length;
		var faceDataIndices = new int[numFaces];

		var numTotalFaceDataSamples = 0;
		for ( var i = 0; i < numFaces; ++i )
		{
			faceDataIndices[i] = numTotalFaceDataSamples;
			numTotalFaceDataSamples += Topology.ComputeNumEdgesInFace( faces[i] );
		}

		var vertexPositions = new Vector3[numTotalFaceDataSamples];
		var faceData = new FaceData[numFaces];

		for ( var i = 0; i < numFaces; ++i )
		{
			var hFace = faces[i];
			faceData[i] = new FaceData
			{
				TextureUAxis = TextureUAxis[hFace],
				TextureVAxis = TextureVAxis[hFace],
				TextureScale = TextureScale[hFace],
				TextureOffset = TextureOffset[hFace],
				MaterialId = MaterialIndex[hFace],
			};

			var hStartFaceVertex = Topology.GetFirstEdgeInFaceLoop( hFace );
			var hCurrentFaceVertex = hStartFaceVertex;
			var vertexIndex = 0;
			do
			{
				var hCurrentVertex = Topology.GetEndVertexConnectedToEdge( hCurrentFaceVertex );
				var nDstDataIndex = faceDataIndices[i] + vertexIndex;
				vertexPositions[nDstDataIndex] = GetVertexPosition( hCurrentVertex );
				hCurrentFaceVertex = Topology.GetNextEdgeInFaceLoop( hCurrentFaceVertex );

				++vertexIndex;
			}
			while ( hCurrentFaceVertex != hStartFaceVertex );
		}

		if ( !Topology.BevelFaces( faces, faces.Length, createConnectingFaces,
			out newFaces,
			out connectingFaces,
			out var connectingTargetFaces,
			out var connectingOriginFaces ) )
		{
			return;
		}

		for ( var i = 0; i < numFaces; ++i )
		{
			var hNewFace = newFaces[i];
			if ( !hNewFace.IsValid )
				continue;

			var newFaceData = faceData[i];
			TextureUAxis[hNewFace] = newFaceData.TextureUAxis;
			TextureVAxis[hNewFace] = newFaceData.TextureVAxis;
			TextureScale[hNewFace] = newFaceData.TextureScale;
			TextureOffset[hNewFace] = newFaceData.TextureOffset;
			MaterialIndex[hNewFace] = newFaceData.MaterialId;

			var vertexIndex = 0;
			var hStartFaceVertex = Topology.GetFirstEdgeInFaceLoop( hNewFace );
			var hCurrentFaceVertex = hStartFaceVertex;

			do
			{
				var hCurrentVertex = Topology.GetEndVertexConnectedToEdge( hCurrentFaceVertex );
				var nSrcDataIndex = faceDataIndices[i] + vertexIndex;
				SetVertexPosition( hCurrentVertex, vertexPositions[nSrcDataIndex] + offset );
				hCurrentFaceVertex = Topology.GetNextEdgeInFaceLoop( hCurrentFaceVertex );

				++vertexIndex;
			}
			while ( hCurrentFaceVertex != hStartFaceVertex );
		}

		ComputeFaceTextureCoordinatesFromParameters( newFaces );

		var numConnectingFaces = connectingFaces.Count;
		Assert.True( connectingOriginFaces.Count == numConnectingFaces );

		for ( var i = 0; i < numConnectingFaces; ++i )
		{
			var hConnectingFace = connectingFaces[i];
			var hOriginFace = connectingOriginFaces[i];

			if ( !hOriginFace.IsValid )
				hOriginFace = connectingTargetFaces[i];

			SetFaceMaterialIndex( hConnectingFace, GetFaceMaterialIndex( hOriginFace ) );
			TextureAlignToGrid( Transform, hConnectingFace );
		}

		connectingEdges.EnsureCapacity( numConnectingFaces );
		for ( int iFace = 0; iFace < numConnectingFaces; ++iFace )
		{
			var hConnectingFace = connectingFaces[iFace];
			var hTargetFace = connectingTargetFaces[iFace];
			var hTargetEdge = FindEdgeConnectingFaces( hConnectingFace, hTargetFace );
			var hConnectingEdge = FindOppositeEdgeInFace( hConnectingFace, hTargetEdge );
			connectingEdges.Add( hConnectingEdge );
		}

		IsDirty = true;
	}

	void OffsetFacesAlongNormal( List<FaceHandle> faces, float flOffset )
	{
		var faceCount = faces.Count;

		Topology.FindVerticesConnectedToFaces( faces, faceCount, out var connectedVertices );

		var vertexToIndex = new Dictionary<VertexHandle, int>( connectedVertices.Length );
		for ( var i = 0; i < connectedVertices.Length; i++ )
			vertexToIndex[connectedVertices[i]] = i;

		var facePlanes = new Plane[faceCount];
		for ( var i = 0; i < faceCount; ++i )
		{
			GetFacePlane( faces[i], Transform.Zero, out var plane );
			plane.Distance += flOffset;
			facePlanes[i] = plane;
		}

		var offsetPositions = new Vector3[connectedVertices.Length];
		for ( var i = 0; i < connectedVertices.Length; i++ )
			offsetPositions[i] = GetVertexPosition( connectedVertices[i] );

		for ( var i = 0; i < faceCount; ++i )
		{
			var hFace = faces[i];
			var hStart = GetFirstVertexInFace( hFace );
			var hCurrent = hStart;

			do
			{
				var hVertex = GetVertexConnectedToFaceVertex( hCurrent );
				offsetPositions[vertexToIndex[hVertex]] += facePlanes[i].Normal * flOffset;
				hCurrent = GetNextVertexInFace( hCurrent );
			}
			while ( hCurrent != hStart );
		}

		for ( var i = 0; i < faceCount; ++i )
		{
			var hFace = faces[i];
			var hStart = GetFirstVertexInFace( hFace );
			var hCurrent = hStart;

			do
			{
				var hVertex = GetVertexConnectedToFaceVertex( hCurrent );
				var index = vertexToIndex[hVertex];
				var original = GetVertexPosition( hVertex );
				var offset = offsetPositions[index];
				var intersection = facePlanes[i].IntersectLine( original, offset );
				if ( intersection.HasValue )
				{
					offsetPositions[index] = intersection.Value;
				}

				hCurrent = GetNextVertexInFace( hCurrent );
			}
			while ( hCurrent != hStart );
		}

		for ( int i = 0; i < connectedVertices.Length; ++i )
		{
			SetVertexPosition( connectedVertices[i], offsetPositions[i] );
		}
	}

	public bool ExtendEdges( IReadOnlyList<HalfEdgeHandle> edges, float amount, out List<HalfEdgeHandle> newEdges, out List<FaceHandle> newFaces )
	{
		newFaces = new();

		if ( !Topology.ExtendEdges( edges, edges.Count, out newEdges, out var originalEdges, out _, out _ ) )
			return false;

		if ( originalEdges.Count == newEdges.Count )
		{
			int nNumEdges = newEdges.Count;
			newFaces = new List<FaceHandle>( nNumEdges );

			for ( int iEdge = 0; iEdge < nNumEdges; ++iEdge )
			{
				var hNewEdge = newEdges[iEdge];
				var hOriginalEdge = originalEdges[iEdge];

				Topology.GetFacesConnectedToFullEdge( hNewEdge, out var hFaceA, out var hFaceB );
				var hNewFace = (hFaceA == FaceHandle.Invalid) ? hFaceB : hFaceA;

				GetVerticesConnectedToEdge( hOriginalEdge, out var hVertexA, out var hVertexB );

				var vVertexPositionA = Vector3.Zero;
				var vVertexPositionB = Vector3.Zero;
				var hOriginalFace = FaceHandle.Invalid;

				hFaceA = Topology.FindFaceWithEdgeConnectingVertices( hVertexA, hVertexB );
				hFaceB = Topology.FindFaceWithEdgeConnectingVertices( hVertexB, hVertexA );
				if ( hFaceA == hNewFace )
				{
					vVertexPositionA = GetVertexPosition( hVertexA );
					vVertexPositionB = GetVertexPosition( hVertexB );
					hOriginalFace = hFaceB;
				}
				else if ( hFaceB == hNewFace )
				{
					vVertexPositionA = GetVertexPosition( hVertexB );
					vVertexPositionB = GetVertexPosition( hVertexA );
					hOriginalFace = hFaceA;
				}

				Assert.True( hOriginalFace != FaceHandle.Invalid );
				if ( hOriginalFace != FaceHandle.Invalid )
				{
					// Compute the position of the new edge base on the orginal face
					GetFacePlane( hOriginalFace, Transform.Zero, out var facePlane );

					var vEdgeDirection = vVertexPositionB - vVertexPositionA;
					vEdgeDirection = vEdgeDirection.Normal;

					var vExtendDirection = facePlane.Normal.Cross( vEdgeDirection );

					GetVerticesConnectedToEdge( hNewEdge, out var hNewVertexA, out var hNewVertexB );

					if ( Topology.FindFaceWithEdgeConnectingVertices( hNewVertexB, hNewVertexA ) != hNewFace )
					{
						(hNewVertexB, hNewVertexA) = (hNewVertexA, hNewVertexB);
						Assert.True( Topology.FindFaceWithEdgeConnectingVertices( hNewVertexB, hNewVertexA ) == hNewFace );
					}

					var vNewPositionA = GetVertexPosition( hNewVertexA );
					var vNewPositionB = GetVertexPosition( hNewVertexB );

					var vEdgeA = vNewPositionA - vVertexPositionA;
					var vEdgeB = vNewPositionB - vVertexPositionB;

					vNewPositionA += (vExtendDirection * vEdgeA.Dot( vExtendDirection )) + (vExtendDirection * amount);
					vNewPositionB += (vExtendDirection * vEdgeB.Dot( vExtendDirection )) + (vExtendDirection * amount);

					SetVertexPosition( hNewVertexA, vNewPositionA );
					SetVertexPosition( hNewVertexB, vNewPositionB );

					TextureUAxis[hNewFace] = TextureUAxis[hOriginalFace];
					TextureVAxis[hNewFace] = TextureVAxis[hOriginalFace];
					TextureScale[hNewFace] = TextureScale[hOriginalFace];
					TextureOffset[hNewFace] = TextureOffset[hOriginalFace];
					MaterialIndex[hNewFace] = MaterialIndex[hOriginalFace];

					newFaces.Add( hNewFace );
				}
			}

			ComputeFaceTextureCoordinatesFromParameters( newFaces );
		}

		IsDirty = true;

		return true;
	}

	private bool DissolveEdge( HalfEdgeHandle hFullEdge, out FaceHandle hOutFace )
	{
		if ( !Topology.DissolveEdge( hFullEdge, out hOutFace ) )
			return false;

		return true;
	}

	private void GetEdgeVertexPositions( HalfEdgeHandle hEdge, out Vector3 pOutVertexA, out Vector3 pOutVertexB )
	{
		if ( !hEdge.IsValid )
		{
			pOutVertexA = default;
			pOutVertexB = default;

			return;
		}

		Topology.GetVerticesConnectedToFullEdge( hEdge, out var hVertexA, out var hVertexB );
		pOutVertexA = GetVertexPosition( hVertexA );
		pOutVertexB = GetVertexPosition( hVertexB );
	}

	public bool AreEdgesCoLinear( HalfEdgeHandle hEdgeA, HalfEdgeHandle hEdgeB, float flAngleToleranceInDegrees )
	{
		float flTolerance = MathF.Cos( MathF.Min( flAngleToleranceInDegrees, 180.0f ).DegreeToRadian() );

		if ( (hEdgeA == HalfEdgeHandle.Invalid) || (hEdgeB == HalfEdgeHandle.Invalid) )
			return false;

		GetEdgeVertexPositions( hEdgeA, out var vPositionA1, out var vPositionA2 );
		GetEdgeVertexPositions( hEdgeB, out var vPositionB1, out var vPositionB2 );

		var vEdgeA = vPositionA2 - vPositionA1;
		vEdgeA = vEdgeA.Normal;

		var vEdgeB = vPositionB2 - vPositionB1;
		vEdgeB = vEdgeB.Normal;

		float flCosAngle = MathF.Abs( vEdgeA.Dot( vEdgeB ) );
		return flCosAngle > flTolerance;
	}

	public void CombineFaces( IReadOnlyList<FaceHandle> faces )
	{
		FindEdgesConnectedToFaces( faces, faces.Count, out var connectedEdges, out var edgeFaceCounts );

		connectedEdges = connectedEdges
			.Where( ( edge, i ) => edgeFaceCounts[i] >= 2 )
			.ToArray();

		DissolveEdges( connectedEdges, true, DissolveRemoveVertexCondition.Colinear );

		IsDirty = true;
	}

	public void DissolveEdge( HalfEdgeHandle edge )
	{
		Topology.DissolveEdge( edge, out _ );

		IsDirty = true;
	}

	public void DissolveEdges( IReadOnlyList<HalfEdgeHandle> edges, bool bFaceMustBePlanar, DissolveRemoveVertexCondition removeCondition )
	{
		const float flColinearTolerance = 5.0f; // Edges may be at an angle of up to this many degrees and still be considered co-linear
		const float flPlanarTolerance = 0.01f;

		var nNumEdges = edges.Count;

		var verticesToRemove = new List<VertexHandle>( nNumEdges );
		var combinedFaces = new List<FaceHandle>( nNumEdges );

		for ( var iEdge = 0; iEdge < nNumEdges; ++iEdge )
		{
			var hEdge = edges[iEdge];

			// Get the two faces connected to the edge, if the faces are not in the same plane then any two 
			// edge vertices will left behind will be removed to prevent the creation of a non-planar polygon.
			Topology.GetFacesConnectedToFullEdge( hEdge, out var hFaceA, out var hFaceB );
			if ( (hFaceA == FaceHandle.Invalid) || (hFaceB == FaceHandle.Invalid) )
				continue;

			ComputeFaceNormal( hFaceA, out var planeA );
			ComputeFaceNormal( hFaceB, out var planeB );

			float flFaceAngle = planeA.Normal.Dot( planeB.Normal );
			if ( bFaceMustBePlanar && (flFaceAngle < (1.0f - flPlanarTolerance)) )
				continue;

			// Get the vertices connected to the edge
			Topology.GetVerticesConnectedToFullEdge( hEdge, out var hVertexA, out var hVertexB );

			// Dissolve the edge
			DissolveEdge( hEdge, out var hFace );

			// Determine if the vertices at the ends of the edge should be removed. A vertex should be 
			// removed if after the edge is dissolved it has only 2 edges connected to it and it passes
			// the specified removal criteria. Note the vertices are not removed here, but are placed
			// in a list of vertices to be removed, this is because removing the vertices might result
			// in removing one of the edges in the list of edges to be dissolved.

			if ( ShouldDissolveRemoveVertex( hVertexA, removeCondition, flColinearTolerance ) )
			{
				verticesToRemove.Add( hVertexA );
			}

			if ( ShouldDissolveRemoveVertex( hVertexB, removeCondition, flColinearTolerance ) )
			{
				verticesToRemove.Add( hVertexB );
			}

			combinedFaces.Add( hFace );
		}

		// Now that all of the edges have been dissolved remove all of the vertices that were
		// determined should be removed while dissolving the edges.
		int nNumVerticesToRemove = verticesToRemove.Count;
		for ( int iVertex = 0; iVertex < nNumVerticesToRemove; ++iVertex )
		{
			Topology.RemoveVertex( verticesToRemove[iVertex], true );
		}

		// Make sure there are no remaining co-linear edges in the face, this may happen if removing
		// the edge cause there to be a loose edge in the face that is subsequently removed.
		if ( removeCondition != DissolveRemoveVertexCondition.None )
		{
			int nNumFaces = combinedFaces.Count;
			for ( int iFace = 0; iFace < nNumFaces; ++iFace )
			{
				RemoveVerticesFromColinearEdgesInFace( combinedFaces[iFace], flColinearTolerance );
			}
		}

		IsDirty = true;
	}

	public bool ComputeClosestPointOnEdge( VertexHandle hVertexA, VertexHandle hVertexB, Vector3 vTargetPoint, out float pOutBaseEdgeParam )
	{
		pOutBaseEdgeParam = 0.0f;
		var hEdge = FindEdgeConnectingVertices( hVertexA, hVertexB );
		if ( hEdge == HalfEdgeHandle.Invalid )
			return false;

		var vEdgePositions = new Vector3[2];
		vEdgePositions[0] = GetVertexPosition( hVertexA );
		vEdgePositions[1] = GetVertexPosition( hVertexB );
		int nNumPositions = vEdgePositions.Length;

		float flEdgeLength = 0.0f;
		for ( int iPos = 1; iPos < nNumPositions; ++iPos )
		{
			flEdgeLength += vEdgePositions[iPos - 1].Distance( vEdgePositions[iPos] );
		}

		Vector3 vClosestPointOnEdge = Vector3.Zero;
		float flClosestPointParam = 0.0f;
		float flMinDistanceSqr = float.MaxValue;
		float flClosestSegmentParam = 0.0f;
		float flBaseEdgeParam = 0.0f;
		int nClosestSegment = -1;

		float flSegmentStart = 0.0f;
		for ( int iPos = 1; iPos < nNumPositions; ++iPos )
		{
			var vEdgePosA = vEdgePositions[iPos - 1];
			var vEdgePosB = vEdgePositions[iPos];

			var vSegment = vEdgePosB - vEdgePosA;
			float flSegmentLength = vSegment.Length;
			float flSegmentEnd = flSegmentStart + flSegmentLength;

			CalcClosestPointOnLineSegment( vTargetPoint, vEdgePosA, vEdgePosB, out var vClosestPointOnSegment, out var flSegmentParam );

			float flDistSqr = vClosestPointOnSegment.DistanceSquared( vTargetPoint );
			if ( flDistSqr < flMinDistanceSqr )
			{
				flMinDistanceSqr = flDistSqr;
				vClosestPointOnEdge = vClosestPointOnSegment;
				flClosestSegmentParam = flSegmentParam;
				nClosestSegment = iPos - 1;
				float flPointDistance = flSegmentStart + flSegmentParam * flSegmentLength;
				flClosestPointParam = flPointDistance / flEdgeLength;

				flBaseEdgeParam = MathX.Lerp( (iPos - 1) / (float)(nNumPositions - 1), iPos / (float)(nNumPositions - 1), flSegmentParam );
			}

			flSegmentStart = flSegmentEnd;
		}

		Assert.True( nClosestSegment >= 0 );
		if ( nClosestSegment < 0 )
			return false;

		pOutBaseEdgeParam = flBaseEdgeParam;

		return true;
	}

	private void RemoveVerticesFromColinearEdgesInFace( FaceHandle hFace, float flColinearAngleTolerance )
	{
		// Get all of the vertices in the face
		Topology.GetVerticesConnectedToFace( hFace, out var verticesInFace );
		if ( verticesInFace is null || verticesInFace.Length == 0 )
			return;

		// Iterate over all of the vertices connected to the face, find the ones which are only connected
		// to two edges and determine if those two edges are co-linear, if so remove the vertex.
		int nNumVertices = verticesInFace.Length;
		for ( var iVertex = 0; iVertex < nNumVertices; ++iVertex )
		{
			RemoveColinearVertex( verticesInFace[iVertex], flColinearAngleTolerance );
		}
	}

	bool RemoveColinearVertex( VertexHandle hVertex, float flColinearAngleTolerance )
	{
		return RemoveColinearVertexAndUpdateTable( hVertex, null, flColinearAngleTolerance );
	}

	public bool RemoveColinearVertexAndUpdateTable( VertexHandle hVertex, SortedSet<HalfEdgeHandle> edgeTable, float flColinearAngleTolerance = 5.0f )
	{
		Topology.GetFullEdgesConnectedToVertex( hVertex, out var edgesConnectedToVertex );

		if ( edgesConnectedToVertex is not null && edgesConnectedToVertex.Count == 2 )
		{
			if ( AreEdgesCoLinear( edgesConnectedToVertex[0], edgesConnectedToVertex[1], flColinearAngleTolerance ) )
			{
				// Were either of the edges in the table
				bool bEdgeInTable = false;
				if ( edgeTable is not null )
				{
					if ( edgeTable.Contains( edgesConnectedToVertex[0] ) ||
						 edgeTable.Contains( edgesConnectedToVertex[1] ) )
					{
						bEdgeInTable = true;
					}
				}

				// Get the vertices at the ends of each edge opposite the vertex about to be removed.
				GetVerticesConnectedToEdge( edgesConnectedToVertex[0], out var hVertexA, out var hVertexB );
				var hVertex0 = (hVertexA == hVertex) ? hVertexB : hVertexA;

				GetVerticesConnectedToEdge( edgesConnectedToVertex[1], out hVertexA, out hVertexB );
				var hVertex1 = (hVertexA == hVertex) ? hVertexB : hVertexA;

				// Remove the vertex, combining the two edges into a single edge
				if ( Topology.RemoveVertex( hVertex, true ) )
				{
					// Remove the two old edges from the table and add the new edge
					if ( bEdgeInTable )
					{
						edgeTable.Remove( edgesConnectedToVertex[0] );
						edgeTable.Remove( edgesConnectedToVertex[1] );
						var hCombinedEdge = FindEdgeConnectingVertices( hVertex0, hVertex1 );
						if ( hCombinedEdge != HalfEdgeHandle.Invalid )
						{
							edgeTable.Add( hCombinedEdge );
						}
					}

					return true;
				}
			}
		}

		return false;
	}

	public bool GetEdgesConnectedToVertex( VertexHandle hVertex, out List<HalfEdgeHandle> edges )
	{
		return Topology.GetFullEdgesConnectedToVertex( hVertex, out edges );
	}

	static float CalcClosestPointToLineT( Vector3 P, Vector3 vLineA, Vector3 vLineB, out Vector3 vDir )
	{
		vDir = vLineB - vLineA;
		var div = vDir.Dot( vDir );
		return div < 0.00001f ? 0.0f : (vDir.Dot( P ) - vDir.Dot( vLineA )) / div;
	}

	static void CalcClosestPointOnLine( Vector3 P, Vector3 vLineA, Vector3 vLineB, out Vector3 vClosest, out float outT )
	{
		outT = CalcClosestPointToLineT( P, vLineA, vLineB, out var vDir );
		vClosest = vLineA + vDir * outT;
	}

	public VertexHandle CreateEdgesConnectingVertexToPoint( VertexHandle hStartVertex, Vector3 vTargetPosition, out List<HalfEdgeHandle> pOutEdgeList, out bool pOutIsLastEdgeConnector, SortedSet<HalfEdgeHandle> pEdgeTable )
	{
		const float flTolerance = 0.001f;

		pOutEdgeList = [];
		pOutIsLastEdgeConnector = false;

		var hCurrentVertex = hStartVertex;
		var hTargetVertex = VertexHandle.Invalid;

		while ( hTargetVertex == VertexHandle.Invalid )
		{
			var hNextVertex = VertexHandle.Invalid;

			if ( FindCutEdgeIntersection( hCurrentVertex, vTargetPosition, out var hIntersectionEdge, out var hIntersectionFace, out var vIntersectionPoint ) )
			{
				GetVerticesConnectedToEdge( hIntersectionEdge, out var hVertexA, out var hVertexB );

				var vPositionA = GetVertexPosition( hVertexA );
				var vPositionB = GetVertexPosition( hVertexB );

				CalcClosestPointOnLineSegment( vIntersectionPoint, vPositionA, vPositionB, out _, out var flParam );

				if ( flParam < flTolerance )
				{
					hNextVertex = hVertexA;
				}
				else if ( flParam > (1.0f - flTolerance) )
				{
					hNextVertex = hVertexB;
				}
				else
				{
					AddVertexToEdgeAndUpdateTable( hVertexA, hVertexB, flParam, out hNextVertex, pEdgeTable );
				}
			}

			if ( hNextVertex == VertexHandle.Invalid )
				break;

			var hTargetEdges = new HalfEdgeHandle[2];
			hTargetEdges[0] = FindEdgeConnectingVertices( hCurrentVertex, hNextVertex );
			hTargetEdges[1] = HalfEdgeHandle.Invalid;

			if ( hTargetEdges[0] == HalfEdgeHandle.Invalid )
			{
				if ( IsLineBetweenVerticesInsideFace( hIntersectionFace, hCurrentVertex, hNextVertex ) )
				{
					AddEdgeToFace( hIntersectionFace, hCurrentVertex, hNextVertex, out hTargetEdges[0] );
				}

				if ( pEdgeTable is not null && (hTargetEdges[0] != HalfEdgeHandle.Invalid) )
				{
					pEdgeTable.Add( hTargetEdges[0] );
				}
			}

			if ( hTargetEdges[0] != HalfEdgeHandle.Invalid )
			{
				var vPositionA = GetVertexPosition( hCurrentVertex );
				var vPositionB = GetVertexPosition( hNextVertex );

				CalcClosestPointOnLine( vTargetPosition, vPositionA, vPositionB, out _, out var flParam );
				if ( (flParam > -flTolerance) && (flParam < (1.0f + flTolerance)) )
				{
					if ( flParam < flTolerance )
					{
						hTargetVertex = hCurrentVertex;
					}
					else if ( flParam > (1.0f - flTolerance) )
					{
						hTargetVertex = hNextVertex;
					}
					else
					{
						if ( AddVertexToEdgeAndUpdateTable( hCurrentVertex, hNextVertex, flParam, out hTargetVertex, pEdgeTable ) )
						{
							hTargetEdges[0] = FindEdgeConnectingVertices( hCurrentVertex, hTargetVertex );
							hTargetEdges[1] = FindEdgeConnectingVertices( hTargetVertex, hNextVertex );

							if ( pEdgeTable is not null && pEdgeTable.Contains( hTargetEdges[1] ) )
							{
								pOutIsLastEdgeConnector = true;
							}
						}
						else
						{
							Assert.True( hTargetVertex != VertexHandle.Invalid );
							break;
						}
					}
				}
			}

			if ( hTargetEdges[0] != HalfEdgeHandle.Invalid )
			{
				pOutEdgeList.Add( hTargetEdges[0] );
			}
			if ( hTargetEdges[1] != HalfEdgeHandle.Invalid )
			{
				pOutEdgeList.Add( hTargetEdges[1] );
			}

			Assert.True( hNextVertex != hStartVertex );
			Assert.True( hNextVertex != hCurrentVertex );
			if ( (hNextVertex == hStartVertex) || (hNextVertex == hCurrentVertex) )
				break;

			hCurrentVertex = hNextVertex;
		}

		return hTargetVertex;
	}

	public enum DissolveRemoveVertexCondition
	{
		None,               // Never remove vertices
		Colinear,           // Remove vertices with only 2 edges attached that are colinear
		InteriorOrColinear, // Remove vertices with only 2 edges attached that are interior edges (not open) or are colinear
		All                 // Remove all vertices with only 2 edges attached
	};

	private bool ShouldDissolveRemoveVertex( VertexHandle hVertex, DissolveRemoveVertexCondition removeCondition, float flColinearTolerance )
	{
		if ( removeCondition == DissolveRemoveVertexCondition.None )
			return false;

		if ( Topology.ComputeNumEdgesConnectedToVertex( hVertex ) != 2 )
			return false;

		Topology.GetFullEdgesConnectedToVertex( hVertex, out var connectedEdges );
		var bInterior = !IsEdgeOpen( connectedEdges[0] );
		var bColinear = AreEdgesCoLinear( connectedEdges[0], connectedEdges[1], flColinearTolerance );

		return removeCondition switch
		{
			DissolveRemoveVertexCondition.InteriorOrColinear => bInterior || bColinear,
			DissolveRemoveVertexCondition.Colinear => bColinear,
			DissolveRemoveVertexCondition.All => true,
			_ => false,
		};
	}

	private bool AddFace( HalfEdgeHandle hOpenEdge, out FaceHandle hOutFace )
	{
		hOutFace = FaceHandle.Invalid;

		var hStartHalfEdge = Topology.GetHalfEdgeForFaceEdge( FaceHandle.Invalid, hOpenEdge );
		if ( !hStartHalfEdge.IsValid )
			return false;

		// Count the number of edges in the loop
		int numEdgesInLoop = 0;
		var hCurrentEdge = hStartHalfEdge;
		do
		{
			++numEdgesInLoop;
			hCurrentEdge = Topology.GetNextEdgeInFaceLoop( hCurrentEdge );
		}
		while ( hCurrentEdge != hStartHalfEdge );

		// Construct the list of vertices
		var hVertices = new VertexHandle[numEdgesInLoop];
		var nVertexCount = 0;
		hCurrentEdge = hStartHalfEdge;
		do
		{
			hVertices[nVertexCount++] = Topology.GetEndVertexConnectedToEdge( hCurrentEdge );
			hCurrentEdge = Topology.GetNextEdgeInFaceLoop( hCurrentEdge );
		}
		while ( hCurrentEdge != hStartHalfEdge );

		// Create the face using the vertices
		hOutFace = AddFace( hVertices );
		return hOutFace.IsValid;
	}

	private HalfEdgeHandle FindEdgeConnectingVertices( VertexHandle hVertexA, VertexHandle hVertexB )
	{
		return Topology.FindFullEdgeConnectingVertices( hVertexA, hVertexB );
	}

	public bool MergeEdges( HalfEdgeHandle hEdgeA, HalfEdgeHandle hEdgeB, out HalfEdgeHandle hOutNewEdge )
	{
		hOutNewEdge = HalfEdgeHandle.Invalid;

		if ( !Topology.GetEdgeMergeVertexPairs( hEdgeA, hEdgeB, out var hVertexPairA1, out var hVertexPairA2, out var hVertexPairB1, out var hVertexPairB2 ) )
			return false;

		// Check to see of the edges share a single vertex, 
		// if so just merge the other vertex instead of the edge.
		var hSharedVertex = VertexHandle.Invalid;
		var hMergeVertexA = VertexHandle.Invalid;
		var hMergeVertexB = VertexHandle.Invalid;

		if ( hVertexPairA1 == hVertexPairA2 )
		{
			hSharedVertex = hVertexPairA1;
			hMergeVertexA = hVertexPairB1;
			hMergeVertexB = hVertexPairB2;
		}
		else if ( hVertexPairB1 == hVertexPairB2 )
		{
			hSharedVertex = hVertexPairB1;
			hMergeVertexA = hVertexPairA1;
			hMergeVertexB = hVertexPairA2;
		}

		if ( hSharedVertex != VertexHandle.Invalid )
		{
			if ( !MergeVertices( hMergeVertexA, hMergeVertexB, 0.5f, out var hNewVertex ) )
				return false;

			hOutNewEdge = FindEdgeConnectingVertices( hNewVertex, hSharedVertex );

			IsDirty = true;

			return true;
		}

		// No vertices are shared between the edges, merge them.
		var a = Vector3.Lerp( GetVertexPosition( hVertexPairA1 ), GetVertexPosition( hVertexPairA2 ), 0.5f );
		var b = Vector3.Lerp( GetVertexPosition( hVertexPairB1 ), GetVertexPosition( hVertexPairB2 ), 0.5f );

		if ( Topology.MergeEdges( hEdgeA, hEdgeB, out var hNewVertexA, out var hNewVertexB ) )
		{
			SetVertexPosition( hNewVertexA, a );
			SetVertexPosition( hNewVertexB, b );

			hOutNewEdge = FindEdgeConnectingVertices( hNewVertexA, hNewVertexB );

			IsDirty = true;

			return true;
		}

		return false;
	}

	private bool MergeVertices( VertexHandle hVertexA, VertexHandle hVertexB, float flParam, out VertexHandle hOutNewVertex )
	{
		// If there is an edge connecting the vertices, just call edge collapse so that 
		// the proper interpolation is done for the face vertices of the merged edge.
		var hEdge = Topology.FindHalfEdgeConnectingVertices( hVertexA, hVertexB );
		if ( hEdge != HalfEdgeHandle.Invalid )
		{
			return CollapseEdge( hEdge, flParam, out hOutNewVertex );
		}

		// Interpolate the data on the two vertices and store a copy before they are destroyed
		var newVertex = GetVertexPosition( hVertexA ).LerpTo( GetVertexPosition( hVertexB ), flParam );

		// Merge the two vertices and create a new one with
		// the interpolated values of the original vertices.
		if ( Topology.MergeVertices( hVertexA, hVertexB, out hOutNewVertex ) )
		{
			SetVertexPosition( hOutNewVertex, newVertex );
			return true;
		}

		return false;
	}

	private static bool IsVertexInMesh( VertexHandle hVertex ) => hVertex is not null && hVertex.IsValid;
	private static bool IsHalfEdgeInMesh( HalfEdgeHandle hHalfEdge ) => hHalfEdge is not null && hHalfEdge.IsValid;
	private static bool IsFaceInMesh( FaceHandle hFace ) => hFace is not null && hFace.IsValid;

	public void FlipAllFaces()
	{
		Topology.FlipAllFaces();

		ComputeFaceTextureCoordinatesFromParameters();

		IsDirty = true;
	}

	public int MergeVerticesWithinDistance( IReadOnlyList<VertexHandle> originalVertices, float flMaxDistance, bool bPreConnect, bool bAveragePositions, out List<VertexHandle> pOutFinalVertices )
	{
		pOutFinalVertices = new();
		var bUseDistance = (flMaxDistance >= 0.0f);
		var vDistance = new Vector3( flMaxDistance, flMaxDistance, flMaxDistance );
		var nMaxIterations = 10;
		var flMaxDistSq = bUseDistance ? (flMaxDistance * flMaxDistance) : float.MaxValue;

		var verticesToMerge = new List<VertexHandle>();
		verticesToMerge.EnsureCapacity( originalVertices.Count );
		foreach ( var hVertex in originalVertices )
		{
			if ( IsVertexInMesh( hVertex ) )
			{
				verticesToMerge.Add( hVertex );
			}
		}

		int nNumOriginalVertices = verticesToMerge.Count;
		if ( nNumOriginalVertices < 2 )
			return 0;

		var nNumTotalVerticesMerged = 0;

		pOutFinalVertices.EnsureCapacity( nNumOriginalVertices );

		// Assign the vertices to groups based on their positions. Each group will contain all of the 
		// vertices within the specified maximum distance of the first vertex in the group.
		var verticesSortedByGroup = new List<VertexHandle>( nNumOriginalVertices );
		var groupVertexCounts = new int[nNumOriginalVertices];
		var groupOffsets = new int[nNumOriginalVertices];

		for ( int iIteration = 0; iIteration < nMaxIterations; ++iIteration )
		{
			// Stop if there are not at least two vertices left.
			var nNumVerticesToMerge = verticesToMerge.Count;
			if ( nNumVerticesToMerge < 2 )
				break;

			var vertexGroupAssignments = new List<int>( nNumVerticesToMerge );
			vertexGroupAssignments.AddRange( Enumerable.Repeat( -1, nNumVerticesToMerge ) );
			verticesSortedByGroup.Clear();

			var nNumGroups = 0;

			if ( bUseDistance )
			{
				// Build an array of the positions specifically for the vertices 
				// to merge instead of all the positions in the mesh
				var vertexPositions = new List<Vector3>( nNumVerticesToMerge );
				for ( var iVertex = 0; iVertex < nNumVerticesToMerge; ++iVertex )
				{
					vertexPositions.Add( GetVertexPosition( verticesToMerge[iVertex] ) );
				}

				// Build a kd-tree of the vertex positions that we can use to find nearby vertices more efficiently
				var vertexPositionTree = new VertexKDTree();
				vertexPositionTree.BuildMidpoint( vertexPositions );

				for ( var iVertex = 0; iVertex < nNumVerticesToMerge; ++iVertex )
				{
					// Check to see if the vertex has already been added to a group.
					if ( vertexGroupAssignments[iVertex] >= 0 )
						continue;

					// If the vertex has not been assigned to a group assign it the next available group.
					var hVertexA = verticesToMerge[iVertex];
					var vGroupPosition = GetVertexPosition( hVertexA );
					var nGroupIndex = nNumGroups++;
					vertexGroupAssignments[iVertex] = nGroupIndex;

					// Set the index of the start of the group in the sorted vertex array.
					groupOffsets[nGroupIndex] = verticesSortedByGroup.Count;

					// Add the vertex to the sorted array
					verticesSortedByGroup.Add( hVertexA );

					// Search the the rest of the vertices to see if there are any which have not yet been 
					// assigned a group that are close enough to the current vertex to be grouped with it.
					var vGroupMin = vGroupPosition - vDistance;
					var vGroupMax = vGroupPosition + vDistance;
					var verticesInBox = vertexPositionTree.FindVertsInBox( vGroupMin, vGroupMax );
					var nNumVerticesInBox = verticesInBox.Count;

					// There are some cases where the behavior of merging is order dependent, ideally it
					// wouldn't be, but it is due to the constraint of not being able to connect more 
					// than two faces at a single vertex by merging. So to maintain the same behavior 
					// as the old approach we need to add the vertices in the order they were supplied
					// in the input list.
					verticesInBox.Sort();

					for ( var iVertexInBox = 0; iVertexInBox < nNumVerticesInBox; ++iVertexInBox )
					{
						var nVertexIndexB = verticesInBox[iVertexInBox];
						var pVertexPosition = vertexPositions[nVertexIndexB];

						if ( pVertexPosition.DistanceSquared( vGroupPosition ) < flMaxDistSq )
						{
							if ( vertexGroupAssignments[nVertexIndexB] < 0 )
							{
								var hVertexB = verticesToMerge[nVertexIndexB];
								verticesSortedByGroup.Add( hVertexB );
								vertexGroupAssignments[nVertexIndexB] = nGroupIndex;
							}
						}
					}

					// Compute the number of vertices that were assigned to the group
					groupVertexCounts[nGroupIndex] = verticesSortedByGroup.Count - groupOffsets[nGroupIndex];
				}
			}
			else
			{
				// If not using the distance just add all the vertices to a single group for merging
				nNumGroups = 1;
				verticesSortedByGroup = new( verticesToMerge );

				for ( var i = 0; i < vertexGroupAssignments.Count; i++ )
				{
					vertexGroupAssignments[i] = 0;
				}

				groupVertexCounts[0] = nNumVerticesToMerge;
				groupOffsets[0] = 0;
			}

			var groupsMergedVertexCount = new int[nNumGroups]; // Number of vertices in each group that were successfully merged
			var groupsSumPosition = new Vector3[nNumGroups]; // Average position of the vertices in the group that were merged
			var groupsTargetVertex = new VertexHandle[nNumGroups]; // Target vertex with which other vertices in the group should be merged.

			for ( var iGroup = 0; iGroup < nNumGroups; ++iGroup )
			{
				var nGroupVertexOffset = groupOffsets[iGroup];
				var hFirstVertex = verticesSortedByGroup[nGroupVertexOffset];

				groupsMergedVertexCount[iGroup] = 1;
				groupsTargetVertex[iGroup] = hFirstVertex;
				groupsSumPosition[iGroup] = GetVertexPosition( hFirstVertex );

				// Clear the first vertex in the group, it does not need to be merged.
				verticesSortedByGroup[nGroupVertexOffset] = VertexHandle.Invalid;
			}

			// Merge all of the vertices in each group. Multiple iterations are done until all of the vertices
			// in all of the groups have been merged or until no vertices were merged in the previous iteration.
			for ( var iGroupPass = 0; iGroupPass < nNumVerticesToMerge; ++iGroupPass )
			{
				var nNumMerged = 0;
				var nNumUnmerged = 0;

				for ( var iGroup = 0; iGroup < nNumGroups; ++iGroup )
				{
					var nGroupVertexCount = groupVertexCounts[iGroup];
					if ( nGroupVertexCount < 2 )
						continue;

					var nGroupVertexOffset = groupOffsets[iGroup];
					var nNumUnmergedInGroup = 0;

					var hTargetVertex = groupsTargetVertex[iGroup];

					for ( var iVertex = 1; iVertex < nGroupVertexCount; ++iVertex )
					{
						var hMergeVertex = verticesSortedByGroup[nGroupVertexOffset + iVertex];
						if ( IsVertexInMesh( hMergeVertex ) == false )
							continue;

						// Get the position of the vertex to be merged before
						// merging it, which will delete the vertex.			
						var vMergeVertexPosition = GetVertexPosition( hMergeVertex );

						// If bPreConnect is specified connect vertices that share a face allowing them to
						// be merged even if they did not originally have a common edge.
						if ( bPreConnect )
						{
							var hSharedFace = Topology.FindFaceSharedByVertices( hTargetVertex, hMergeVertex );
							if ( hSharedFace != FaceHandle.Invalid )
							{
								AddEdgeToFace( hSharedFace, hTargetVertex, hMergeVertex, out _ );
							}
						}

						// If averaging positions, set the merge interpolation parameter to 0.5f, 
						// otherwise set it to 1.0 so that the data of the merge vertex is preserved.
						var flParam = bAveragePositions ? 0.5f : 1.0f;

						if ( MergeVertices( hTargetVertex, hMergeVertex, flParam, out var hNewVertex ) )
						{
							// Add the position of the vertex to the group sum position
							groupsSumPosition[iGroup] += vMergeVertexPosition;
							groupsMergedVertexCount[iGroup] += 1;

							// Update the merged vertex of the group
							groupsTargetVertex[iGroup] = hNewVertex;

							// Update the target vertex to be the new vertex since the target vertex has
							// be removed, if we don't update the target, then there is no way to merge
							// the remaining vertices in this pass.
							hTargetVertex = hNewVertex;

							// Set the original vertex in the group to invalid so we 
							// don't try to merge it again in subsequent passes.
							verticesSortedByGroup[nGroupVertexOffset + iVertex] = VertexHandle.Invalid;

							++nNumMerged;
						}
						else
						{
							++nNumUnmerged;
							++nNumUnmergedInGroup;
						}
					}

					// If all of the vertices in the group were merged mark the group as not having any
					// vertices so that it is not touched in any future iterations.
					if ( nNumUnmergedInGroup == 0 )
					{
						groupVertexCounts[iGroup] = -1;
					}
				}

				if ( (nNumUnmerged == 0) || (nNumMerged == 0) )
					break;
			}

			// Set the merged vertex positions to the average position
			var nNumVerticesMerged = 0;
			for ( var iGroup = 0; iGroup < nNumGroups; ++iGroup )
			{
				if ( bAveragePositions )
				{
					var hVertex = groupsTargetVertex[iGroup];
					if ( IsVertexInMesh( hVertex ) )
					{
						var vAveragePosition = groupsSumPosition[iGroup] / (float)groupsMergedVertexCount[iGroup];
						SetVertexPosition( hVertex, vAveragePosition );
					}
				}

				var nNumVerticesMergedInGroup = groupsMergedVertexCount[iGroup];
				if ( nNumVerticesMergedInGroup > 1 )
				{
					nNumVerticesMerged += nNumVerticesMergedInGroup;
				}
			}

			// Add the merged vertices from the groups
			for ( var iGroup = 0; iGroup < nNumGroups; ++iGroup )
			{
				if ( IsVertexInMesh( groupsTargetVertex[iGroup] ) )
				{
					pOutFinalVertices.Add( groupsTargetVertex[iGroup] );
				}
			}

			// Build the remaining list of vertices to merge
			verticesToMerge.Clear();
			for ( var iVertex = 0; iVertex < nNumVerticesToMerge; ++iVertex )
			{
				if ( IsVertexInMesh( verticesSortedByGroup[iVertex] ) )
				{
					verticesToMerge.Add( verticesSortedByGroup[iVertex] );
				}
			}

			nNumTotalVerticesMerged += nNumVerticesMerged;
		}

		// Add all of the vertices which were not merged
		pOutFinalVertices.AddRange( verticesToMerge );

		return nNumTotalVerticesMerged;
	}

	private bool CollapseEdge( HalfEdgeHandle hHalfEdgeA, float flParam, out VertexHandle pOutNewVertex )
	{
		var hHalfEdgeB = Topology.GetOppositeHalfEdge( hHalfEdgeA );

		// Get the vertices connected to the edge and average the values
		var hVertexA = Topology.GetEndVertexConnectedToEdge( hHalfEdgeB );
		var hVertexB = Topology.GetEndVertexConnectedToEdge( hHalfEdgeA );

		var newVertex = GetVertexPosition( hVertexA ).LerpTo( GetVertexPosition( hVertexB ), flParam );
		var hEdge = Topology.GetFullEdgeForHalfEdge( hHalfEdgeA );
		var bRemoved = Topology.CollapseEdge( hEdge, out pOutNewVertex, out var _ );

		if ( pOutNewVertex != VertexHandle.Invalid )
		{
			SetVertexPosition( pOutNewVertex, newVertex );
		}

		return bRemoved;
	}

	/// <summary>
	/// Add a face filling in the open edge loop specified by the provided edge
	/// </summary>
	public bool CreateFaceInEdgeLoop( HalfEdgeHandle hOpenEdge, out FaceHandle hNewFace )
	{
		hNewFace = FaceHandle.Invalid;

		// Find the face to which the new face will be connected
		Topology.GetFacesConnectedToFullEdge( hOpenEdge, out var hFaceA, out var hFaceB );
		var hSourceFace = hFaceA.IsValid ? hFaceA : hFaceB;
		if ( !hSourceFace.IsValid )
			return false;

		// Try to add a face for the specified open edge. If the edge is not open no face will be 
		// created. Creation of the face may also fail if it would create invalid topology.
		if ( AddFace( hOpenEdge, out hNewFace ) )
		{
			SetFaceMaterialIndex( hNewFace, GetFaceMaterialIndex( hSourceFace ) );
			TextureAlignToGrid( Transform, hNewFace );

			return true;
		}

		return false;
	}

	/// <summary>
	/// Get the face connected to this half edge
	/// </summary>
	public FaceHandle GetHalfEdgeFace( HalfEdgeHandle hEdge )
	{
		return hEdge.Face;
	}

	/// <summary>
	/// Determine if the specified edge is open (only has one connected face)
	/// </summary>
	public bool IsEdgeOpen( HalfEdgeHandle hEdge )
	{
		return Topology.IsFullEdgeOpen( hEdge );
	}

	/// <summary>
	/// Find all of the edges in the ring with the specified edge. An edge ring is the set of edges that
	/// are connected by a loop of faces.
	/// </summary>
	public void FindEdgeRing( HalfEdgeHandle hEdge, out List<HalfEdgeHandle> outEdgeList )
	{
		Topology.FindEdgeRing( hEdge, out outEdgeList );
	}

	/// <summary>
	/// Bridge two edges (create a face connecting them). The edges must both be open and belong to
	/// different faces.
	/// </summary>
	public bool BridgeEdges( HalfEdgeHandle hEdgeA, HalfEdgeHandle hEdgeB, out FaceHandle hOutNewFace )
	{
		Topology.GetFacesConnectedToFullEdge( hEdgeA, out var hFaceA, out var hFaceB );
		var hSourceFace = hFaceA.IsValid ? hFaceA : hFaceB;
		if ( !hSourceFace.IsValid )
		{
			hOutNewFace = FaceHandle.Invalid;
			return false;
		}

		if ( !Topology.BridgeEdges( hEdgeA, hEdgeB, out hOutNewFace ) )
		{
			// Do the edges share a vertex, if so try to create a triangle face
			var hSharedVertex = Topology.FindVertexConnectingFullEdges( hEdgeA, hEdgeB );
			if ( hSharedVertex.IsValid )
			{
				Topology.GetVerticesConnectedToHalfEdge( hEdgeA, out var hVertexA1, out var hVertexA2 );
				Topology.GetVerticesConnectedToHalfEdge( hEdgeB, out var hVertexB1, out var hVertexB2 );

				var vertices = new VertexHandle[3];
				vertices[0] = (hSharedVertex != hVertexA1) ? hVertexA1 : hVertexA2;
				vertices[1] = hSharedVertex;
				vertices[2] = (hSharedVertex != hVertexB1) ? hVertexB1 : hVertexB2;

				if ( !Topology.AddFace( out hOutNewFace, vertices ) )
				{
					(vertices[2], vertices[0]) = (vertices[0], vertices[2]);
					Topology.AddFace( out hOutNewFace, vertices );
				}
			}
		}

		if ( !hOutNewFace.IsValid )
			return false;

		SetFaceMaterialIndex( hOutNewFace, GetFaceMaterialIndex( hSourceFace ) );
		TextureAlignToGrid( Transform, hOutNewFace );

		IsDirty = true;

		return true;
	}

	/// <summary>
	/// Connect the specified edges by adding a vertex to their mid point of each edge and then 
	/// connecting the vertices.
	/// </summary>
	public bool ConnectEdges( IReadOnlyList<HalfEdgeHandle> edges, out List<HalfEdgeHandle> newEdges )
	{
		newEdges = null;

		if ( edges is null || edges.Count < 2 )
			return false;

		var nNumEdges = edges.Count;
		var edgesToCut = new List<HalfEdgeHandle>( nNumEdges );
		var vertices = new List<VertexHandle>( nNumEdges );

		// First build a list of the edges that should be cut, skip any which do not share a face with 
		// any of the other edges. This prevents adding new vertices that will not be used in a new edge.
		for ( int iEdge = 0; iEdge < nNumEdges; ++iEdge )
		{
			for ( int jEdge = 0; jEdge < nNumEdges; ++jEdge )
			{
				if ( jEdge == iEdge )
					continue;

				var hSharedFace = Topology.FindFaceConnectingFullEdges( edges[iEdge], edges[jEdge] );
				if ( hSharedFace.IsValid )
				{
					edgesToCut.Add( edges[iEdge] );
					break;
				}
			}
		}

		// Add the vertices to the edges
		int nNumEdgesToCut = edgesToCut.Count;
		for ( int iEdge = 0; iEdge < nNumEdgesToCut; ++iEdge )
		{
			Topology.GetVerticesConnectedToHalfEdge( edgesToCut[iEdge], out var hVertexA, out var hVertexB );

			AddVertexToEdge( hVertexA, hVertexB, 0.5f, out var hNewVertex );
			if ( hNewVertex.IsValid )
				vertices.Add( hNewVertex );
		}

		// Connect all of the vertices that were added
		return ConnectVertices( vertices, out newEdges );
	}

	public bool AddVertexToEdge( VertexHandle hVertexA, VertexHandle hVertexB, float flParam, out VertexHandle pOutNewVertex )
	{
		pOutNewVertex = null;

		var hEdge = Topology.FindHalfEdgeConnectingVertices( hVertexA, hVertexB );
		if ( !hEdge.IsValid )
			return false;

		var hPrevEdge = Topology.FindPreviousEdgeInFaceLoop( hEdge );
		var hOpposite = Topology.GetOppositeHalfEdge( hEdge );
		var hOppositePrev = Topology.FindPreviousEdgeInFaceLoop( hOpposite );

		// Add the new vertex to the edge, this will result in the edge being split into two edges.
		if ( !Topology.AddVertexToEdge( hEdge, out var hNewVertex ) )
			return false;

		// Interpolate the values of the vertices to compute the value of the new vertex
		InterpolateVertexData( hNewVertex, hVertexA, hVertexB, flParam );

		// Interpolate the value of the face vertices connected to each face to compute the value of the
		// new face vertex on each side of the edge.
		var hEdgeAToNew = Topology.FindHalfEdgeConnectingVertices( hVertexA, hNewVertex );
		var hEdgeNewToB = Topology.FindHalfEdgeConnectingVertices( hNewVertex, hVertexB );
		InterpolateFaceVertexData( hEdgeAToNew, hPrevEdge, hEdgeNewToB, flParam );

		var hEdgeBToNew = Topology.FindHalfEdgeConnectingVertices( hVertexB, hNewVertex );
		var hEdgeNewToA = Topology.FindHalfEdgeConnectingVertices( hNewVertex, hVertexA );
		InterpolateFaceVertexData( hEdgeBToNew, hEdgeNewToA, hOppositePrev, flParam );

		pOutNewVertex = hNewVertex;

		return true;
	}

	public bool AddVertexToEdgeAndUpdateTable( VertexHandle hVertexA, VertexHandle hVertexB, float flParam, out VertexHandle pNewVertex, SortedSet<HalfEdgeHandle> pEdgeTable )
	{
		pNewVertex = VertexHandle.Invalid;

		bool bOriginalEdgeInTable = false;
		var hOriginalEdge = HalfEdgeHandle.Invalid;

		if ( pEdgeTable is not null )
		{
			var hEdge = FindEdgeConnectingVertices( hVertexA, hVertexB );
			if ( pEdgeTable.Contains( hEdge ) )
			{
				bOriginalEdgeInTable = true;
			}
		}

		if ( AddVertexToEdge( hVertexA, hVertexB, flParam, out var hNewVertex ) )
		{
			if ( bOriginalEdgeInTable )
			{
				Topology.GetFullEdgesConnectedToVertex( hNewVertex, out var connectedEdges );
				if ( connectedEdges.Count == 2 )
				{
					pEdgeTable.Remove( hOriginalEdge );
					pEdgeTable.Add( connectedEdges[0] );
					pEdgeTable.Add( connectedEdges[1] );
				}
			}

			pNewVertex = hNewVertex;

			return true;
		}

		return false;
	}

	public bool RemoveVertex( VertexHandle hVertex, bool removeFreeVerts )
	{
		return Topology.RemoveVertex( hVertex, removeFreeVerts );
	}

	private bool AddVertexToEdgeAtDistance( VertexHandle hVertexA, VertexHandle hVertexB, float distance, float maxParam, out VertexHandle newVertex )
	{
		var edgeLength = GetVertexPosition( hVertexA ).Distance( GetVertexPosition( hVertexB ) );
		var param = maxParam;
		if ( edgeLength > 0.000001f )
		{
			param = MathF.Min( distance / edgeLength, maxParam );
		}

		if ( param >= 1.0f )
		{
			newVertex = hVertexB;
			return true;
		}

		param = 1.0f - param;
		return AddVertexToEdge( hVertexB, hVertexA, param, out newVertex );
	}

	private bool BevelVertex( VertexHandle hVertex, bool replaceVertexWithFace, float distance, out VertexHandle[] hNewVertices, out FaceHandle hNewFace )
	{
		Topology.GetVerticesConnectedToVertexByEdge( hVertex, out var connectedVertices );
		var nNumVertices = connectedVertices.Count;

		hNewVertices = new VertexHandle[nNumVertices];
		var connectedFaces = new FaceHandle[nNumVertices];

		for ( var iVertex = 0; iVertex < nNumVertices; ++iVertex )
		{
			var hHalfEdge = Topology.FindHalfEdgeConnectingVertices( hVertex, connectedVertices[iVertex] );
			connectedFaces[iVertex] = Topology.GetFaceConnectedToHalfEdge( hHalfEdge );
			AddVertexToEdgeAtDistance( hVertex, connectedVertices[iVertex], distance, 1.0f, out hNewVertices[iVertex] );
		}

		for ( int iVertex = 0, iPrevVertex = nNumVertices - 1; iVertex < nNumVertices; iPrevVertex = iVertex++ )
		{
			AddEdgeToFace( connectedFaces[iVertex], hNewVertices[iVertex], hNewVertices[iPrevVertex], out _ );
		}

		hNewFace = FaceHandle.Invalid;

		if ( replaceVertexWithFace )
		{
			var hVerticesForFace = new VertexHandle[nNumVertices];
			for ( var i = 0; i < nNumVertices; ++i )
			{
				hVerticesForFace[i] = hNewVertices[nNumVertices - 1 - i];
			}

			RemoveVertex( hVertex, true );
			hNewFace = AddFace( hVerticesForFace );
		}

		IsDirty = true;

		return true;
	}

	public bool BevelVertices( IReadOnlyList<VertexHandle> vertices, float distance, out List<VertexHandle> newVertices )
	{
		var numVertices = vertices.Count;
		newVertices = new List<VertexHandle>( numVertices * 8 );

		var success = true;

		for ( var i = 0; i < numVertices; ++i )
		{
			if ( BevelVertex( vertices[i], true, distance, out var newVerticesForVertex, out _ ) )
			{
				newVertices.AddRange( newVerticesForVertex );
			}
			else
			{
				success = false;
			}
		}

		Topology.FindFacesConnectedToVertices( newVertices, newVertices.Count, out var connectedFaces, out _ );

		foreach ( var hFace in connectedFaces )
		{
			TextureAlignToGrid( Transform, hFace );
		}

		IsDirty = true;

		return success;
	}

	private bool ConnectVertices( IReadOnlyList<VertexHandle> pVertices, out List<HalfEdgeHandle> outNewEdges )
	{
		var numVertices = pVertices.Count;
		outNewEdges = new List<HalfEdgeHandle>( numVertices * 2 );
		var connected = false;

		for ( var i = 0; i < numVertices; ++i )
		{
			for ( int j = i + 1; j < numVertices; ++j )
			{
				if ( ConnectVertices( pVertices[i], pVertices[j], out var hNewEdge ) )
				{
					if ( hNewEdge.IsValid )
					{
						outNewEdges.Add( hNewEdge );
						connected = true;
					}
				}
			}
		}

		return connected;
	}

	public bool GetFacesConnectedToVertex( VertexHandle hVertex, out List<FaceHandle> faces )
	{
		return Topology.GetFacesConnectedToVertex( hVertex, out faces );
	}

	public bool GetFacesConnectedToFace( FaceHandle hFace, out List<FaceHandle> faces )
	{
		return Topology.GetFacesConnectedToFace( hFace, out faces );
	}

	public HalfEdgeHandle FindFaceVertexConnectedToVertex( VertexHandle hVertex, FaceHandle hFace )
	{
		return Topology.FindEdgeConnectedToFaceEndingAtVertex( hFace, hVertex );
	}

	public HalfEdgeHandle GetNextVertexInFace( HalfEdgeHandle hFaceVertex )
	{
		return Topology.GetNextEdgeInFaceLoop( hFaceVertex );
	}

	public bool ConnectVertices( VertexHandle hVertexA, VertexHandle hVertexB, out HalfEdgeHandle hNewEdge )
	{
		hNewEdge = null;

		Topology.FindFacesSharedByVertices( hVertexA, hVertexB, out var sharedFaces );

		foreach ( var hSharedFace in sharedFaces )
		{
			if ( IsLineBetweenVerticesInsideFace( hSharedFace, hVertexA, hVertexB ) )
			{
				if ( AddEdgeToFace( hSharedFace, hVertexA, hVertexB, out hNewEdge ) )
				{
					IsDirty = true;

					return true;
				}
			}
		}

		return false;
	}

	bool IsLineBetweenVerticesInsideFace( FaceHandle face, VertexHandle a, VertexHandle b )
	{
		if ( a == b ) return false;

		var pa = GetVertexPosition( a );
		var pb = GetVertexPosition( b );

		var v3 = GetFaceVertexPositions( face, Transform.Zero ).ToArray();
		if ( v3.Length < 3 ) return false;

		PlaneEquation( v3, out var n, out _ );

		var u = (MathF.Abs( n.z ) < 0.9f ? Vector3.Up : Vector3.Right).Cross( n ).Normal;
		var v = n.Cross( u );

		var a2 = new Vector2( pa.Dot( u ), pa.Dot( v ) );
		var b2 = new Vector2( pb.Dot( u ), pb.Dot( v ) );

		var poly = new Vector2[v3.Length];
		for ( int i = 0; i < v3.Length; ++i ) poly[i] = new Vector2( v3[i].Dot( u ), v3[i].Dot( v ) );

		static float Cross( Vector2 x, Vector2 y ) => x.x * y.y - x.y * y.x;

		for ( int i = poly.Length - 1, j = 0; j < poly.Length; i = j++ )
		{
			var p = poly[i];
			var q = poly[j];

			var da = b2 - a2;
			var db = q - p;

			var o1 = Cross( da, p - a2 );
			var o2 = Cross( da, q - a2 );
			var o3 = Cross( db, a2 - p );
			var o4 = Cross( db, b2 - p );

			if ( o1 * o2 < 0 && o3 * o4 < 0 ) return false;
		}

		var m = (a2 + b2) * 0.5f;
		var inside = false;
		for ( int i = poly.Length - 1, j = 0; j < poly.Length; i = j++ )
		{
			if ( ((poly[i].y > m.y) != (poly[j].y > m.y)) &&
				 (m.x < (poly[j].x - poly[i].x) * (m.y - poly[i].y) /
				 (poly[j].y - poly[i].y + 1e-20f) + poly[i].x) )
			{
				inside = !inside;
			}
		}

		return inside;
	}

	private bool AddEdgeToFace( FaceHandle hFace, VertexHandle hVertexA, VertexHandle hVertexB, out HalfEdgeHandle pOutNewEdge )
	{
		pOutNewEdge = null;

		if ( !hVertexA.IsValid || !hVertexB.IsValid )
			return false;

		// Must be two different vertices
		if ( hVertexA == hVertexB )
			return false;

		var hFaceVertexA = Topology.FindEdgeConnectedToFaceEndingAtVertex( hFace, hVertexA );
		var hFaceVertexB = Topology.FindEdgeConnectedToFaceEndingAtVertex( hFace, hVertexB );

		// If either of the vertices is internal the edge must be connected in the correct winding 
		// order, use the vertex which is not internal to determine the correct winding order.
		var hExternalVertex = VertexHandle.Invalid;
		var hInternalVertex = VertexHandle.Invalid;

		if ( Topology.IsVertexInternal( hVertexA ) )
		{
			// Cannot connect two internal vertices
			if ( Topology.IsVertexInternal( hVertexB ) )
				return false;

			hInternalVertex = hVertexA;
			hExternalVertex = hVertexB;
		}
		else if ( Topology.IsVertexInternal( hVertexB ) )
		{
			hInternalVertex = hVertexB;
			hExternalVertex = hVertexA;
		}

		// If there is a position stream use the direction of the edge to disambiguate
		// which of the incoming half edge to internal vertex is the best one to use.
		if ( hInternalVertex.IsValid && hExternalVertex.IsValid )
		{
			// Compute the direction of the new edge to be added, going from the internal vertex to the external
			var vExternalPos = GetVertexPosition( hExternalVertex );
			var vInternalPos = GetVertexPosition( hInternalVertex );
			var vNewEdgeDir = (vExternalPos - vInternalPos).Normal;

			// Compute the normal of the face
			ComputeFaceNormal( hFace, out var vFaceNormal );

			// Determine which of the edges terminating at the internal edge should be used by finding 
			// the direction perpendicular to each edge that points into the face and comparing the 
			// direction of the new edge to the vector pointing into the face.
			float flBestDot = float.MinValue;
			var hBestInternalFaceVertex = HalfEdgeHandle.Invalid;

			Topology.GetIncomingHalfEdgesConnectedToVertex( hInternalVertex, out var incomingEdges );

			var nNumIncomingEdges = incomingEdges.Count;
			for ( var iEdge = 0; iEdge < nNumIncomingEdges; ++iEdge )
			{
				var hIncomingEdge = incomingEdges[iEdge];
				if ( Topology.GetFaceConnectedToHalfEdge( hIncomingEdge ) == hFace )
				{
					var hPreviousFaceVertex = Topology.FindPreviousEdgeInFaceLoop( hIncomingEdge );
					var hPreviousVertex = Topology.GetEndVertexConnectedToEdge( hPreviousFaceVertex );
					var vPreviousPos = GetVertexPosition( hPreviousVertex );

					// Compute the direction of the internal edge
					var vInternalEdgeDir = (vInternalPos - vPreviousPos).Normal;

					// Compute the direction pointing into the face
					var vInteriorDir = vFaceNormal.Cross( vInternalEdgeDir );

					// Is the new edge direction closer to the interior direction
					// of this edge than any of the previous edges?
					float flDot = vInteriorDir.Dot( vNewEdgeDir );
					if ( flDot > flBestDot )
					{
						hBestInternalFaceVertex = hIncomingEdge;
						flBestDot = flDot;
					}
				}
			}

			Assert.True( hBestInternalFaceVertex.IsValid );
			if ( hBestInternalFaceVertex.IsValid )
			{
				if ( hInternalVertex == hVertexA )
				{
					hFaceVertexA = hBestInternalFaceVertex;
				}
				else
				{
					hFaceVertexB = hBestInternalFaceVertex;
				}
			}
		}

		return Topology.AddEdgeToFace( hFaceVertexA, hFaceVertexB, out pOutNewEdge );
	}

	private bool FindOpenEdgeLoop( HalfEdgeHandle hEdge, out List<HalfEdgeHandle> pOutEdgeList )
	{
		pOutEdgeList = new List<HalfEdgeHandle>();
		var hStartHalfEdge = Topology.GetHalfEdgeForFaceEdge( FaceHandle.Invalid, hEdge );
		if ( hStartHalfEdge == HalfEdgeHandle.Invalid )
			return false;

		// Count the number of edges in the loop
		int nNumEdgesInLoop = 0;
		var hCurrentEdge = hStartHalfEdge;
		do
		{
			++nNumEdgesInLoop;
			hCurrentEdge = Topology.GetNextEdgeInFaceLoop( hCurrentEdge );
		}
		while ( hCurrentEdge != hStartHalfEdge );

		// Allocate the space in the provided list
		pOutEdgeList.EnsureCapacity( pOutEdgeList.Count + nNumEdgesInLoop );

		// Add the edges to the list
		hCurrentEdge = hStartHalfEdge;
		do
		{
			pOutEdgeList.Add( Topology.GetFullEdgeForHalfEdge( hCurrentEdge ) );
			hCurrentEdge = Topology.GetNextEdgeInFaceLoop( hCurrentEdge );
		}
		while ( hCurrentEdge != hStartHalfEdge );

		return true;
	}

	private FaceHandle FindFaceSharedEdges( IReadOnlyList<HalfEdgeHandle> edgeList )
	{
		int nNumEdges = edgeList.Count;
		if ( nNumEdges < 2 )
			return FaceHandle.Invalid;

		var hFaceSharedFace = Topology.FindFaceConnectingFullEdges( edgeList[0], edgeList[1] );

		for ( int iEdge = 2; iEdge < nNumEdges; ++iEdge )
		{
			if ( Topology.FindFaceConnectingFullEdges( edgeList[0], edgeList[iEdge] ) != hFaceSharedFace )
			{
				hFaceSharedFace = FaceHandle.Invalid;
			}
		}

		return hFaceSharedFace;
	}

	public void FindEdgeLoopForEdges( IReadOnlyList<HalfEdgeHandle> originalEdges, out HalfEdgeHandle[] pOutEdgeLoopEdges )
	{
		var loopEdges = new Dictionary<HalfEdgeHandle, int>( 256 );
		var subSetLoopEdges = new List<HalfEdgeHandle>( 1024 );
		var index = 0;

		// First find the existing loops
		Topology.FindEdgeIslands( originalEdges, out var edgeIslands );

		for ( int iIsland = 0; iIsland < edgeIslands.Count; ++iIsland )
		{
			var edgeIsland = edgeIslands[iIsland];
			if ( Topology.ClassifyEdgeListConnectivity( edgeIsland, edgeIsland.Count, out _ ) == ComponentConnectivityType.Loop )
			{
				for ( int i = 0; i < edgeIsland.Count; ++i )
				{
					if ( !loopEdges.ContainsKey( edgeIsland[i] ) )
						loopEdges.Add( edgeIsland[i], index++ );
				}
			}
		}

		// Look for open edge loops, or general edge loops.
		for ( int iEdge = 0; iEdge < originalEdges.Count; ++iEdge )
		{
			var hEdge = originalEdges[iEdge];

			// Skip any edges that are already in the list of edges used by the loops
			if ( loopEdges.ContainsKey( hEdge ) )
				continue;

			List<HalfEdgeHandle> edgeLoop;

			// First check to see if the edge is open, if so select the loop of open edges it is 
			// connected to, otherwise find the general edge loop to which the edge belongs.
			if ( IsEdgeOpen( hEdge ) )
			{
				FindOpenEdgeLoop( hEdge, out edgeLoop );
			}
			else
			{
				Topology.FindEdgeLoop( hEdge, -1, out edgeLoop );
			}

			if ( edgeLoop.Count > 1 )
			{
				// Find the sub-set of the loop containing the original edges
				if ( FindShortestEdgeLoopSubSetContainingEdges( edgeLoop, originalEdges, out edgeLoop ) == false )
				{
					subSetLoopEdges.AddRange( edgeLoop );
				}

				// Add whatever edge loop was found to the selection
				for ( int i = 0; i < edgeLoop.Count; ++i )
				{
					if ( !loopEdges.ContainsKey( edgeLoop[i] ) )
						loopEdges.Add( edgeLoop[i], index++ );
				}
			}
		}

		// Build a combine list of the original edges and loop edges, if there are any remaining isolated 
		// edge islands where all of the edges share a common face select the loop of that face.
		var allEdges = new List<HalfEdgeHandle>( originalEdges.Count + loopEdges.Count );
		for ( int iEdge = 0; iEdge < originalEdges.Count; ++iEdge )
		{
			if ( !loopEdges.ContainsKey( originalEdges[iEdge] ) )
			{
				allEdges.Add( originalEdges[iEdge] );
			}
		}

		var loopEdgesList = new HalfEdgeHandle[loopEdges.Count];
		foreach ( var edge in loopEdges )
			loopEdgesList[edge.Value] = edge.Key;

		allEdges.AddRange( loopEdgesList );

		Topology.FindEdgeIslands( allEdges, out edgeIslands );

		for ( int iIsland = 0; iIsland < edgeIslands.Count; ++iIsland )
		{
			var edgeIsland = edgeIslands[iIsland];
			int nNumIslandEdges = edgeIsland.Count;
			if ( nNumIslandEdges <= 0 )
				continue;

			// Skip the island if any of its edges are in the loop selection
			bool bEdgeInLoopSelection = false;
			for ( int iEdge = 0; iEdge < nNumIslandEdges; ++iEdge )
			{
				if ( loopEdges.ContainsKey( edgeIsland[iEdge] ) )
				{
					bEdgeInLoopSelection = true;
					break;
				}
			}
			if ( bEdgeInLoopSelection )
				continue;

			var hStartEdge = edgeIsland[0];
			FaceHandle hLoopFace;
			if ( nNumIslandEdges == 1 )
			{
				Topology.GetFacesConnectedToFullEdge( hStartEdge, out var hFaceA, out var hFaceB );

				// Compute the number of edges in each face and select the 
				// edge loop of the face which has more edges.
				int nNumEdgesFaceA = Topology.ComputeNumEdgesInFace( hFaceA );
				int nNumEdgesFaceB = Topology.ComputeNumEdgesInFace( hFaceB );
				hLoopFace = (nNumEdgesFaceA >= nNumEdgesFaceB) ? hFaceA : hFaceB;
			}
			else
			{
				// If all of the edges in the edgeIsland share a 
				// face add the loop of that face to the list.
				hLoopFace = FindFaceSharedEdges( edgeIsland );
			}

			if ( hLoopFace != FaceHandle.Invalid )
			{
				// Get the edges of the face and add them to the loop edges
				Topology.GetFullEdgesConnectedToFace( hLoopFace, out var faceEdgeLoop );

				int nNumFaceEdges = faceEdgeLoop.Count;
				int nStartIndex = faceEdgeLoop.IndexOf( hStartEdge );
				var orderedFaceLoop = new List<HalfEdgeHandle>( nNumFaceEdges );
				for ( int iEdge = nStartIndex; iEdge < nNumFaceEdges; ++iEdge )
				{
					orderedFaceLoop.Add( faceEdgeLoop[iEdge] );
				}
				for ( int iEdge = 0; iEdge < nStartIndex; ++iEdge )
				{
					orderedFaceLoop.Add( faceEdgeLoop[iEdge] );
				}

				// Find the sub-set of the loop containing the original edges
				if ( !FindShortestEdgeLoopSubSetContainingEdges( orderedFaceLoop, originalEdges, out faceEdgeLoop ) )
				{
					subSetLoopEdges.AddRange( faceEdgeLoop );
				}

				for ( int i = 0; i < faceEdgeLoop.Count; ++i )
				{
					if ( !loopEdges.ContainsKey( faceEdgeLoop[i] ) )
						loopEdges.Add( faceEdgeLoop[i], index++ );
				}
			}
		}

		// If there are sub-set loop edges use only those so that loops 
		// are not selected from existing connected edges.
		if ( subSetLoopEdges.Count > 0 )
		{
			int nNumSubsetLoopEdges = subSetLoopEdges.Count;
			loopEdges.Clear();
			loopEdges.EnsureCapacity( nNumSubsetLoopEdges );
			for ( int iEdge = 0; iEdge < nNumSubsetLoopEdges; ++iEdge )
			{
				if ( !loopEdges.ContainsKey( subSetLoopEdges[iEdge] ) )
					loopEdges.Add( subSetLoopEdges[iEdge], index++ );
			}
		}

		// Build the list of unique edges to be returned from the tree
		pOutEdgeLoopEdges = new HalfEdgeHandle[loopEdges.Count];
		foreach ( var edge in loopEdges )
			pOutEdgeLoopEdges[edge.Value] = edge.Key;
	}

	public void FindEdgeIslands( IReadOnlyList<HalfEdgeHandle> edgeList, out List<List<HalfEdgeHandle>> outEdgeList )
	{
		Topology.FindEdgeIslands( edgeList, out outEdgeList );
	}

	public int FindEdgeRibs( IReadOnlyList<HalfEdgeHandle> edges, out List<List<HalfEdgeHandle>> outLeftRibs, out List<List<HalfEdgeHandle>> outRightRibs )
	{
		var numRibs = Topology.FindEdgeRibs( edges, edges.Count, out var leftRibs, out var rightRibs, out _ );

		outLeftRibs = new List<List<HalfEdgeHandle>>();
		outRightRibs = new List<List<HalfEdgeHandle>>();

		for ( var i = 0; i < numRibs; i++ )
		{
			outLeftRibs.Add( new List<HalfEdgeHandle>() );
			outRightRibs.Add( new List<HalfEdgeHandle>() );
		}

		for ( var ribIndex = 0; ribIndex < numRibs; ++ribIndex )
		{
			var numLeftRibEdges = leftRibs[ribIndex].Count;
			var outLeftRibEdges = outLeftRibs[ribIndex];

			for ( var edgeIndex = 0; edgeIndex < numLeftRibEdges; ++edgeIndex )
			{
				outLeftRibEdges.Add( Topology.GetFullEdgeForHalfEdge( leftRibs[ribIndex][edgeIndex] ) );
			}

			var numRightRibEdges = rightRibs[ribIndex].Count;
			var outRightRibEdges = outRightRibs[ribIndex];

			for ( var edgeIndex = 0; edgeIndex < numRightRibEdges; ++edgeIndex )
			{
				outRightRibEdges.Add( Topology.GetFullEdgeForHalfEdge( rightRibs[ribIndex][edgeIndex] ) );
			}
		}

		return numRibs;
	}

	private bool FindShortestEdgeLoopSubSetContainingEdges( IReadOnlyList<HalfEdgeHandle> edgeLoop, IReadOnlyList<HalfEdgeHandle> originalEdges, out List<HalfEdgeHandle> pOutEdgeLoop )
	{
		var subSetEdgeList = new List<HalfEdgeHandle>();

		// Build a list of the original edges which are in the loop
		var originalEdgesInLoop = new List<HalfEdgeHandle>( Math.Min( edgeLoop.Count, originalEdges.Count ) );
		int nNumOriginalEdges = originalEdges.Count;
		for ( int iEdge = 0; iEdge < nNumOriginalEdges; ++iEdge )
		{
			if ( edgeLoop.Contains( originalEdges[iEdge] ) )
			{
				originalEdgesInLoop.Add( originalEdges[iEdge] );
			}
		}

		// Determine if all original edges in the list are connected or not. If they are all connected 
		// return the entire loop, but if they are disjoint return the smallest section of the loop 
		// which contains all of the original edges.
		var originalEdgesConnectivity = Topology.ClassifyEdgeListConnectivity( originalEdgesInLoop, originalEdgesInLoop.Count, out _ );

		if ( (originalEdgesConnectivity == ComponentConnectivityType.Mixed) ||
			 (originalEdgesConnectivity == ComponentConnectivityType.None) )
		{
			var loopConnectivity = Topology.ClassifyEdgeListConnectivity( edgeLoop, edgeLoop.Count, out _ );

			if ( loopConnectivity == ComponentConnectivityType.List )
			{
				int nFirstEdgeInLoop = -1;
				int nLastEdgeInLoop = 0;
				int nNumEdgesInLoop = edgeLoop.Count;
				for ( int iEdge = 0; iEdge < nNumEdgesInLoop; ++iEdge )
				{
					if ( originalEdgesInLoop.Contains( edgeLoop[iEdge] ) )
					{
						if ( nFirstEdgeInLoop < 0 )
						{
							nFirstEdgeInLoop = iEdge;
						}
						nLastEdgeInLoop = Math.Max( nLastEdgeInLoop, iEdge );
					}
				}

				subSetEdgeList.EnsureCapacity( nLastEdgeInLoop - nFirstEdgeInLoop + 1 );
				for ( int iEdge = nFirstEdgeInLoop; iEdge <= nLastEdgeInLoop; ++iEdge )
				{
					subSetEdgeList.Add( edgeLoop[iEdge] );
				}
			}
			else if ( loopConnectivity == ComponentConnectivityType.Loop )
			{
				int nCurrentGapStart = -1;
				int nCurrentGapEnd = -1;
				int nLargestGapStart = -1;
				int nLargestGapEnd = -1;
				int nLargestGapSize = -1;
				int nNumEdgesInLoop = edgeLoop.Count;
				int nCurrentGapSize;
				for ( int iEdge = 1; iEdge < nNumEdgesInLoop; ++iEdge )
				{
					if ( !originalEdgesInLoop.Contains( edgeLoop[iEdge] ) )
					{
						if ( nCurrentGapStart < 0 )
						{
							nCurrentGapStart = iEdge;
						}
						nCurrentGapEnd = iEdge;
					}
					else if ( nCurrentGapStart >= 0 )
					{
						nCurrentGapSize = (nCurrentGapEnd - nCurrentGapStart) + 1;
						if ( nCurrentGapSize > nLargestGapSize )
						{
							nLargestGapStart = nCurrentGapStart;
							nLargestGapEnd = nCurrentGapEnd;
							nLargestGapSize = nCurrentGapSize;
						}
						nCurrentGapStart = -1;
						nCurrentGapEnd = -1;
					}
				}

				nCurrentGapSize = (nCurrentGapEnd - nCurrentGapStart) + 1;
				if ( nCurrentGapSize > nLargestGapSize )
				{
					nLargestGapStart = nCurrentGapStart;
					nLargestGapEnd = nCurrentGapEnd;
					nLargestGapSize = nCurrentGapSize;
				}

				subSetEdgeList.EnsureCapacity( nNumEdgesInLoop - nLargestGapSize );
				for ( int iEdge = 0; iEdge < nNumEdgesInLoop; ++iEdge )
				{
					if ( (iEdge >= nLargestGapStart) && (iEdge <= nLargestGapEnd) )
						continue;

					subSetEdgeList.Add( edgeLoop[iEdge] );
				}
			}
		}
		else
		{
			pOutEdgeLoop = new List<HalfEdgeHandle>( edgeLoop );

			return true;
		}

		pOutEdgeLoop = subSetEdgeList;

		return false;
	}

	private void InterpolateVertexData( VertexHandle hDstVertex, VertexHandle hVertexA, VertexHandle hVertexB, float param )
	{
		if ( !hDstVertex.IsValid )
			return;

		if ( !hVertexA.IsValid )
			return;

		if ( !hVertexB.IsValid )
			return;

		var a = GetVertexPosition( hVertexA );
		var b = GetVertexPosition( hVertexB );
		SetVertexPosition( hDstVertex, a.LerpTo( b, param ) );
	}

	private void InterpolateFaceVertexData( HalfEdgeHandle hDstFaceVertex, HalfEdgeHandle hFaceVertexA, HalfEdgeHandle hFaceVertexB, float param )
	{
		if ( !hDstFaceVertex.IsValid )
			return;

		if ( !hFaceVertexA.IsValid )
			return;

		if ( !hFaceVertexB.IsValid )
			return;

		var a = TextureCoord[hFaceVertexA];
		var b = TextureCoord[hFaceVertexB];
		TextureCoord[hDstFaceVertex] = a.LerpTo( b, param );
	}

	/// <summary>
	/// Get start and end points of an edge
	/// </summary>
	public Line GetEdgeLine( HalfEdgeHandle hEdge )
	{
		if ( !hEdge.IsValid )
			return default;

		GetEdgeVertices( hEdge, out var hVertexA, out var hVertexB );
		var a = Positions[hVertexA];
		var b = Positions[hVertexB];

		return new Line( a, b );
	}

	/// <summary>
	/// Get the two vertices of this half edge
	/// </summary>
	public void GetEdgeVertices( HalfEdgeHandle hEdge, out VertexHandle hVertexA, out VertexHandle hVertexB )
	{
		Topology.GetVerticesConnectedToHalfEdge( hEdge, out hVertexA, out hVertexB );
	}

	/// <summary>
	/// Set the position of a vertex
	/// </summary>
	public void SetVertexPosition( VertexHandle hVertex, Vector3 position )
	{
		if ( !hVertex.IsValid )
			return;

		Positions[hVertex] = position;

		IsDirty = true;
	}

	/// <summary>
	/// Get the position of a vertex
	/// </summary>
	public Vector3 GetVertexPosition( VertexHandle hVertex )
	{
		return Positions[hVertex];
	}

	/// <summary>
	/// Get the position of a vertex
	/// </summary>
	public void GetVertexPosition( VertexHandle hVertex, Transform transform, out Vector3 outPosition )
	{
		outPosition = transform.PointToWorld( GetVertexPosition( hVertex ) );
	}

	/// <summary>
	/// Get the positions of all vertices
	/// </summary>
	public IEnumerable<Vector3> GetVertexPositions()
	{
		foreach ( var hVertex in Topology.VertexHandles )
			yield return Positions[hVertex];
	}

	/// <summary>
	/// Set the blend of a vertex
	/// </summary>
	public void SetVertexBlend( HalfEdgeHandle hFaceVertex, Color32 blend )
	{
		if ( !hFaceVertex.IsValid )
			return;

		Blends[hFaceVertex] = blend;

		_dirtyHalfEdges.Add( hFaceVertex );
	}

	/// <summary>
	/// Set the color of a vertex
	/// </summary>
	public void SetVertexColor( HalfEdgeHandle hFaceVertex, Color32 color )
	{
		if ( !hFaceVertex.IsValid )
			return;

		Colors[hFaceVertex] = color;

		_dirtyHalfEdges.Add( hFaceVertex );
	}

	/// <summary>
	/// Get the color of a vertex
	/// </summary>
	public Color32 GetVertexColor( HalfEdgeHandle hFaceVertex )
	{
		if ( !hFaceVertex.IsValid )
			return default;

		return Colors[hFaceVertex];
	}

	/// <summary>
	/// Get the blend of a vertex
	/// </summary>
	public Color32 GetVertexBlend( HalfEdgeHandle hFaceVertex )
	{
		if ( !hFaceVertex.IsValid )
			return default;

		return Blends[hFaceVertex];
	}

	public void ComputeFaceNormal( FaceHandle hFace, out Vector3 pOutNormal )
	{
		var positions = GetFaceVertexPositions( hFace, Transform.Zero ).ToArray();
		PlaneEquation( positions, out pOutNormal, out _ );
	}

	/// <summary>
	/// Calculate the center point of a face
	/// </summary>
	public Vector3 GetFaceCenter( FaceHandle hFace )
	{
		var centroid = Vector3.Zero;
		int count = 0;
		foreach ( var i in Topology.GetFaceVertices( hFace ) )
		{
			centroid += Positions[i];
			count++;
		}
		centroid *= 1f / count;
		return centroid;
	}

	/// <summary>
	/// Get the start and end points of all edges
	/// </summary>
	public IEnumerable<Line> GetEdges()
	{
		foreach ( var hEdge in Topology.HalfEdgeHandles )
		{
			if ( hEdge.Index > Topology.GetOppositeHalfEdge( hEdge ).Index )
				continue;

			yield return GetEdgeLine( hEdge );
		}
	}

	public IEnumerable<Vector3> GetFaceVertexPositions( FaceHandle hFace, Transform transform )
	{
		var hFirstFaceVertex = Topology.GetFirstEdgeInFaceLoop( hFace );
		var hCurrentFaceVertex = hFirstFaceVertex;

		do
		{
			var hVertex = Topology.GetEndVertexConnectedToEdge( hCurrentFaceVertex );
			yield return transform.PointToWorld( GetVertexPosition( hVertex ) );
			hCurrentFaceVertex = Topology.GetNextEdgeInFaceLoop( hCurrentFaceVertex );
		}
		while ( hCurrentFaceVertex != hFirstFaceVertex );
	}

	public bool GetFaceVerticesConnectedToFace( FaceHandle hFace, out HalfEdgeHandle[] hEdges )
	{
		return Topology.GetHalfEdgesConnectedToFace( hFace, out hEdges );
	}

	public VertexHandle GetVertexConnectedToFaceVertex( HalfEdgeHandle hFaceVertex )
	{
		return Topology.GetEndVertexConnectedToEdge( hFaceVertex );
	}

	public void ComputeFaceTextureParametersFromCoordinates()
	{
		ComputeFaceTextureParametersFromCoordinates( FaceHandles );
	}

	public void ComputeFaceTextureParametersFromCoordinates( IEnumerable<FaceHandle> faces )
	{
		var textureSizes = Materials.Select( CalculateTextureSize ).ToArray();
		ComputeFaceTextureParametersFromCoordinates( faces, textureSizes, Transform );
	}

	public void ComputeFaceTextureCoordinatesFromParameters()
	{
		ComputeFaceTextureCoordinatesFromParameters( FaceHandles );
	}

	public void ComputeFaceTextureCoordinatesFromParameters( Transform transform )
	{
		var textureSizes = Materials.Select( CalculateTextureSize ).ToArray();
		ComputeFaceTextureCoordinatesFromParameters( FaceHandles, transform, textureSizes, 0.25f );
	}

	public void ComputeFaceTextureCoordinatesFromParameters( IEnumerable<FaceHandle> faces )
	{
		var textureSizes = Materials.Select( CalculateTextureSize ).ToArray();
		ComputeFaceTextureCoordinatesFromParameters( faces, Transform, textureSizes, 0.25f );
	}

	public void ComputeFaceTextureParametersFromCoordinates( IEnumerable<FaceHandle> faces, IReadOnlyList<Vector2> textureSizes, Transform transform )
	{
		foreach ( var hFace in faces )
		{
			if ( !hFace.IsValid )
				continue;

			GetFaceVerticesConnectedToFace( hFace, out var faceVertices );

			var facePositions = new List<Vector3>();
			var faceTexCoords = new List<Vector2>();

			var nNumVertices = faceVertices.Length;

			for ( var iVertex = 0; iVertex < nNumVertices; ++iVertex )
			{
				var hFaceVertex = faceVertices[iVertex];
				var hVertex = GetVertexConnectedToFaceVertex( hFaceVertex );

				var vLocalPosition = Positions[hVertex];
				var vWorldPosition = transform.PointToWorld( vLocalPosition );

				facePositions.Add( vWorldPosition );
				faceTexCoords.Add( TextureCoord[hFaceVertex] );
			}

			GetBestThreeTextureBasisVerticies( facePositions, faceTexCoords, nNumVertices, out var bestPositions, out var bestTexCoords );

			if ( CalcTextureBasisFromUVs( bestPositions, bestTexCoords, out _, out _ ) )
			{
				var materialIndex = MaterialIndex[hFace];
				var textureSize = materialIndex >= 0 && materialIndex < textureSizes.Count ? textureSizes[materialIndex] : DefaultTextureSize;
				ComputeFaceTextureParametersFromUVs( bestPositions, bestTexCoords, textureSize,
					out var axisU, out var axisV, out var scale );

				TextureUAxis[hFace] = (Vector3)axisU;
				TextureVAxis[hFace] = (Vector3)axisV;
				TextureOffset[hFace] = new Vector2( axisU.w, axisV.w );
				TextureScale[hFace] = scale;
			}
			else
			{
				TextureUAxis[hFace] = new Vector3( 1.0f, 0.0f, 0.0f );
				TextureVAxis[hFace] = new Vector3( 0.0f, 1.0f, 0.0f );
				TextureOffset[hFace] = Vector2.Zero;
				TextureScale[hFace] = Vector2.One;
			}
		}
	}

	public void ComputeFaceTextureCoordinatesFromParameters( IEnumerable<FaceHandle> faces, Transform transform, IReadOnlyList<Vector2> textureSizes, float defaultScale )
	{
		foreach ( var hFace in faces )
		{
			if ( !hFace.IsValid )
				continue;

			var materialIndex = MaterialIndex[hFace];
			var textureSize = materialIndex >= 0 && materialIndex < textureSizes.Count ? textureSizes[materialIndex] : DefaultTextureSize;

			var axisU = TextureUAxis[hFace];
			var axisV = TextureVAxis[hFace];
			var scale = TextureScale[hFace];
			var offset = TextureOffset[hFace];

			scale.x = (scale.x.AlmostEqual( 0.0f ) || float.IsNaN( scale.x )) ? defaultScale : scale.x;
			scale.y = (scale.y.AlmostEqual( 0.0f ) || float.IsNaN( scale.y )) ? defaultScale : scale.y;

			GetFaceVerticesConnectedToFace( hFace, out var faceVertices );
			var numVertices = faceVertices.Length;

			for ( var iVertex = 0; iVertex < numVertices; ++iVertex )
			{
				var hFaceVertex = faceVertices[iVertex];
				var hVertex = GetVertexConnectedToFaceVertex( hFaceVertex );

				var position = Positions[hVertex];
				var worldPos = transform.PointToWorld( position );

				var u = Vector3.Dot( axisU, worldPos ) / scale.x + offset.x;
				var v = Vector3.Dot( axisV, worldPos ) / scale.y + offset.y;

				var texCoord = new Vector2( u / MathF.Max( textureSize.x, 1.0f ), v / MathF.Max( textureSize.y, 1.0f ) );
				TextureCoord[hFaceVertex] = texCoord;
			}
		}

		IsDirty = true;
	}

	private static bool CalcTextureBasisFromUVs( Vector3[] vVertPos, Vector2[] vTexCoord, out Vector3 vOutU, out Vector3 vOutV )
	{
		const float flEpsilon = 0.000001f;

		vOutU = new Vector3( 1.0f, 0.0f, 0.0f );
		vOutV = new Vector3( 0.0f, 1.0f, 0.0f );

		Vector3[] E = { vVertPos[1] - vVertPos[0], vVertPos[2] - vVertPos[0] };
		Vector2[] T = { vTexCoord[1] - vTexCoord[0], vTexCoord[2] - vTexCoord[0] };

		if ( T[0].LengthSquared < flEpsilon && T[1].LengthSquared < flEpsilon )
			return false;

		var eDet = T[0].x * T[1].y - T[1].x * T[0].y;
		if ( MathF.Abs( eDet ) < flEpsilon )
			eDet = flEpsilon;

		var textureU = 1.0f / eDet * (T[1].y * E[0] - T[0].y * E[1]);
		var textureV = 1.0f / eDet * (-T[1].x * E[0] + T[0].x * E[1]);
		var textureNormal = Vector3.Cross( textureU, textureV );

		var mTextureToWorld = new System.Numerics.Matrix4x4(
			textureU.x, textureV.x, textureNormal.x, 0,
			textureU.y, textureV.y, textureNormal.y, 0,
			textureU.z, textureV.z, textureNormal.z, 0,
			0, 0, 0, 1 );

		if ( System.Numerics.Matrix4x4.Invert( mTextureToWorld, out var mWorldToTexture ) )
		{
			vOutU = new Vector3( mWorldToTexture.M11, mWorldToTexture.M12, mWorldToTexture.M13 );
			vOutV = new Vector3( mWorldToTexture.M21, mWorldToTexture.M22, mWorldToTexture.M23 );
			return true;
		}

		return false;
	}

	private static void ComputeFaceTextureParametersFromUVs( Vector3[] vVertPos, Vector2[] vTexCoord, Vector2 vTextureDimensions, out Vector4 pAxisU, out Vector4 pAxisV, out Vector2 pScale )
	{
		if ( !CalcTextureBasisFromUVs( vVertPos, vTexCoord, out var vWorldU, out var vWorldV ) )
		{
			pAxisU = default;
			pAxisV = default;
			pScale = default;

			return;
		}

		var flWorldUScale = vWorldU.Length;
		var flWorldVScale = vWorldV.Length;

		vWorldU = vWorldU.Normal;
		vWorldV = vWorldV.Normal;

		pScale = new Vector2( 1.0f / (vTextureDimensions.x * flWorldUScale), 1.0f / (vTextureDimensions.y * flWorldVScale) );

		pAxisU = new Vector4( vWorldU );
		pAxisV = new Vector4( vWorldV );

		var vWorldOffset = new Vector2(
			Vector3.Dot( vWorldU, vVertPos[0] ) * flWorldUScale,
			Vector3.Dot( vWorldV, vVertPos[0] ) * flWorldVScale );

		var vWorldOffsetFrac = new Vector2( vWorldOffset.x - (int)vWorldOffset.x, vWorldOffset.y - (int)vWorldOffset.y );
		var vTexCoordFrac = new Vector2( vTexCoord[0].x - (int)vTexCoord[0].x, vTexCoord[0].y - (int)vTexCoord[0].y );

		var uvOffset = vTexCoordFrac - vWorldOffsetFrac;
		uvOffset.x -= (int)uvOffset.x;
		uvOffset.y -= (int)uvOffset.y;
		if ( uvOffset.x < 0 ) uvOffset.x += 1.0f;
		if ( uvOffset.y < 0 ) uvOffset.y += 1.0f;

		var uOffset = uvOffset.x * vTextureDimensions.x;
		var vOffset = uvOffset.y * vTextureDimensions.y;
		if ( uOffset >= vTextureDimensions.x ) uOffset -= vTextureDimensions.x;
		if ( vOffset >= vTextureDimensions.y ) vOffset -= vTextureDimensions.y;

		pAxisU.w = uOffset;
		pAxisV.w = vOffset;
	}

	private static void GetBestThreeTextureBasisVerticies( IReadOnlyList<Vector3> pPositions, IReadOnlyList<Vector2> pTexCoords, int nNumPositions, out Vector3[] pOutPositions, out Vector2[] pOutTexCoords )
	{
		pOutPositions = new Vector3[3];
		pOutTexCoords = new Vector2[3];

		if ( nNumPositions < 3 )
			return;

		var nBestVert = 0;
		var flBestHeuristic = -1.0f;

		for ( var i = 0; i < nNumPositions; ++i )
		{
			var nVert0 = (i + nNumPositions - 1) % nNumPositions;
			var nVert1 = i;
			var nVert2 = (i + 1) % nNumPositions;

			var vEdge0 = pPositions[nVert0] - pPositions[nVert1];
			var vEdge1 = pPositions[nVert2] - pPositions[nVert1];
			var flOneMinsDot = (1.0f - MathF.Abs( vEdge0.Normal.Dot( vEdge1.Normal ) ));

			var flHeuristic = vEdge0.LengthSquared * vEdge1.LengthSquared * flOneMinsDot;
			if ( flHeuristic > flBestHeuristic )
			{
				nBestVert = nVert1;
				flBestHeuristic = flHeuristic;
			}
		}

		var nPrevVert = (nBestVert + nNumPositions - 1) % nNumPositions;
		var nNextVert = (nBestVert + 1) % nNumPositions;

		pOutPositions[0] = pPositions[nBestVert];
		pOutPositions[1] = pPositions[nPrevVert];
		pOutPositions[2] = pPositions[nNextVert];

		pOutTexCoords[0] = pTexCoords[nBestVert];
		pOutTexCoords[1] = pTexCoords[nPrevVert];
		pOutTexCoords[2] = pTexCoords[nNextVert];
	}

	/// <summary>
	/// Transform all the vertices
	/// </summary>
	public void ApplyTransform( Transform transform )
	{
		foreach ( var hVertex in VertexHandles )
		{
			SetVertexPosition( hVertex, transform.PointToWorld( GetVertexPosition( hVertex ) ) );
		}
	}

	/// <summary>
	/// Get all edge handles of a face
	/// </summary>
	public HalfEdgeHandle[] GetFaceEdges( FaceHandle hFace )
	{
		GetEdgesConnectedToFace( hFace, out var edges );
		return edges.ToArray();
	}

	/// <summary>
	/// Get all vertex handles of a face
	/// </summary>
	public VertexHandle[] GetFaceVertices( FaceHandle hFace )
	{
		return Topology.GetFaceVertices( hFace );
	}

	/// <summary>
	/// Get texture offset of a face
	/// </summary>
	public Vector2 GetTextureOffset( FaceHandle hFace )
	{
		return TextureOffset[hFace];
	}

	/// <summary>
	/// Set texture offset of a face
	/// </summary>
	public void SetTextureOffset( FaceHandle hFace, Vector2 offset )
	{
		TextureOffset[hFace] = offset;

		ComputeFaceTextureCoordinatesFromParameters( new[] { hFace } );

		IsDirty = true;
	}

	/// <summary>
	/// Get texture scale of a face
	/// </summary>
	public Vector2 GetTextureScale( FaceHandle hFace )
	{
		return TextureScale[hFace];
	}

	/// <summary>
	/// Set texture scale of a face
	/// </summary>
	public void SetTextureScale( FaceHandle hFace, Vector2 scale )
	{
		TextureScale[hFace] = scale;

		ComputeFaceTextureCoordinatesFromParameters( new[] { hFace } );

		IsDirty = true;
	}

	/// <summary>
	/// Align face texture properties to grid
	/// </summary>
	public void TextureAlignToGrid( Transform transform, FaceHandle hFace )
	{
		TextureOffset[hFace] = 0;
		TextureScale[hFace] = 0.25f;

		GetFacePlane( hFace, transform, out var plane );

		var normal = plane.Normal;
		ComputeTextureAxes( normal, out var uAxis, out var vAxis );

		TextureUAxis[hFace] = uAxis;
		TextureVAxis[hFace] = vAxis;

		ComputeFaceTextureCoordinatesFromParameters( new[] { hFace } );

		IsDirty = true;
	}

	/// <summary>
	/// Align face texture properties to face
	/// </summary>
	public void TextureAlignToFace( Transform transform, FaceHandle hFace )
	{
		TextureOffset[hFace] = 0;
		TextureScale[hFace] = 0.25f;

		GetFacePlane( hFace, transform, out var plane );

		var normal = plane.Normal;
		var orientation = GetOrientationForPlane( normal );
		var vAxis = FaceDownVectors[orientation];
		var uAxis = normal.Cross( vAxis ).Normal;
		vAxis = uAxis.Cross( normal ).Normal;

		TextureUAxis[hFace] = uAxis;
		TextureVAxis[hFace] = vAxis;

		ComputeFaceTextureCoordinatesFromParameters( [hFace] );

		IsDirty = true;
	}

	/// <summary>
	/// Set face vertex texture coord
	/// </summary>
	public void SetTextureCoord( HalfEdgeHandle faceVertex, Vector2 texcoord )
	{
		TextureCoord[faceVertex] = texcoord;

		IsDirty = true;
	}

	/// <summary>
	/// Get face vertex texture coord
	/// </summary>
	public Vector2 GetTextureCoord( HalfEdgeHandle faceVertex )
	{
		return TextureCoord[faceVertex];
	}

	/// <summary>
	/// Set face texture coords
	/// </summary>
	public void SetFaceTextureCoords( FaceHandle hFace, IReadOnlyList<Vector2> texcoords )
	{
		if ( texcoords is null )
			return;

		if ( texcoords.Count <= 0 )
			return;

		if ( !hFace.IsValid )
			return;

		GetFaceVerticesConnectedToFace( hFace, out var hEdges );
		var numCoords = Math.Min( texcoords.Count, hEdges.Length );
		if ( numCoords == 0 )
			return;

		for ( var i = 0; i < numCoords; i++ )
		{
			TextureCoord[hEdges[i]] = texcoords[i];
		}

		ComputeFaceTextureParametersFromCoordinates( new[] { hFace } );

		IsDirty = true;
	}

	public Vector2[] GetFaceTextureCoords( FaceHandle hFace )
	{
		if ( !hFace.IsValid )
			return Array.Empty<Vector2>();
		GetFaceVerticesConnectedToFace( hFace, out var hEdges );
		var texcoords = new Vector2[hEdges.Length];
		for ( int i = 0; i < hEdges.Length; i++ )
		{
			texcoords[i] = TextureCoord[hEdges[i]];
		}
		return texcoords;
	}

	/// <summary>
	/// Set face texture properties
	/// </summary>
	public void SetFaceTextureParameters( FaceHandle hFace, Vector2 offset, Vector3 uAxis, Vector3 vAxis )
	{
		TextureOffset[hFace] = offset;
		TextureScale[hFace] = 0.25f;
		TextureUAxis[hFace] = uAxis;
		TextureVAxis[hFace] = vAxis;

		ComputeFaceTextureCoordinatesFromParameters( new[] { hFace } );

		IsDirty = true;
	}

	public void GetFaceTextureParameters( FaceHandle hFace, out Vector4 outAxisU, out Vector4 outAxisV, out Vector2 outScale )
	{
		var offset = TextureOffset[hFace];
		outAxisU = new Vector4( TextureUAxis[hFace], offset.x );
		outAxisV = new Vector4( TextureVAxis[hFace], offset.y );
		outScale = TextureScale[hFace];
	}

	public void SetFaceTextureParameters( FaceHandle hFace, Vector4 axisU, Vector4 axisV, Vector2 scale )
	{
		TextureUAxis[hFace] = (Vector3)axisU;
		TextureVAxis[hFace] = (Vector3)axisV;
		TextureOffset[hFace] = new Vector2( axisU.w, axisV.w );
		TextureScale[hFace] = scale;

		ComputeFaceTextureCoordinatesFromParameters( new[] { hFace } );

		IsDirty = true;
	}

	/// <summary>
	/// Align all face texture properties to grid
	/// </summary>
	public void TextureAlignToGrid( Transform transform )
	{
		foreach ( var hFace in Topology.FaceHandles )
		{
			TextureAlignToGrid( transform, hFace );
		}

		IsDirty = true;
	}

	/// <summary>
	/// Remove these faces
	/// </summary>
	public void RemoveFaces( IEnumerable<FaceHandle> hFaces )
	{
		foreach ( var hFace in hFaces )
		{
			Topology.RemoveFace( hFace, true );
		}

		IsDirty = true;
	}

	/// <summary>
	/// Remove these vertices
	/// </summary>
	public void RemoveVertices( IEnumerable<VertexHandle> hVertices )
	{
		foreach ( var hVertex in hVertices )
		{
			Topology.RemoveVertex( hVertex, true );
		}

		IsDirty = true;
	}

	/// <summary>
	/// Remove these edges
	/// </summary>
	public void RemoveEdges( IEnumerable<HalfEdgeHandle> hEdges )
	{
		foreach ( var hEdge in hEdges )
		{
			Topology.RemoveEdge( hEdge, true );
		}

		IsDirty = true;
	}

	public bool CollapseEdge( HalfEdgeHandle hEdge, out VertexHandle pOutNewVertex, out List<(HalfEdgeHandle, HalfEdgeHandle)> pOutReplacedEdges )
	{
		Topology.GetHalfEdgesConnectedToFullEdge( hEdge, out var hHalfEdgeA, out var _ );
		return CollapseEdge( hHalfEdgeA, 0.5f, out pOutNewVertex, out pOutReplacedEdges );
	}

	public bool CollapseEdge( HalfEdgeHandle hHalfEdgeA, float flParam, out VertexHandle pOutNewVertex, out List<(HalfEdgeHandle, HalfEdgeHandle)> pOutReplacedEdges )
	{
		var hHalfEdgeB = Topology.GetOppositeHalfEdge( hHalfEdgeA );
		var hVertexA = Topology.GetEndVertexConnectedToEdge( hHalfEdgeB );
		var hVertexB = Topology.GetEndVertexConnectedToEdge( hHalfEdgeA );
		var newVertex = GetVertexPosition( hVertexA ).LerpTo( GetVertexPosition( hVertexB ), flParam );
		var hEdge = Topology.GetFullEdgeForHalfEdge( hHalfEdgeA );
		var bRemoved = Topology.CollapseEdge( hEdge, out var hNewVertex, out pOutReplacedEdges );

		if ( hNewVertex is not null && hNewVertex.IsValid )
			SetVertexPosition( hNewVertex, newVertex );

		pOutNewVertex = hNewVertex;

		IsDirty = true;

		return bRemoved;
	}

	public bool CollapseFace( FaceHandle hFace, out VertexHandle hOutVertex )
	{
		hOutVertex = null;

		if ( !hFace.IsValid )
			return false;

		var nNumVerticesInFace = Topology.ComputeNumEdgesInFace( hFace );
		var nIndex = 0;
		var hFirstEdge = Topology.GetFirstEdgeInFaceLoop( hFace );
		var hEdge = hFirstEdge;
		var newVertex = Vector3.Zero;
		do
		{
			Assert.True( nIndex < nNumVerticesInFace );
			if ( nIndex >= nNumVerticesInFace )
				break;

			newVertex += GetVertexPosition( Topology.GetEndVertexConnectedToEdge( hEdge ) );
			++nIndex;

			hEdge = Topology.GetNextEdgeInFaceLoop( hEdge );
		}
		while ( hEdge != hFirstEdge );

		if ( !Topology.CollapseFace( hFace, out hOutVertex ) )
			return false;

		newVertex /= nNumVerticesInFace;
		SetVertexPosition( hOutVertex, newVertex );

		IsDirty = true;

		return true;
	}

	public bool SplitEdges( IReadOnlyList<HalfEdgeHandle> edges, out HalfEdgeHandle[] newEdgesA, out HalfEdgeHandle[] pOutNewEdgesB )
	{
		return Topology.SplitEdges( edges, out newEdgesA, out pOutNewEdgesB );
	}

	public void CollapseEdges( IReadOnlyList<HalfEdgeHandle> edges )
	{
		Topology.FindEdgeIslands( edges, out var edgeIslands );
		var nNumEdgeIslands = edgeIslands.Count;
		var nNumEdges = edges.Count;
		var edgeIslandsAvergeVertex = new Vector3[nNumEdges];
		{
			for ( var iGroup = 0; iGroup < nNumEdgeIslands; ++iGroup )
			{
				var edgeGroup = edgeIslands[iGroup];
				Topology.FindVerticesConnectedToFullEdges( edgeGroup, out var connectedVertices );

				var averageVertex = Vector3.Zero;
				foreach ( var p in connectedVertices )
					averageVertex += GetVertexPosition( p );

				averageVertex /= connectedVertices.Length;
				edgeIslandsAvergeVertex[iGroup] = averageVertex;
			}
		}

		//
		// Collapse all of the edges in each group and apply the averaged values to the final vertex 
		//
		for ( int iGroup = 0; iGroup < nNumEdgeIslands; ++iGroup )
		{
			var edgeGroup = edgeIslands[iGroup];
			var nNumEdgesInGroup = edgeGroup.Count;
			var hGroupVertex = VertexHandle.Invalid;

			for ( var iEdge = 0; iEdge < nNumEdgesInGroup; ++iEdge )
			{
				var hEdge = edgeGroup[iEdge];

				if ( CollapseEdge( hEdge, out var hEdgeCollapseVertex, out var replacedEdges ) )
				{
					// If any additional edges were replaced by the collapse of the current edge,
					// search the group for the replaced edges and re-map the them to the new edge.
					var nNumReplacedEdges = replacedEdges.Count;
					for ( var iReplacedEdge = 0; iReplacedEdge < nNumReplacedEdges; ++iReplacedEdge )
					{
						var nEdgeInGroup = edgeGroup.IndexOf( replacedEdges[iReplacedEdge].Item1 );
						if ( nEdgeInGroup != -1 )
						{
							edgeGroup[nEdgeInGroup] = replacedEdges[iReplacedEdge].Item2;
						}
					}

					// Update the final vertex the group of edges was collapsed to
					if ( hEdgeCollapseVertex.IsValid )
					{
						hGroupVertex = hEdgeCollapseVertex;
					}
				}
			}

			if ( hGroupVertex.IsValid )
			{
				var groupAverageVertex = edgeIslandsAvergeVertex[iGroup];
				SetVertexPosition( hGroupVertex, groupAverageVertex );
			}
		}

		IsDirty = true;
	}

	struct MeshVertexRef
	{
		public int SubmeshIndex;
		public int VertexIndex;
	}

	readonly Dictionary<HalfEdgeHandle, List<MeshVertexRef>> _halfEdgeToMeshVertices = [];
	readonly HashSet<HalfEdgeHandle> _dirtyHalfEdges = [];
	readonly List<Submesh> _submeshes = [];

	internal void UpdateVertexData()
	{
		if ( _dirtyHalfEdges.Count == 0 ) return;

		var dirtySubmeshes = new HashSet<int>();

		foreach ( var hEdge in _dirtyHalfEdges )
		{
			var blend = Blends[hEdge];
			var color = Colors[hEdge];

			if ( _halfEdgeToMeshVertices.TryGetValue( hEdge, out var refs ) )
			{
				foreach ( var vertex in refs )
				{
					var vertices = _submeshes[vertex.SubmeshIndex].Vertices;
					var v = vertices[vertex.VertexIndex];
					v.Blend = blend;
					v.Color = color;
					vertices[vertex.VertexIndex] = v;

					dirtySubmeshes.Add( vertex.SubmeshIndex );
				}
			}
		}

		foreach ( var submeshIndex in dirtySubmeshes )
		{
			var submesh = _submeshes[submeshIndex];
			var mesh = submesh.Mesh;
			mesh.SetVertexBufferData( submesh.Vertices );
		}

		_dirtyHalfEdges.Clear();
	}

	/// <summary>
	/// Triangulate the polygons into a model
	/// </summary>
	public Model Rebuild()
	{
		_triangleFaces.Clear();
		_meshIndices.Clear();
		_meshVertices.Clear();
		_meshFaces.Clear();
		_meshTriangleMaterials.Clear();

		var builder = Model.Builder;
		var submeshes = new Dictionary<int, Submesh>();

		_halfEdgeToMeshVertices.Clear();

		foreach ( var hFace in Topology.FaceHandles )
		{
			var materialId = MaterialIndex[hFace];
			var material = GetMaterial( MaterialIndex[hFace] );
			if ( !submeshes.TryGetValue( materialId, out var submesh ) )
			{
				submesh = new()
				{
					Material = material,
					Index = submeshes.Count,
				};

				submeshes.Add( materialId, submesh );

				builder.AddSurface( material?.Surface );
			}

			TriangulateFace( hFace, submesh );
		}

		_submeshes.Clear();
		_submeshes.AddRange( submeshes.Values );

		if ( _meshVertices.Count >= 3 && _meshIndices.Count >= 3 )
		{
			builder.AddCollisionHull( _meshVertices );
			builder.AddCollisionMesh( _meshVertices, _meshIndices, _meshTriangleMaterials );
			builder.AddTraceMesh( _meshVertices, _meshIndices );
		}

		foreach ( var submesh in submeshes.Values )
		{
			var vertices = submesh.Vertices;
			var indices = submesh.Indices;

			if ( vertices.Count < 3 )
				continue;

			if ( indices.Count < 3 )
				continue;

			var bounds = BBox.FromPoints( vertices.Select( x => x.Position ) );
			var material = submesh.Material ?? DefaultMaterial;
			var mesh = new Mesh( material );
			mesh.CreateVertexBuffer( vertices.Count, vertices );
			mesh.CreateIndexBuffer( indices.Count, indices );
			mesh.Bounds = bounds;

			var uvDensity = submesh.UvDensity;
			if ( uvDensity.Count > 0 )
			{
				uvDensity.Sort();
				mesh.UvDensity = uvDensity[2 * (uvDensity.Count - 1) / 10];
			}

			builder.AddMesh( mesh );

			submesh.Mesh = mesh;
		}

		IsDirty = false;

		return builder.Create();
	}

	private int AddMaterial( Material material )
	{
		if ( material is null )
			return -1;

		if ( _materialIdsByName.TryGetValue( material.Name, out var existingId ) )
			return existingId;

		int id = _materialId++;
		_materialsById[id] = material;
		_materialIdsByName[material.Name] = id;
		return id;
	}

	private Material GetMaterial( int id )
	{
		_materialsById.TryGetValue( id, out var material );
		return material;
	}

	private static Vector2 CalculateTextureSize( Material material )
	{
		Vector2 textureSize = 512;
		if ( material is null )
			return textureSize;

		var width = material.Attributes.GetInt( "WorldMappingWidth" );
		var height = material.Attributes.GetInt( "WorldMappingHeight" );
		var texture = material.FirstTexture;
		if ( texture != null )
		{
			textureSize = texture.Size;
			if ( width > 0 ) textureSize.x = width / 0.25f;
			if ( height > 0 ) textureSize.y = height / 0.25f;
		}

		return textureSize;
	}

	public bool IsEdgeSmooth( HalfEdgeHandle hEdge )
	{
		if ( IsEdgeOpen( hEdge ) )
			return false;

		const float epsilon = 0.00001f;

		if ( _smoothingThreshold > (1.0f - epsilon) )
			return false;

		var mode = GetEdgeSmoothing( hEdge );

		if ( mode == EdgeSmoothMode.Hard )
			return false;

		if ( mode == EdgeSmoothMode.Soft )
			return true;

		if ( _smoothingThreshold < epsilon )
			return true;

		var hFaceA = hEdge.Face;
		if ( !hFaceA.IsValid )
			return false;

		var hFaceB = Topology.GetOppositeHalfEdge( hEdge ).Face;
		if ( !hFaceB.IsValid )
			return false;

		ComputeFaceNormal( hFaceA, out var vNormalA );
		ComputeFaceNormal( hFaceB, out var vNormalB );

		return (vNormalA.Dot( vNormalB ) > (_smoothingThreshold + epsilon));
	}

	private HalfEdgeHandle GetNextFaceVertexConnectedToVertex( HalfEdgeHandle hEdge )
	{
		return Topology.GetOppositeHalfEdge( hEdge.NextEdge );
	}

	public bool GetEdgesConnectedToFace( FaceHandle hFace, out List<HalfEdgeHandle> edges )
	{
		return Topology.GetFullEdgesConnectedToFace( hFace, out edges );
	}

	public bool GetVerticesConnectedToEdge( HalfEdgeHandle hEdge, FaceHandle hFace, out VertexHandle hOutVertexA, out VertexHandle hOutVertexB )
	{
		hOutVertexA = VertexHandle.Invalid;
		hOutVertexB = VertexHandle.Invalid;

		Topology.GetHalfEdgesConnectedToFullEdge( hEdge, out var hHalfEdgeA, out var hHalfEdgeB );

		if ( Topology.GetFaceConnectedToHalfEdge( hHalfEdgeA ) == hFace )
		{
			Topology.GetVerticesConnectedToHalfEdge( hHalfEdgeA, out hOutVertexA, out hOutVertexB );
			return true;
		}

		if ( Topology.GetFaceConnectedToHalfEdge( hHalfEdgeB ) == hFace )
		{
			Topology.GetVerticesConnectedToHalfEdge( hHalfEdgeB, out hOutVertexA, out hOutVertexB );
			return true;
		}

		return false;
	}

	public void GetVerticesConnectedToEdge( HalfEdgeHandle hEdge, out VertexHandle hOutVertexA, out VertexHandle hOutVertexB )
	{
		Topology.GetVerticesConnectedToFullEdge( hEdge, out hOutVertexA, out hOutVertexB );
	}

	public void GetEdgeVertexPositions( HalfEdgeHandle hEdge, Transform transform, out Vector3 outVertexA, out Vector3 outVertexB )
	{
		GetVerticesConnectedToEdge( hEdge, out var hVertexA, out var hVertexB );
		outVertexA = transform.PointToWorld( GetVertexPosition( hVertexA ) );
		outVertexB = transform.PointToWorld( GetVertexPosition( hVertexB ) );
	}

	private HalfEdgeHandle FindEdgeConnectingFaces( FaceHandle hFaceA, FaceHandle hFaceB )
	{
		return Topology.FindEdgeConnectingFaces( hFaceA, hFaceB );
	}

	public FaceHandle GetOppositeFaceConnectedToEdge( HalfEdgeHandle hEdge, FaceHandle hFace )
	{
		return Topology.GetOppositeFaceConnectedToFullEdge( hEdge, hFace );
	}

	private void FindVerticesConnectedToEdges( IReadOnlyList<HalfEdgeHandle> edgeList, out VertexHandle[] outVertices )
	{
		Topology.FindVerticesConnectedToFullEdges( edgeList, out outVertices );
	}

	private void FindFacesConnectedToVertices( IReadOnlyList<VertexHandle> hVertices, int nNumVertices, out FaceHandle[] newFaces, out int[] faceVertexCounts )
	{
		Topology.FindFacesConnectedToVertices( hVertices, nNumVertices, out newFaces, out faceVertexCounts );
	}

	private void FindEdgesConnectedToFaces( IReadOnlyList<FaceHandle> pFaceList, int nNumFaces, out HalfEdgeHandle[] newEdges, out int[] edgeFaceCounts )
	{
		Topology.FindFullEdgesConnectedToFaces( pFaceList, nNumFaces, out newEdges, out edgeFaceCounts );
	}

	private HalfEdgeHandle FindOppositeEdgeInFace( FaceHandle hFace, HalfEdgeHandle hEdge )
	{
		Topology.GetHalfEdgesConnectedToFullEdge( hEdge, out var hHalfEdgeA, out var hHalfEdgeB );

		if ( Topology.GetFaceConnectedToHalfEdge( hHalfEdgeA ) == hFace )
		{
			return Topology.GetFullEdgeForHalfEdge( Topology.FindOppositeHalfEdgeInFace( hHalfEdgeA ) );
		}
		else if ( Topology.GetFaceConnectedToHalfEdge( hHalfEdgeB ) == hFace )
		{
			return Topology.GetFullEdgeForHalfEdge( Topology.FindOppositeHalfEdgeInFace( hHalfEdgeB ) );
		}

		return HalfEdgeHandle.Invalid;
	}

	public bool GetVerticesConnectedToFace( FaceHandle hFace, out VertexHandle[] vertices )
	{
		return Topology.GetVerticesConnectedToFace( hFace, out vertices );
	}

	private HalfEdgeHandle GetFirstVertexInFace( FaceHandle hFace )
	{
		return Topology.GetFirstEdgeInFaceLoop( hFace );
	}

	private HalfEdgeHandle FindPreviousVertexInFace( HalfEdgeHandle hFaceVertex )
	{
		return Topology.FindPreviousEdgeInFaceLoop( hFaceVertex );
	}

	private void FindCornerVerticesForFace( FaceHandle hFace, float minCornerAngle, out List<VertexHandle> outCornerVertices )
	{
		outCornerVertices = new List<VertexHandle>();

		var threshold = MathF.Cos( minCornerAngle.Clamp( 0.0f, 180.0f ).DegreeToRadian() );

		var hStartFaceVertex = GetFirstVertexInFace( hFace );
		var hCurrentFaceVertex = hStartFaceVertex;
		var hPreviousFaceVertex = FindPreviousVertexInFace( hCurrentFaceVertex );
		var hNextFaceVertex = GetNextVertexInFace( hCurrentFaceVertex );

		do
		{
			var hVertexPrev = GetVertexConnectedToFaceVertex( hPreviousFaceVertex );
			var hVertexCurr = GetVertexConnectedToFaceVertex( hCurrentFaceVertex );
			var hVertexNext = GetVertexConnectedToFaceVertex( hNextFaceVertex );

			var posPrev = GetVertexPosition( hVertexPrev );
			var posCurr = GetVertexPosition( hVertexCurr );
			var posNext = GetVertexPosition( hVertexNext );

			var dirIn = (posCurr - posPrev).Normal;
			var dirOut = (posNext - posCurr).Normal;

			var dot = dirIn.Dot( dirOut );
			if ( dot <= threshold )
			{
				outCornerVertices.Add( hVertexCurr );
			}

			hPreviousFaceVertex = hCurrentFaceVertex;
			hCurrentFaceVertex = hNextFaceVertex;
			hNextFaceVertex = GetNextVertexInFace( hCurrentFaceVertex );
		}
		while ( hCurrentFaceVertex != hStartFaceVertex );
	}

	public void QuadSliceFaces( IReadOnlyList<FaceHandle> faces, int cutsX, int cutsY, float minCornerAngleDegrees, List<FaceHandle> outNewFaceList )
	{
		var numFaces = faces.Count;
		var newFaces = new List<FaceHandle>( numFaces * (cutsX + 1) * (cutsY + 1) );

		foreach ( var hFace in faces )
		{
			GetVerticesConnectedToFace( hFace, out var vertices );
			if ( vertices.Length != 4 )
			{
				// If the face isn't actually a quad, see if there are 4 distinct corner
				// vertices that can be identified based on the shape.
				FindCornerVerticesForFace( hFace, minCornerAngleDegrees, out var cornerVertices );

				vertices = cornerVertices.ToArray();
			}

			// If we didn't identify exactly 4 corner vertices the operation cannot be applied to the face
			if ( vertices.Length != 4 )
				continue;

			QuadSliceFace( hFace, vertices, cutsX, cutsY, newFaces );
		}

		// Mark the new faces as having their uv texture coordinates explicitly set so that the
		// texture parameters will be recomputed from the uvs.
		ComputeFaceTextureCoordinatesFromParameters( newFaces );

		if ( outNewFaceList is not null )
		{
			outNewFaceList.Clear();
			outNewFaceList.AddRange( newFaces );
		}
	}

	private bool QuadSliceFace( FaceHandle hFace, IReadOnlyList<VertexHandle> cornerVertices, int numCutsX, int numCutsY, List<FaceHandle> outNewFaces = null,
								  List<EdgeSpan> outEdgeSpansX = null, List<EdgeSpan> outEdgeSpansY = null, List<VertexHandle> outVertices = null, List<Vector2> outVertexParameters = null )

	{
		var hFaceVertices = new HalfEdgeHandle[4];

		if ( FindFaceVerticesInWindingOrder( hFace, cornerVertices, 4, hFaceVertices ) == false )
			return false;

		var edgeSpanX1 = new EdgeSpan();
		var edgeSpanX2 = new EdgeSpan();
		edgeSpanX1.InitializeFromFace( this, hFace, hFaceVertices[3], hFaceVertices[0] );
		edgeSpanX1.Reverse();
		edgeSpanX2.InitializeFromFace( this, hFace, hFaceVertices[1], hFaceVertices[2] );

		var edgeSpanY1 = new EdgeSpan();
		var edgeSpanY2 = new EdgeSpan();
		edgeSpanY1.InitializeFromFace( this, hFace, hFaceVertices[0], hFaceVertices[1] );
		edgeSpanY2.InitializeFromFace( this, hFace, hFaceVertices[2], hFaceVertices[3] );
		edgeSpanY2.Reverse();

		var edgeSpansX = new List<EdgeSpan>();
		edgeSpansX.EnsureCapacity( 2 + numCutsX );
		edgeSpansX.Add( edgeSpanX1 );

		var edgeSpansY = new List<EdgeSpan>();
		edgeSpansY.EnsureCapacity( 2 + numCutsY );
		edgeSpansY.Add( edgeSpanY1 );
		edgeSpansY.Add( edgeSpanY2 );

		if ( AddEdgesConnectingSpans( edgeSpansY, edgeSpansY.Count, numCutsX, edgeSpansX, outNewFaces ) == false )
			return false;

		edgeSpansX.Add( edgeSpanX2 );

		// Remove the last y edge span so that the new ones can be inserted before it.
		edgeSpansY.RemoveAt( edgeSpansY.Count - 1 );

		if ( AddEdgesConnectingSpans( edgeSpansX, edgeSpansX.Count, numCutsY, edgeSpansY, outNewFaces ) == false )
			return false;

		// Re-add the last x edge span
		edgeSpansY.Add( edgeSpanY2 );

		Assert.True( edgeSpansX.Count == (2 + numCutsX) );
		Assert.True( edgeSpansY.Count == (2 + numCutsY) );

		// Since the operation only adds new edges to faces, which creates 
		// one new face each time, the original face is still in use.
		outNewFaces?.Add( hFace );

		if ( outEdgeSpansX is not null )
		{
			outEdgeSpansX.Clear();
			outEdgeSpansX.AddRange( edgeSpansY );
		}

		if ( outEdgeSpansY is not null )
		{
			outEdgeSpansY.Clear();
			outEdgeSpansY.AddRange( edgeSpansX );
		}

		// If requested, get all of the vertices from the spans and 
		// optionally compute their [ 0, 1 ] coordinates within the quad.
		if ( outVertices is not null || outVertexParameters is not null )
		{
			var nNumTotalVertices = (2 + numCutsX) * (2 + numCutsY);

			var allVertices = new List<VertexHandle>( nNumTotalVertices );
			var vertexParameters = new List<Vector2>( nNumTotalVertices );

			var nNumSpansX = edgeSpansX.Count;
			for ( int iX = 0; iX < nNumSpansX; ++iX )
			{
				var edgeSpan = edgeSpansX[iX];
				var nNumSpanVertices = edgeSpan.NumVertices;

				for ( int iY = 0; iY < nNumSpanVertices; ++iY )
				{
					var hVertex = edgeSpan.GetVertex( iY );
					allVertices.Add( hVertex );
					vertexParameters.Add( new Vector2( iX / (float)(nNumSpansX - 1), iY / (float)(nNumSpanVertices - 1) ) );
				}
			}

			if ( outVertices is not null )
			{
				outVertices.Clear();
				outVertices.AddRange( allVertices );
			}

			if ( outVertexParameters is not null )
			{
				outVertexParameters.Clear();
				outVertexParameters.AddRange( vertexParameters );
			}
		}

		return true;
	}

	private FaceHandle FindFaceConnectingEdges( HalfEdgeHandle hEdgeA, HalfEdgeHandle hEdgeB )
	{
		return Topology.FindFaceConnectingFullEdges( hEdgeA, hEdgeB );
	}

	private void GetFaceTriangulatedPositions( FaceHandle hFace, Transform mTransform, out Vector3[] pOutPositions, out int[] pOutIndices )
	{
		pOutPositions = GetFaceVertexPositions( hFace, mTransform ).ToArray();
		pOutIndices = null;

		int nNumFaceVertices = pOutPositions.Length;
		if ( nNumFaceVertices < 3 )
			return;

		PlaneEquation( pOutPositions, out _, out _ );

		pOutIndices = Mesh.TriangulatePolygon( pOutPositions ).ToArray();
	}

	private bool IsFaceShapeValid( FaceHandle hFace )
	{
		if ( hFace == FaceHandle.Invalid )
			return false;

		GetFaceTriangulatedPositions( hFace, Transform.Zero, out var positions, out var indices );

		int nNumExpectedIndices = (positions.Length - 2) * 3;

		return indices.Length == nNumExpectedIndices;
	}

	public void RemoveBadFaces()
	{
		foreach ( var hFace in FaceHandles )
		{
			if ( IsFaceShapeValid( hFace ) )
				continue;

			if ( Topology.RemoveFace( hFace, true ) )
			{
				IsDirty = true;
			}
		}
	}

	private bool AddEdgesConnectingSpans( IReadOnlyList<EdgeSpan> pEdgeSpans, int nNumSpans, int nNumEdgesToAdd, List<EdgeSpan> pOutEdgeSpans, List<FaceHandle> pOutNewFaces )
	{
		// Must have at least two spans
		if ( nNumSpans < 2 )
			return false;

		var pMesh = pEdgeSpans[0].Mesh;
		if ( pMesh is null )
			return false;

		// All spans must be from the same mesh
		for ( int iSpan = 0; iSpan < nNumSpans; ++iSpan )
		{
			if ( pEdgeSpans[iSpan].Mesh != pMesh )
				return false;
		}

		var newVerticesOnSpan = new int[nNumSpans][];

		// Add the required vertices to all of the spans
		for ( int iSpan = 0; iSpan < nNumSpans; ++iSpan )
		{
			pEdgeSpans[iSpan].AddVertices( nNumEdgesToAdd, out newVerticesOnSpan[iSpan] );
		}

		// Add edges between the new vertices on each successive pair of spans
		for ( int iEdgeToAdd = 0; iEdgeToAdd < nNumEdgesToAdd; ++iEdgeToAdd )
		{
			var hEdgeA = pEdgeSpans[0].GetEdge( newVerticesOnSpan[0][iEdgeToAdd] );
			var hVertexA = pEdgeSpans[0].GetVertex( newVerticesOnSpan[0][iEdgeToAdd] );

			var vertices = new List<VertexHandle>( nNumSpans )
			{
				hVertexA
			};

			for ( int iSpan = 1; iSpan < nNumSpans; ++iSpan )
			{
				var hEdgeB = pEdgeSpans[iSpan].GetEdge( newVerticesOnSpan[iSpan][iEdgeToAdd] );
				var hVertexB = pEdgeSpans[iSpan].GetVertex( newVerticesOnSpan[iSpan][iEdgeToAdd] );

				var hFace = pMesh.FindFaceConnectingEdges( hEdgeA, hEdgeB );
				if ( hFace == FaceHandle.Invalid )
					break;

				pMesh.AddEdgeToFace( hFace, hVertexA, hVertexB, out var hNewEdge );
				if ( hNewEdge == HalfEdgeHandle.Invalid )
					break;

				var hNewFace = pMesh.GetOppositeFaceConnectedToEdge( hNewEdge, hFace );
				if ( (hNewFace != FaceHandle.Invalid) && (pOutNewFaces is not null) )
				{
					pOutNewFaces.Add( hNewFace );
				}

				vertices.Add( hVertexB );

				hEdgeA = hEdgeB;
				hVertexA = hVertexB;
			}

			if ( (vertices.Count == nNumSpans) && (pOutEdgeSpans is not null) )
			{
				var pNewEdgeSpan = new EdgeSpan();
				pNewEdgeSpan.InitializeFromVertices( pMesh, vertices );
				pOutEdgeSpans.Add( pNewEdgeSpan );
			}
		}

		return true;
	}

	private bool FindFaceVerticesInWindingOrder( FaceHandle hFace, IReadOnlyList<VertexHandle> pVertices, int nNumVertices, HalfEdgeHandle[] pOutFaceVertices )
	{
		if ( nNumVertices <= 0 )
			return false;

		var faceVertices = new HalfEdgeHandle[nNumVertices];

		// Find the face vertices corresponding to the provided vertices
		for ( int i = 0; i < nNumVertices; ++i )
		{
			faceVertices[i] = FindFaceVertexConnectedToVertex( pVertices[i], hFace );
			if ( faceVertices[i] == HalfEdgeHandle.Invalid )
				return false;
		}

		int nVertexOut = 0;
		var hCurentFaceVertex = faceVertices[0];

		do
		{
			var nVertexIndex = Array.IndexOf( faceVertices, hCurentFaceVertex );
			if ( nVertexIndex != -1 )
			{
				pOutFaceVertices[nVertexOut++] = faceVertices[nVertexIndex];
			}

			// Stop once they have all been found
			if ( nVertexOut == nNumVertices )
				return true;

			hCurentFaceVertex = GetNextVertexInFace( hCurentFaceVertex );
		}
		while ( hCurentFaceVertex != faceVertices[0] );

		// Didn't find all the vertices in the face (this shouldn't happen).
		return false;
	}

	private void ApplyOffsetToFaceUVs( FaceHandle hFace, Vector2 vOffset )
	{
		GetFaceVerticesConnectedToFace( hFace, out var faceVertices );

		foreach ( var hVertex in faceVertices )
		{
			var uv = TextureCoord[hVertex];
			uv += vOffset;
			TextureCoord[hVertex] = uv;
		}
	}

	public void AverageEdgeUVs( IReadOnlyList<HalfEdgeHandle> edges )
	{
		if ( edges.Count <= 0 )
			return;

		foreach ( var hEdge in edges )
		{
			// Get the two faces connected to the edge
			Topology.GetFacesConnectedToFullEdge( hEdge, out var hFace1, out var hFace2 );

			// Get the two vertices connected to the edge
			GetVerticesConnectedToEdge( hEdge, out var hVertexA, out var hVertexB );

			// Get the two pairs of face vertices
			var hFaceVertexA1 = FindFaceVertexConnectedToVertex( hVertexA, hFace1 );
			var hFaceVertexA2 = FindFaceVertexConnectedToVertex( hVertexA, hFace2 );
			var hFaceVertexB1 = FindFaceVertexConnectedToVertex( hVertexB, hFace1 );
			var hFaceVertexB2 = FindFaceVertexConnectedToVertex( hVertexB, hFace2 );

			var uvA1 = TextureCoord[hFaceVertexA1];
			var uvA2 = TextureCoord[hFaceVertexA2];
			var uvB1 = TextureCoord[hFaceVertexB1];
			var uvB2 = TextureCoord[hFaceVertexB2];

			// Find the uv at the center of the edge on each face
			var uvCenterEdge1 = (uvA1 + uvB1) * 0.5f;
			var uvCenterEdge2 = (uvA2 + uvB2) * 0.5f;

			// Compute the integer offset that will place center of edge2 nearest the center of edge1 and
			// then apply that offset to all of the uvs in face2. Since the offset is an integer value 
			// and we apply the offset to all uvs of the face, this won't visually change the texturing.
			var delta = uvCenterEdge1 - uvCenterEdge2;
			var deltaX = (int)(delta.x + (delta.x < 0 ? -0.5f : 0.5f));
			var deltaY = (int)(delta.y + (delta.y < 0 ? -0.5f : 0.5f));
			ApplyOffsetToFaceUVs( hFace2, new Vector2( deltaX, deltaY ) );

			// Read the offset uv values
			var uvOffsetA1 = TextureCoord[hFaceVertexA1];
			var uvOffsetA2 = TextureCoord[hFaceVertexA2];
			var uvOffsetB1 = TextureCoord[hFaceVertexB1];
			var uvOffsetB2 = TextureCoord[hFaceVertexB2];

			// Compute the average for each vertex
			var averageA = (uvOffsetA1 + uvOffsetA2) * 0.5f;
			TextureCoord[hFaceVertexA1] = averageA;
			TextureCoord[hFaceVertexA2] = averageA;

			var averageB = (uvOffsetB1 + uvOffsetB2) * 0.5f;
			TextureCoord[hFaceVertexB1] = averageB;
			TextureCoord[hFaceVertexB2] = averageB;

			ComputeFaceTextureParametersFromCoordinates( new[] { hFace1, hFace2 } );
		}

		IsDirty = true;
	}

	public void AverageVertexUVs( IReadOnlyList<VertexHandle> vertices )
	{
		if ( vertices.Count <= 0 )
			return;

		foreach ( var hVertex in vertices )
		{
			// Get all of the faces connected to the vertex. We get the faces and then the face vertices
			// from the faces because there may be face vertices with no corresponding face for open 
			// edges and we only want the face vertices connected to valid faces.
			GetFacesConnectedToVertex( hVertex, out var connectedFaces );
			var numFaces = connectedFaces.Count;
			if ( numFaces <= 0 )
				continue;

			// Get the face vertices corresponding to each face
			var numFaceVertices = numFaces;
			var connectedFaceVertices = new HalfEdgeHandle[numFaceVertices];
			for ( int i = 0; i < numFaces; ++i )
			{
				connectedFaceVertices[i] = FindFaceVertexConnectedToVertex( hVertex, connectedFaces[i] );
			}

			// First offset the uvs of all of the faces (in 1 unit increments) connected to the vertex 
			// so that the uvs of face vertices connected to the target vertex are as close together as
			// possible. 
			var uv0 = TextureCoord[connectedFaceVertices[0]];

			for ( int i = 1; i < numFaceVertices; ++i )
			{
				var hFaceVertex = connectedFaceVertices[i];
				var hFace = connectedFaces[i];

				var uv = TextureCoord[hFaceVertex];
				var delta = uv0 - uv;
				var deltaX = (int)(delta.x + (delta.x < 0 ? -0.5f : 0.5f));
				var deltaY = (int)(delta.y + (delta.y < 0 ? -0.5f : 0.5f));
				ApplyOffsetToFaceUVs( hFace, new Vector2( deltaX, deltaY ) );
			}

			// Once the uvs have been moved to be as close as possible average the remaining difference
			var uvSum = Vector2.Zero;
			for ( int i = 0; i < numFaceVertices; ++i )
			{
				var hFaceVertex = connectedFaceVertices[i];
				var uv = TextureCoord[hFaceVertex];
				uvSum += uv;
			}

			var uvAverage = uvSum / numFaceVertices;

			for ( int i = 0; i < numFaceVertices; ++i )
			{
				var hFaceVertex = connectedFaceVertices[i];
				TextureCoord[hFaceVertex] = uvAverage;
			}

			ComputeFaceTextureParametersFromCoordinates( connectedFaces );
		}

		IsDirty = true;
	}

	public static void GetBestPlanesForEdgeBetweenFaces( PolygonMesh pMesh1, FaceHandle hFace1, Transform mLocalToWorld1,
										   PolygonMesh pMesh2, FaceHandle hFace2, Transform mLocalToWorld2,
										   out Plane pOutPlane1, out Plane pOutPlane2 )
	{
		var hEdge1 = HalfEdgeHandle.Invalid;
		var hEdge2 = HalfEdgeHandle.Invalid;

		if ( pMesh1 == pMesh2 )
		{
			var hSharedEdge = pMesh1.FindEdgeConnectingFaces( hFace1, hFace2 );
			if ( hSharedEdge != HalfEdgeHandle.Invalid )
			{
				hEdge1 = hSharedEdge;
				hEdge2 = hSharedEdge;
			}
		}

		if ( hEdge1 == HalfEdgeHandle.Invalid )
		{
			if ( !GetNearestEdgesBetweenFaces( pMesh1, hFace1, mLocalToWorld1, pMesh2, hFace2, mLocalToWorld2, out hEdge1, out hEdge2 ) )
			{
				pMesh1.GetFacePlane( hFace1, mLocalToWorld1, out pOutPlane1 );
				pMesh2.GetFacePlane( hFace2, mLocalToWorld2, out pOutPlane2 );
				return;
			}
		}

		pMesh1.GetFacePlaneUsingEdge( hFace1, hEdge1, mLocalToWorld1, out pOutPlane1 );
		pMesh2.GetFacePlaneUsingEdge( hFace2, hEdge2, mLocalToWorld2, out pOutPlane2 );
	}

	public static bool GetNearestEdgesBetweenFaces( PolygonMesh pMesh1, FaceHandle hFace1, Transform mLocalToWorld1,
									PolygonMesh pMesh2, FaceHandle hFace2, Transform mLocalToWorld2,
									out HalfEdgeHandle pOutEdge1, out HalfEdgeHandle pOutEdge2 )
	{
		pOutEdge1 = HalfEdgeHandle.Invalid;
		pOutEdge2 = HalfEdgeHandle.Invalid;

		pMesh1.GetEdgesConnectedToFace( hFace1, out var edgesForFace1 );
		pMesh2.GetEdgesConnectedToFace( hFace2, out var edgesForFace2 );

		var nNumEdgesForFace1 = edgesForFace1.Count;
		var nNumEdgesForFace2 = edgesForFace2.Count;

		var flBestDistSq = 3.402823466e+38F;
		var bFound = false;

		for ( int iEdge1 = 0; iEdge1 < nNumEdgesForFace1; ++iEdge1 )
		{
			var hEdge1 = edgesForFace1[iEdge1];
			pMesh1.GetEdgeVertexPositions( hEdge1, mLocalToWorld1, out var vEdge1PosA, out var vEdge1PosB );
			var vEdge1Norm = (vEdge1PosB - vEdge1PosA).Normal;

			for ( int iEdge2 = 0; iEdge2 < nNumEdgesForFace2; ++iEdge2 )
			{
				var hEdge2 = edgesForFace2[iEdge2];

				pMesh2.GetEdgeVertexPositions( hEdge2, mLocalToWorld2, out var vEdge2PosA, out var vEdge2PosB );
				var vEdge2Norm = (vEdge2PosB - vEdge2PosA).Normal;

				var flDot = vEdge1Norm.Dot( vEdge2Norm );
				if ( MathF.Abs( flDot ) >= 0.95f )
				{
					CalcLineToLineIntersectionSegment( vEdge1PosA, vEdge1PosB, vEdge2PosA, vEdge2PosB, out var vDistA, out var vDistB );

					float flDistSq = (vDistA - vDistB).LengthSquared;
					if ( flDistSq < flBestDistSq )
					{
						flBestDistSq = flDistSq;
						pOutEdge1 = hEdge1;
						pOutEdge2 = hEdge2;
						bFound = true;
					}
				}
			}
		}

		return bFound;
	}

	private static bool CalcLineToLineIntersectionSegment( Vector3 pt1, Vector3 pt2, Vector3 pt3, Vector3 pt4, out Vector3 s1, out Vector3 s2 )
	{
		s1 = default;
		s2 = default;

		var p13 = new Vector3( pt1.x - pt3.x, pt1.y - pt3.y, pt1.z - pt3.z );
		var p43 = new Vector3( pt4.x - pt3.x, pt4.y - pt3.y, pt4.z - pt3.z );
		var p21 = new Vector3( pt2.x - pt1.x, pt2.y - pt1.y, pt2.z - pt1.z );

		const float eps = 0.000001f;

		if ( MathF.Abs( p43.x ) < eps && MathF.Abs( p43.y ) < eps && MathF.Abs( p43.z ) < eps )
			return false;

		if ( MathF.Abs( p21.x ) < eps && MathF.Abs( p21.y ) < eps && MathF.Abs( p21.z ) < eps )
			return false;

		var d1343 = p13.x * p43.x + p13.y * p43.y + p13.z * p43.z;
		var d4321 = p43.x * p21.x + p43.y * p21.y + p43.z * p21.z;
		var d1321 = p13.x * p21.x + p13.y * p21.y + p13.z * p21.z;
		var d4343 = p43.x * p43.x + p43.y * p43.y + p43.z * p43.z;
		var d2121 = p21.x * p21.x + p21.y * p21.y + p21.z * p21.z;

		var denom = d2121 * d4343 - d4321 * d4321;
		if ( MathF.Abs( denom ) < eps )
			return false;

		var numer = d1343 * d4321 - d1321 * d4343;
		var t1 = numer / denom;
		var t2 = (d1343 + d4321 * t1) / d4343;

		s1.x = pt1.x + t1 * p21.x;
		s1.y = pt1.y + t1 * p21.y;
		s1.z = pt1.z + t1 * p21.z;
		s2.x = pt3.x + t2 * p43.x;
		s2.y = pt3.y + t2 * p43.y;
		s2.z = pt3.z + t2 * p43.z;

		return true;
	}

	private static void CalcClosestPointOnLineSegment( Vector3 P, Vector3 A, Vector3 B, out Vector3 closest, out float t )
	{
		var dir = B - A;
		var div = dir.Dot( dir );
		t = (div < 0.00001f) ? 0.0f : Math.Clamp( dir.Dot( P - A ) / div, 0.0f, 1.0f );
		closest = A + dir * t;
	}

	public void GetFacePlaneUsingEdge( FaceHandle hFace, HalfEdgeHandle hEdge, Transform transform, out Plane outPlane )
	{
		GetVerticesConnectedToEdge( hEdge, hFace, out var hVertA, out var hVertB );
		GetVertexPosition( hVertA, transform, out var vPosA );
		GetVertexPosition( hVertB, transform, out var vPosB );

		var line = new Line( vPosA, vPosB );
		var positions = GetFaceVertexPositions( hFace, transform ).ToArray();
		var nNumVertices = positions.Length;

		var flMaxDist = 0.0f;
		var vFurthestPoint = vPosA;

		for ( var i = 0; i < nNumVertices; ++i )
		{
			var flDist = line.Distance( positions[i] );
			if ( flDist > flMaxDist )
			{
				flMaxDist = flDist;
				vFurthestPoint = positions[i];
			}
		}

		var vEdgeDir = (vPosB - vPosA).Normal;
		var vOtherPointDir = (vFurthestPoint - vPosA).Normal;
		var vNormal = vEdgeDir.Cross( vOtherPointDir ).Normal;
		outPlane = new Plane( vPosA, vNormal );
	}

	private void GetFacePlane( FaceHandle hFace, Transform transform, out Plane pOutPlane )
	{
		var positions = GetFaceVertexPositions( hFace, transform ).ToArray();
		PlaneEquation( positions, out var normal, out var distance );
		pOutPlane = new Plane( normal, -distance );
	}

	private static void PlaneEquation( IReadOnlyList<Vector3> pVerts, out Vector3 pOutNormal, out float pOutPlaneDistance )
	{
		var refpt = Vector3.Zero;
		var vNormal = Vector3.Zero;
		var nVertCount = pVerts.Count;

		for ( var i = 0; i < nVertCount; i++ )
		{
			var pU = pVerts[i];
			var pV = pVerts[(i + 1) % nVertCount];
			vNormal.x += (pU.y - pV.y) * (pU.z + pV.z);
			vNormal.y += (pU.z - pV.z) * (pU.x + pV.x);
			vNormal.z += (pU.x - pV.x) * (pU.y + pV.y);
			refpt += pU;
		}

		var len = vNormal.Length + 1.192092896e-07F;
		pOutNormal = vNormal * (1.0f / len);
		len *= nVertCount;
		pOutPlaneDistance = -Vector3.Dot( refpt, vNormal ) / len;
	}

	private Vector3 ComputeFaceVertexNormal( HalfEdgeHandle hTargetFaceVertex )
	{
		var hFirstFaceVertex = hTargetFaceVertex;
		var hLastFaceVertex = hTargetFaceVertex;
		var hCurrentFaceVertex = hTargetFaceVertex;
		var foundHardEdge = false;

		do
		{
			var hNextFaceVertex = GetNextFaceVertexConnectedToVertex( hCurrentFaceVertex );
			var hEdge = Topology.GetFullEdgeForHalfEdge( hNextFaceVertex );

			if ( !foundHardEdge )
			{
				hLastFaceVertex = hNextFaceVertex;
			}

			if ( !IsEdgeSmooth( hEdge ) )
			{
				foundHardEdge = true;
				hFirstFaceVertex = hNextFaceVertex;
			}

			hCurrentFaceVertex = hNextFaceVertex;
		}
		while ( hCurrentFaceVertex != hTargetFaceVertex );

		var normal = Vector3.Zero;
		hCurrentFaceVertex = hFirstFaceVertex;
		do
		{
			var hFace = hCurrentFaceVertex.Face;
			if ( hFace.IsValid )
			{
				ComputeFaceNormal( hFace, out var faceNormal );
				normal += faceNormal;
			}

			hCurrentFaceVertex = GetNextFaceVertexConnectedToVertex( hCurrentFaceVertex );
		}
		while ( hCurrentFaceVertex != hLastFaceVertex );

		return normal.Normal;
	}

	static void CalcTriangleTangentSpace( Vector3 p0, Vector3 p1, Vector3 p2, Vector2 t0, Vector2 t1, Vector2 t2, out Vector3 sVect, out Vector3 tVect )
	{
		const float eps = 1e-12f;

		sVect = Vector3.Zero;
		tVect = Vector3.Zero;

		Vector3 edge01, edge02, cross;

		edge01 = new Vector3( p1.x - p0.x, t1.x - t0.x, t1.y - t0.y );
		edge02 = new Vector3( p2.x - p0.x, t2.x - t0.x, t2.y - t0.y );

		cross = Vector3.Cross( edge01, edge02 );
		if ( MathF.Abs( cross.x ) > eps )
		{
			sVect.x += -cross.y / cross.x;
			tVect.x += -cross.z / cross.x;
		}

		edge01 = new Vector3( p1.y - p0.y, t1.x - t0.x, t1.y - t0.y );
		edge02 = new Vector3( p2.y - p0.y, t2.x - t0.x, t2.y - t0.y );

		cross = Vector3.Cross( edge01, edge02 );
		if ( MathF.Abs( cross.x ) > eps )
		{
			sVect.y += -cross.y / cross.x;
			tVect.y += -cross.z / cross.x;
		}

		edge01 = new Vector3( p1.z - p0.z, t1.x - t0.x, t1.y - t0.y );
		edge02 = new Vector3( p2.z - p0.z, t2.x - t0.x, t2.y - t0.y );

		cross = Vector3.Cross( edge01, edge02 );
		if ( MathF.Abs( cross.x ) > eps )
		{
			sVect.z += -cross.y / cross.x;
			tVect.z += -cross.z / cross.x;
		}

		if ( sVect.LengthSquared > 0.0f ) sVect = sVect.Normal;
		if ( tVect.LengthSquared > 0.0f ) tVect = tVect.Normal;
	}

	bool ComputeTangentSpaceForFaceVertex( HalfEdgeHandle targetHalfEdge, out Vector3 tangentU, out Vector3 tangentV )
	{
		float collinearTolerance = MathF.Cos( 1.0f.DegreeToRadian() );

		tangentU = Vector3.Zero;
		tangentV = Vector3.Zero;

		var face = Topology.GetFaceConnectedToHalfEdge( targetHalfEdge );
		if ( face == FaceHandle.Invalid )
			return false;

		Span<Vector3> positions = stackalloc Vector3[3];
		Span<Vector2> texcoords = stackalloc Vector2[3];

		positions[0] = Positions[GetVertexConnectedToFaceVertex( targetHalfEdge )];
		texcoords[0] = TextureCoord[targetHalfEdge];

		var prevHalfEdge = FindPreviousVertexInFace( targetHalfEdge );
		positions[1] = Positions[GetVertexConnectedToFaceVertex( prevHalfEdge )];
		texcoords[1] = TextureCoord[prevHalfEdge];

		var prevToTarget = (positions[0] - positions[1]).Normal;
		var currentHalfEdge = GetNextVertexInFace( targetHalfEdge );
		do
		{
			positions[2] = Positions[GetVertexConnectedToFaceVertex( currentHalfEdge )];
			texcoords[2] = TextureCoord[currentHalfEdge];

			var targetToCurrent = (positions[2] - positions[0]).Normal;
			if ( Vector3.Dot( targetToCurrent, prevToTarget ) < collinearTolerance )
				break;

			currentHalfEdge = GetNextVertexInFace( currentHalfEdge );
		}
		while ( currentHalfEdge != targetHalfEdge );

		CalcTriangleTangentSpace( positions[0], positions[1], positions[2], texcoords[0], texcoords[1], texcoords[2], out tangentU, out tangentV );

		return true;
	}

	static void BuildBasis( Vector3 normal, out Vector3 tangent, out Vector3 bitangent )
	{
		tangent = Vector3.Cross( MathF.Abs( normal.z ) < 0.999f ? Vector3.Up : Vector3.Left, normal ).Normal;
		bitangent = Vector3.Cross( normal, tangent );
	}

	static void CalcTangentAndFlipFromBasis( Vector3 inTangentU, Vector3 inTangentV, Vector3 normal, out Vector4 tangent )
	{
		var tangentU = inTangentU;
		var tangentV = inTangentV;

		if ( tangentU.Length < 1e-12f || tangentV.Length < 1e-12f )
		{
			BuildBasis( normal, out tangentU, out tangentV );
		}

		var crossUV = Vector3.Cross( tangentU, tangentV );
		var isLeftHanded = Vector3.Dot( crossUV, normal ) < 0.0f;
		var orthoU = Vector3.Cross( normal, tangentU );
		var tangentFromU = Vector3.Cross( orthoU, normal ).Normal;
		var tangentFromV = isLeftHanded ? Vector3.Cross( normal, tangentV ) : Vector3.Cross( tangentV, normal );
		tangentFromV = tangentFromV.Normal;
		var finalTangent = (tangentFromU + tangentFromV).Normal;
		tangent = new Vector4( finalTangent.x, finalTangent.y, finalTangent.z, isLeftHanded ? -1.0f : 1.0f );
	}

	public HalfEdgeHandle GetOppositeHalfEdge( HalfEdgeHandle hEdge )
	{
		if ( !hEdge.IsValid )
			return HalfEdgeHandle.Invalid;

		return Topology.GetOppositeHalfEdge( hEdge );
	}

	public enum ExtentType
	{
		XMin,
		XMax,
		YMin,
		YMax,
		ZMin,
		ZMax
	}

	public class FaceExtents
	{
		private readonly Vector3[] _extents = new Vector3[6];

		public FaceExtents()
		{
			for ( int i = 0; i < _extents.Length; i++ )
			{
				_extents[i] = i % 2 == 0 ? new Vector3( float.MaxValue, float.MaxValue, float.MaxValue ) :
					new Vector3( float.MinValue, float.MinValue, float.MinValue );
			}
		}

		public void AddPoint( Vector3 position )
		{
			if ( position.x < _extents[(int)ExtentType.XMin].x ) _extents[(int)ExtentType.XMin] = position;
			if ( position.x > _extents[(int)ExtentType.XMax].x ) _extents[(int)ExtentType.XMax] = position;
			if ( position.y < _extents[(int)ExtentType.YMin].y ) _extents[(int)ExtentType.YMin] = position;
			if ( position.y > _extents[(int)ExtentType.YMax].y ) _extents[(int)ExtentType.YMax] = position;
			if ( position.z < _extents[(int)ExtentType.ZMin].z ) _extents[(int)ExtentType.ZMin] = position;
			if ( position.z > _extents[(int)ExtentType.ZMax].z ) _extents[(int)ExtentType.ZMax] = position;
		}

		public Vector3 Get( ExtentType type ) => _extents[(int)type];

		public void AddExtents( FaceExtents other )
		{
			if ( other._extents[(int)ExtentType.XMin].x < _extents[(int)ExtentType.XMin].x )
				_extents[(int)ExtentType.XMin] = other._extents[(int)ExtentType.XMin];

			if ( other._extents[(int)ExtentType.XMax].x > _extents[(int)ExtentType.XMax].x )
				_extents[(int)ExtentType.XMax] = other._extents[(int)ExtentType.XMax];

			if ( other._extents[(int)ExtentType.YMin].y < _extents[(int)ExtentType.YMin].y )
				_extents[(int)ExtentType.YMin] = other._extents[(int)ExtentType.YMin];

			if ( other._extents[(int)ExtentType.YMax].y > _extents[(int)ExtentType.YMax].y )
				_extents[(int)ExtentType.YMax] = other._extents[(int)ExtentType.YMax];

			if ( other._extents[(int)ExtentType.ZMin].z < _extents[(int)ExtentType.ZMin].z )
				_extents[(int)ExtentType.ZMin] = other._extents[(int)ExtentType.ZMin];

			if ( other._extents[(int)ExtentType.ZMax].z > _extents[(int)ExtentType.ZMax].z )
				_extents[(int)ExtentType.ZMax] = other._extents[(int)ExtentType.ZMax];
		}
	}

	public static void GetTextureExtents( Vector4 vAxisU, Vector4 vAxisV, Vector2 vScale, FaceExtents extents, out Vector2 topLeft, out Vector2 bottomRight )
	{
		bool first = true;
		topLeft = Vector2.Zero;
		bottomRight = Vector2.Zero;

		for ( var nPoint = 0; nPoint < 6; nPoint++ )
		{
			var test = new Vector2( Vector3.Dot( extents.Get( (ExtentType)nPoint ), (Vector3)vAxisU ) / vScale.x,
				Vector3.Dot( extents.Get( (ExtentType)nPoint ), (Vector3)vAxisV ) / vScale.y );

			if ( (test.x < topLeft.x) || first )
				topLeft.x = test.x;

			if ( (test.y < topLeft.y) || first )
				topLeft.y = test.y;

			if ( (test.x > bottomRight.x) || first )
				bottomRight.x = test.x;

			if ( (test.y > bottomRight.y) || first )
				bottomRight.y = test.y;

			first = false;
		}
	}

	public enum TextureJustification
	{
		None = 0,
		Top,
		Bottom,
		Left,
		Center,
		Right,
		Fit,
		FitX,
		FitY,
	};

	void JustifyTextureUsingExtents( TextureJustification eJustification, FaceExtents Extents, Vector2 vTextureDimensions, ref Vector4 vAxisU, ref Vector4 vAxisV, ref Vector2 vScale )
	{
		if ( vScale.x.AlmostEqual( 0.0f ) )
			vScale.x = 512;

		if ( vScale.y.AlmostEqual( 0.0f ) )
			vScale.y = 512;

		if ( eJustification == TextureJustification.None )
		{
			vAxisU.w = 0;
			vAxisV.w = 0;
			return;
		}

		if ( eJustification == TextureJustification.Fit ||
			 eJustification == TextureJustification.FitX )
		{
			vScale.x = 1.0f;
		}

		if ( eJustification == TextureJustification.Fit ||
			 eJustification == TextureJustification.FitY )
		{
			vScale.y = 1.0f;
		}

		GetTextureExtents( vAxisU, vAxisV, vScale, Extents, out var topLeft, out var bottomRight );

		var Center = new Vector2( (topLeft.x + bottomRight.x) / 2, (topLeft.y + bottomRight.y) / 2 );

		switch ( eJustification )
		{
			case TextureJustification.Top:
				{
					vAxisV.w = -topLeft.y;
					break;
				}

			case TextureJustification.Bottom:
				{
					vAxisV.w = -bottomRight.y + vTextureDimensions.y;
					break;
				}

			case TextureJustification.Left:
				{
					vAxisU.w = -topLeft.x;
					break;
				}

			case TextureJustification.Right:
				{
					vAxisU.w = -bottomRight.x + vTextureDimensions.x;
					break;
				}

			case TextureJustification.Center:
				{
					vAxisU.w = -Center.x + (vTextureDimensions.x * 0.5f);
					vAxisV.w = -Center.y + (vTextureDimensions.y * 0.5f);
					break;
				}

			case TextureJustification.Fit:
			case TextureJustification.FitX:
			case TextureJustification.FitY:
				{
					if ( eJustification != TextureJustification.FitX )
					{
						vScale.y *= (bottomRight.y - topLeft.y) / vTextureDimensions.y;
					}
					if ( eJustification != TextureJustification.FitY )
					{
						vScale.x *= (bottomRight.x - topLeft.x) / vTextureDimensions.x;
					}

					JustifyTextureUsingExtents( TextureJustification.Top, Extents, vTextureDimensions, ref vAxisU, ref vAxisV, ref vScale );
					JustifyTextureUsingExtents( TextureJustification.Left, Extents, vTextureDimensions, ref vAxisU, ref vAxisV, ref vScale );

					break;
				}
		}
	}

	public void JustifyFaceTextureParameters( IEnumerable<FaceHandle> hFaces, TextureJustification justification, FaceExtents extents )
	{
		var textureSizes = Materials.Select( CalculateTextureSize ).ToArray();
		JustifyFaceTextureParameters( hFaces, justification, Transform, textureSizes, extents );
	}

	private void JustifyFaceTextureParameters( IEnumerable<FaceHandle> hFaces, TextureJustification justification, Transform transform, IReadOnlyList<Vector2> textureSizes, FaceExtents extents )
	{
		foreach ( var hFace in hFaces )
		{
			var faceExtents = extents;
			if ( faceExtents is null )
			{
				faceExtents = new FaceExtents();
				UnionExtentsForFaces( new[] { hFace }, transform, faceExtents );
			}

			var offset = TextureOffset[hFace];
			var axisU = new Vector4( TextureUAxis[hFace], offset.x );
			var axisV = new Vector4( TextureVAxis[hFace], offset.y );
			var scale = TextureScale[hFace];
			var materialIndex = MaterialIndex[hFace];
			var textureSize = materialIndex >= 0 && materialIndex < textureSizes.Count ? textureSizes[materialIndex] : DefaultTextureSize;

			JustifyTextureUsingExtents( justification, faceExtents, textureSize, ref axisU, ref axisV, ref scale );
			SetFaceTextureParameters( hFace, axisU, axisV, scale );
		}
	}

	public void UnionExtentsForFaces( IEnumerable<FaceHandle> hFaces, Transform transform, FaceExtents extents )
	{
		if ( extents is null )
			return;

		foreach ( var hFace in hFaces )
		{
			var positions = GetFaceVertexPositions( hFace, transform );
			foreach ( var position in positions )
				extents.AddPoint( position );
		}
	}

	private HalfEdgeHandle FindProceedingHalfEdgeEndingAtVertex( HalfEdgeHandle hStartEdge, VertexHandle hEndVertex )
	{
		var hCurrentEdge = hStartEdge;
		var hIncomingEdge = HalfEdgeHandle.Invalid;

		do
		{
			if ( Topology.GetEndVertexConnectedToEdge( hCurrentEdge ) == hEndVertex )
			{
				hIncomingEdge = hCurrentEdge;
			}

			hCurrentEdge = Topology.GetNextEdgeInFaceLoop( hCurrentEdge );
		}
		while ( hCurrentEdge != hStartEdge );

		return hIncomingEdge;
	}

	private bool GetFaceVertexPositionsStartingAtVertex( HalfEdgeHandle hFaceVertex, Vector3[] outVertexPositions )
	{
		var maxVertices = outVertexPositions.Length;
		var sum = Vector3.Zero;

		// Iterate all of the vertices in face of the specified face
		var hFirstFaceVertex = hFaceVertex;
		var hCurrentFaceVertex = hFirstFaceVertex;
		var numVertices = 0;

		do
		{
			if ( numVertices < maxVertices )
			{
				var hVertex = GetVertexConnectedToFaceVertex( hCurrentFaceVertex );
				var position = GetVertexPosition( hVertex );
				outVertexPositions[numVertices] = position;
				sum += position;
			}
			++numVertices;

			hCurrentFaceVertex = GetNextVertexInFace( hCurrentFaceVertex );
		}
		while ( hCurrentFaceVertex != hFirstFaceVertex );

		return numVertices <= maxVertices;
	}

	private void TriangulateFace( FaceHandle hFace, Submesh submesh )
	{
		if ( !hFace.IsValid )
			return;

		var vertexPositions = Topology.GetFaceVertices( hFace )
			.Select( x => Positions[x] )
			.ToArray();

		var faceIndices = Mesh.TriangulatePolygon( vertexPositions );
		if ( faceIndices.Length < 3 )
			return;

		if ( faceIndices.Length % 3 != 0 )
			return;

		var triangleCount = faceIndices.Length / 3;
		var vertices = submesh.Vertices;
		var triangles = submesh.Indices;
		var uvDensity = submesh.UvDensity;
		int startVertex = vertices.Count;
		int startCollisionVertex = _meshVertices.Count;

		GetFaceVerticesConnectedToFace( hFace, out var faceEdges );

		for ( var i = 0; i < faceEdges.Length; ++i )
		{
			var faceEdge = faceEdges[i];
			var normal = ComputeFaceVertexNormal( faceEdge );
			ComputeTangentSpaceForFaceVertex( faceEdge, out var u, out var v );
			CalcTangentAndFlipFromBasis( u, v, normal, out var tangent );

			int vertexIndex = vertices.Count;

			vertices.Add( new MeshVertex
			{
				Position = vertexPositions[i],
				Normal = normal,
				Tangent = tangent,
				Texcoord = TextureCoord[faceEdge],
				Blend = Blends[faceEdge],
				Color = Colors[faceEdge],
			} );

			if ( !_halfEdgeToMeshVertices.TryGetValue( faceEdge, out var list ) )
			{
				list = new List<MeshVertexRef>( 1 );
				_halfEdgeToMeshVertices.Add( faceEdge, list );
			}

			list.Add( new MeshVertexRef
			{
				SubmeshIndex = submesh.Index,
				VertexIndex = vertexIndex
			} );
		}

		_meshVertices.AddRange( vertexPositions );

		if ( startVertex == vertices.Count )
			return;

		var startIndex = _meshIndices.Count;
		for ( int index = 0; index < triangleCount; ++index )
		{
			var triangle = index * 3;

			var a = startVertex + faceIndices[triangle];
			var b = startVertex + faceIndices[triangle + 1];
			var c = startVertex + faceIndices[triangle + 2];

			if ( a < 0 || a >= vertices.Count )
				return;

			if ( b < 0 || b >= vertices.Count )
				return;

			if ( c < 0 || c >= vertices.Count )
				return;

			var ab = vertices[b].Position - vertices[a].Position;
			var ac = vertices[c].Position - vertices[a].Position;
			var area = Vector3.Cross( ab, ac ).Length * 0.5f;

			if ( area.AlmostEqual( 0.0f ) )
				continue;

			triangles.Add( a );
			triangles.Add( b );
			triangles.Add( c );

			_triangleFaces.Add( hFace );
			_meshTriangleMaterials.Add( (byte)submesh.Index );

			_meshIndices.Add( startCollisionVertex + faceIndices[triangle] );
			_meshIndices.Add( startCollisionVertex + faceIndices[triangle + 1] );
			_meshIndices.Add( startCollisionVertex + faceIndices[triangle + 2] );

			var areaUV = CalculateTriangleAreaUV( vertices[a].Texcoord, vertices[b].Texcoord, vertices[c].Texcoord );
			if ( areaUV > 0.0f )
				uvDensity.Add( MathF.Sqrt( area / areaUV ) );
		}

		_meshFaces.Add( hFace, new FaceMesh
		{
			VertexCount = _meshVertices.Count - startCollisionVertex,
			IndexCount = _meshIndices.Count - startIndex,
			VertexStart = startCollisionVertex,
			IndexStart = startIndex,
		} );
	}

	private static float CalculateTriangleAreaUV( Vector2 a, Vector2 b, Vector2 c )
	{
		return 0.5f * MathF.Abs( -b.x * a.y + c.x * a.y + a.x * b.y - c.x * b.y - a.x * c.y + b.x * c.y );
	}

	public Vertex[] CreateFace( FaceHandle hFace, Transform transform, Color color )
	{
		if ( _meshFaces == null || !_meshFaces.TryGetValue( hFace, out FaceMesh faceMesh ) )
			return null;

		return Enumerable.Range( faceMesh.IndexStart, faceMesh.IndexCount )
					.Select( x => new Vertex( transform.PointToWorld( _meshVertices[_meshIndices[x]] ), color ) )
					.ToArray();
	}

	bool FindCutEdgeIntersection( VertexHandle hVertex, Vector3 targetPosition, out HalfEdgeHandle outEdge, out FaceHandle outFace, out Vector3 outPosition )
	{
		outEdge = HalfEdgeHandle.Invalid;
		outFace = FaceHandle.Invalid;
		outPosition = default;

		GetVertexPosition( hVertex, Transform.Zero, out var vCurrentPosition );
		var vDir = (targetPosition - vCurrentPosition).Normal;

		GetFacesConnectedToVertex( hVertex, out var connectedFaces );

		var hBestFace = FaceHandle.Invalid;
		var hBestVertexA = VertexHandle.Invalid;
		var hBestVertexB = VertexHandle.Invalid;
		var vBestPoint = Vector3.Zero;
		var flMinDistance = float.MaxValue;

		int nNumFaces = connectedFaces.Count;
		for ( int iFace = 0; iFace < nNumFaces; ++iFace )
		{
			var hFace = connectedFaces[iFace];

			ComputeFaceNormal( hFace, out var vFaceNormal );

			if ( MathF.Abs( vFaceNormal.Dot( vDir ) ) > 0.5f )
				continue;

			var vCutPlaneNormal = vFaceNormal.Cross( vDir ).Normal;
			var cutPlane = new Plane( vCurrentPosition, vCutPlaneNormal );
			var basePlane = new Plane( vCurrentPosition, vDir );

			var hStartFaceVertex = FindFaceVertexConnectedToVertex( hVertex, hFace );
			var hFaceVertexA = GetNextVertexInFace( hStartFaceVertex );
			var hFaceVertexB = GetNextVertexInFace( hFaceVertexA );

			var hBestVertexForFaceA = VertexHandle.Invalid;
			var hBestVertexForFaceB = VertexHandle.Invalid;
			var vBestPointForFace = Vector3.Zero;
			var flMinBasePlaneDistance = float.MaxValue;

			while ( hFaceVertexB != hStartFaceVertex )
			{
				var hVertexA = GetVertexConnectedToFaceVertex( hFaceVertexA );
				var hVertexB = GetVertexConnectedToFaceVertex( hFaceVertexB );

				if ( (hVertexA != hVertex) && (hVertexB != hVertex) )
				{
					var vPositionA = GetVertexPosition( hVertexA );
					var vPositionB = GetVertexPosition( hVertexB );
					var vIntersection = cutPlane.IntersectLine( vPositionA, vPositionB );
					if ( vIntersection.HasValue )
					{
						float flBasePlaneDistance = basePlane.GetDistance( vIntersection.Value );

						if ( (flBasePlaneDistance >= 0) && (flBasePlaneDistance <= flMinBasePlaneDistance) )
						{
							var vAB = vPositionB - vPositionA;
							var vCross = vDir.Cross( vAB );

							if ( vCross.Dot( vFaceNormal ) > 0.0f )
							{
								hBestVertexForFaceA = hVertexA;
								hBestVertexForFaceB = hVertexB;
								vBestPointForFace = vIntersection.Value;
							}
							else if ( flBasePlaneDistance < flMinBasePlaneDistance )
							{
								hBestVertexForFaceA = VertexHandle.Invalid;
								hBestVertexForFaceB = VertexHandle.Invalid;
								vBestPointForFace = Vector3.Zero;
							}

							flMinBasePlaneDistance = flBasePlaneDistance;
						}
					}
				}

				hFaceVertexA = hFaceVertexB;
				hFaceVertexB = GetNextVertexInFace( hFaceVertexB );
			}

			var flFaceTargetDistance = vBestPointForFace.Distance( targetPosition );
			if ( (hBestVertexForFaceA != VertexHandle.Invalid) &&
				 (hBestVertexForFaceB != VertexHandle.Invalid) &&
				 (flFaceTargetDistance < flMinDistance) )
			{
				hBestFace = hFace;
				hBestVertexA = hBestVertexForFaceA;
				hBestVertexB = hBestVertexForFaceB;
				vBestPoint = vBestPointForFace;
				flMinDistance = flFaceTargetDistance;
			}
		}

		if ( hBestFace == FaceHandle.Invalid )
			return false;

		outEdge = FindEdgeConnectingVertices( hBestVertexA, hBestVertexB );
		outFace = hBestFace;
		outPosition = vBestPoint;

		return true;
	}

	public void GetFacesConnectedToEdge( HalfEdgeHandle hEdge, out FaceHandle hOutFaceA, out FaceHandle hOutFaceB )
	{
		Topology.GetFacesConnectedToFullEdge( hEdge, out hOutFaceA, out hOutFaceB );
	}

	public void FindBoundaryEdgesConnectedToFaces( IReadOnlyList<FaceHandle> faces, out List<HalfEdgeHandle> outBoundaryEdges )
	{
		Topology.FindFullEdgesConnectedToFaces( faces, faces.Count, out var allConnectedEdges, out var edgeFaceCounts );

		var connectedEdgesCount = allConnectedEdges.Length;
		outBoundaryEdges = new( connectedEdgesCount );

		for ( int i = 0; i < connectedEdgesCount; ++i )
		{
			if ( edgeFaceCounts[i] != 2 )
			{
				outBoundaryEdges.Add( allConnectedEdges[i] );
			}
		}
	}

	public bool ThickenFaces( IReadOnlyList<FaceHandle> faces, float amount, out List<FaceHandle> outFaces )
	{
		var any = false;
		outFaces = [];

		FindFaceIslands( faces, out var faceIslands );

		var numIslands = faceIslands.Count;
		for ( int islandIndex = 0; islandIndex < numIslands; ++islandIndex )
		{
			var faceIsland = faceIslands[islandIndex];

			FindBoundaryEdgesConnectedToFaces( faceIsland, out var boundaryEdges );

			var numEdges = boundaryEdges.Count;
			var allEdgesOpen = numEdges > 0;
			for ( int iEdge = 0; iEdge < numEdges; ++iEdge )
			{
				if ( IsEdgeOpen( boundaryEdges[iEdge] ) == false )
				{
					allEdgesOpen = false;
					break;
				}
			}

			if ( allEdgesOpen == false )
				continue;

			var newMesh = new PolygonMesh();
			newMesh.MergeMesh( this, Transform.Zero, out _, out _, out var newFaces );
			var facesToExtrude = new List<FaceHandle>();
			var facesToRemove = new List<FaceHandle>();

			foreach ( var face in faceIsland )
			{
				facesToExtrude.Add( newFaces[face] );
			}

			foreach ( var face in newMesh.FaceHandles )
			{
				if ( facesToExtrude.Contains( face ) ) continue;
				facesToRemove.Add( face );
			}

			newMesh.RemoveFaces( facesToRemove );
			newMesh.FlipAllFaces();
			newMesh.BevelFaces( [.. facesToExtrude], out var extrudedFaces, out _, out var connectingEdges, true );
			newMesh.OffsetFacesAlongNormal( extrudedFaces, amount );

			foreach ( var face in newMesh.FaceHandles )
			{
				newMesh.TextureAlignToGrid( Transform, face );
			}

			MergeMesh( newMesh, Transform.Zero, out _, out var newEdges, out newFaces );

			var allEdges = new List<HalfEdgeHandle>();
			foreach ( var e in connectingEdges )
			{
				if ( newEdges.TryGetValue( e, out var v ) )
					allEdges.Add( v );
			}

			foreach ( var e in boundaryEdges )
			{
				allEdges.Add( e );
			}

			FindVerticesConnectedToEdges( allEdges, out var vertices );
			MergeVerticesWithinDistance( vertices, 0.000001f, false, true, out _ );

			foreach ( var f in faceIsland )
			{
				outFaces.Add( f );
			}

			any = true;
		}

		return any;
	}

	public void FindFaceIslands( IReadOnlyList<FaceHandle> faces, out List<List<FaceHandle>> outFaces )
	{
		outFaces = [];

		var faceSearchSet = faces.Where( IsFaceInMesh ).ToHashSet();

		while ( faceSearchSet.Count > 0 )
		{
			var hStartFace = faceSearchSet.First();
			faceSearchSet.Remove( hStartFace );

			var island = new List<FaceHandle>( 32 );
			outFaces.Add( island );
			island.Add( hStartFace );

			for ( int i = 0; i < island.Count; ++i )
			{
				var hCurrentFace = island[i];
				var hStartEdge = Topology.GetFirstEdgeInFaceLoop( hCurrentFace );
				var hEdge = hStartEdge;

				do
				{
					var hConnectedFace = hEdge.OppositeEdge.Face;

					if ( faceSearchSet.Remove( hConnectedFace ) )
					{
						island.Add( hConnectedFace );
					}

					hEdge = hEdge.NextEdge;
				}
				while ( hEdge != hStartEdge );
			}
		}
	}

	public bool GetFaceVerticesConnectedToVertex( VertexHandle hVertex, out List<HalfEdgeHandle> faceVertices )
	{
		return Topology.GetIncomingHalfEdgesConnectedToVertex( hVertex, out faceVertices );
	}

	public bool FindHalfEdgesConnectedToFace( FaceHandle face, out List<HalfEdgeHandle> halfEdges )
	{
		halfEdges = null;

		if ( !face.IsValid )
			return false;

		int numHalfEdges = Topology.ComputeNumEdgesInFace( face );
		if ( numHalfEdges <= 0 )
			return false;

		halfEdges = new List<HalfEdgeHandle>( numHalfEdges );
		var startEdge = Topology.GetFirstEdgeInFaceLoop( face );
		if ( !startEdge.IsValid )
			return false;

		var current = startEdge;
		do
		{
			halfEdges.Add( current );
			current = Topology.GetNextEdgeInFaceLoop( current );
		}
		while ( current != startEdge );

		return halfEdges.Count > 0;
	}

	private static readonly Vector3[] FaceNormals =
	{
		new( 0, 0, 1 ),
		new( 0, 0, -1 ),
		new( 0, -1, 0 ),
		new( 0, 1, 0 ),
		new( -1, 0, 0 ),
		new( 1, 0, 0 ),
	};

	private static readonly Vector3[] FaceRightVectors =
	{
		new( 1, 0, 0 ),
		new( 1, 0, 0 ),
		new( 1, 0, 0 ),
		new( -1, 0, 0 ),
		new( 0, -1, 0 ),
		new( 0, 1, 0 ),
	};

	private static readonly Vector3[] FaceDownVectors =
	{
		new( 0, -1, 0 ),
		new( 0, -1, 0 ),
		new( 0, 0, -1 ),
		new( 0, 0, -1 ),
		new( 0, 0, -1 ),
		new( 0, 0, -1 ),
	};

	private static void ComputeTextureAxes( Vector3 normal, out Vector3 uAxis, out Vector3 vAxis )
	{
		var orientation = GetOrientationForPlane( normal );
		uAxis = FaceRightVectors[orientation];
		vAxis = FaceDownVectors[orientation];
	}

	private static int GetOrientationForPlane( Vector3 plane )
	{
		plane = plane.Normal;

		var maxDot = 0.0f;
		int orientation = 0;

		for ( int i = 0; i < 6; i++ )
		{
			var dot = Vector3.Dot( plane, FaceNormals[i] );

			if ( dot >= maxDot )
			{
				maxDot = dot;
				orientation = i;
			}
		}

		return orientation;
	}

	[StructLayout( LayoutKind.Sequential )]
	struct MeshVertex( Vector3 position, Vector3 normal, Vector4 tangent, Vector2 texcoord, Color32 blend, Color32 color )
	{
		[VertexLayout.Position] public Vector3 Position = position;
		[VertexLayout.Normal] public Vector3 Normal = normal;
		[VertexLayout.Tangent] public Vector4 Tangent = tangent;
		[VertexLayout.TexCoord] public Vector2 Texcoord = texcoord;
		[VertexLayout.TexCoord( 4 )] public Color32 Blend = blend;
		[VertexLayout.TexCoord( 5 )] public Color32 Color = color;
	}
}
