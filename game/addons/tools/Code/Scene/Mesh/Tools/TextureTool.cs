using HalfEdgeMesh;
using System.Runtime.InteropServices;

namespace Editor.MeshEditor;

/// <summary>
/// Select and edit face uvs and materials.
/// </summary>
[Title( "Texture Tool" )]
[Icon( "gradient" )]
[Alias( "tools.texture-tool" )]
[Group( "4" )]
public sealed partial class TextureTool( MeshTool tool ) : SelectionTool<MeshFace>( tool )
{
	protected override bool HasMoveMode => false;

	MeshFace _hoverFace;
	SceneDynamicObject _faceObject;

	//Selection
	public bool SelectByMaterial { get; set; } = false;
	public bool SelectByNormal { get; set; } = true;
	public float NormalThreshold { get; set; } = 12f;

	public override void OnEnabled()
	{
		base.OnEnabled();

		CreateFaceObject();
	}

	private void CreateFaceObject()
	{
		_faceObject = new SceneDynamicObject( Scene.SceneWorld );
		_faceObject.Material = Material.Load( "materials/tools/vertex_color_translucent.vmat" );
		_faceObject.Attributes.SetCombo( "D_DEPTH_BIAS", 1 );
		_faceObject.Attributes.SetCombo( "D_NO_CULLING", 1 );
		_faceObject.Flags.CastShadows = false;
	}

	public override void OnDisabled()
	{
		base.OnDisabled();

		_faceObject?.Delete();
		_faceObject = null;

		_hoverFace = default;
	}

	public override void OnUpdate()
	{
		base.OnUpdate();

		using var scope = Gizmo.Scope( "FaceTool" );

		var result = MeshTrace.Run();
		if ( result.Hit && result.Component is MeshComponent )
			Gizmo.Hitbox.TrySetHovered( result.EndPosition );

		if ( _faceObject.IsValid() && _faceObject.World != Scene.SceneWorld )
		{
			_hoverFace = default;
			_faceObject.Delete();

			CreateFaceObject();
		}

		if ( Gizmo.IsHovered && !IsLassoSelecting )
		{
			SelectFace();

			if ( Gizmo.IsDoubleClicked )
			{
				if ( Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Alt ) && (SelectByNormal || SelectByMaterial) )
				{
					SelectFacesByNormal( _hoverFace );
				}
				else
				{
					SelectContiguousFaces();
				}
			}
		}

		_faceObject.Init( Graphics.PrimitiveType.Triangles );

		if ( _hoverFace.IsValid() )
		{
			var hoverColor = Color.Green.WithAlpha( 0.1f );
			var mesh = _hoverFace.Component.Mesh;
			var vertices = mesh.CreateFace( _hoverFace.Handle, _hoverFace.Transform, hoverColor );
			if ( vertices is not null )
			{
				_faceObject.AddVertex( vertices.AsSpan() );
			}

			if ( Gizmo.WasRightMousePressed && Gizmo.KeyboardModifiers.Contains( KeyboardModifiers.Shift ) )
			{
				Tool.ActiveMaterial = _hoverFace.Material;
			}

			if ( Gizmo.IsRightMouseDown && Gizmo.KeyboardModifiers.Contains( KeyboardModifiers.Ctrl ) )
			{
				var material = mesh.GetFaceMaterial( _hoverFace.Handle );
				if ( material != Tool.ActiveMaterial )
				{
					using ( SceneEditorSession.Active.UndoScope( "Paint Material" )
						.WithComponentChanges( _hoverFace.Component )
						.Push() )
					{
						mesh.SetFaceMaterial( _hoverFace.Handle, Tool.ActiveMaterial );
					}
				}
			}

			_hoverFace = default;
		}

		var selectionColor = Color.Yellow.WithAlpha( 0.1f );

		if ( !Tool.OverlaySelection )
			selectionColor = Color.Transparent;

		foreach ( var face in Selection.OfType<MeshFace>() )
		{
			var mesh = face.Component.Mesh;
			var vertices = mesh.CreateFace( face.Handle, face.Transform, selectionColor );
			if ( vertices is not null )
			{
				foreach ( var vertex in vertices )
					_faceObject.AddVertex( vertex );
			}
		}

		DrawBounds();
	}

	protected override IEnumerable<MeshFace> ConvertSelectionToCurrentType()
	{
		var selectedEdges = Selection.OfType<MeshEdge>().ToHashSet();
		var selectedVertices = Selection.OfType<MeshVertex>().ToHashSet();

		var candidateFaces = new HashSet<MeshFace>();

		foreach ( var edge in selectedEdges )
		{
			if ( !edge.IsValid() )
				continue;

			var mesh = edge.Component.Mesh;
			mesh.GetFacesConnectedToEdge( edge.Handle, out var faceA, out var faceB );

			if ( faceA.IsValid )
				candidateFaces.Add( new MeshFace( edge.Component, faceA ) );

			if ( faceB.IsValid )
				candidateFaces.Add( new MeshFace( edge.Component, faceB ) );
		}

		foreach ( var vertex in selectedVertices )
		{
			if ( !vertex.IsValid() )
				continue;

			var mesh = vertex.Component.Mesh;
			mesh.GetFacesConnectedToVertex( vertex.Handle, out var faces );

			foreach ( var face in faces )
			{
				if ( face.IsValid )
					candidateFaces.Add( new MeshFace( vertex.Component, face ) );
			}
		}

		foreach ( var face in candidateFaces )
		{
			if ( !face.IsValid() )
				continue;

			var mesh = face.Component.Mesh;

			if ( selectedEdges.Count > 0 )
			{
				var faceEdges = mesh.GetFaceEdges( face.Handle );
				bool allEdgesSelected = faceEdges.All( edge =>
					selectedEdges.Contains( new MeshEdge( face.Component, edge ) )
				);

				if ( allEdgesSelected )
				{
					yield return face;
					continue;
				}
			}

			if ( selectedVertices.Count > 0 )
			{
				mesh.GetVerticesConnectedToFace( face.Handle, out var faceVertices );
				bool allVerticesSelected = faceVertices.All( vertex =>
					selectedVertices.Contains( new MeshVertex( face.Component, vertex ) )
				);

				if ( allVerticesSelected )
				{
					yield return face;
				}
			}
		}
	}

	protected override IEnumerable<IMeshElement> GetAllSelectedElements()
	{
		foreach ( var group in Selection.OfType<MeshFace>()
			.GroupBy( x => x.Component ) )
		{
			var component = group.Key;
			foreach ( var hFace in component.Mesh.FaceHandles )
				yield return new MeshFace( component, hFace );
		}
	}

	private void SelectFace()
	{
		_hoverFace = TraceFace();
		UpdateSelection( _hoverFace );

		if ( Gizmo.IsAltPressed && Gizmo.WasRightMousePressed )
		{
			if ( Gizmo.IsShiftPressed )
			{
				WrapTextureToSelection();
			}
			else
			{
				WrapTexture();
			}
		}
	}

	private void WrapTextureToSelection()
	{
		foreach ( var face in Selection.OfType<MeshFace>() )
		{
			WrapTexture( _hoverFace, face );
		}
	}

	private void WrapTexture()
	{
		if ( !_hoverFace.IsValid() || Selection.LastOrDefault() is not MeshFace face )
			return;

		WrapTexture( face, _hoverFace );
	}

	private static void WrapTexture( MeshFace sourceFace, MeshFace targetFace )
	{
		if ( !sourceFace.IsValid() )
			return;

		if ( !targetFace.IsValid() )
			return;

		var sourceMesh = sourceFace.Component.Mesh;
		var targetMesh = targetFace.Component.Mesh;

		targetFace.Material = sourceFace.Material;
		sourceMesh.GetFaceTextureParameters( sourceFace.Handle, out var vAxisU, out var vAxisV, out var vScale );

		PolygonMesh.GetBestPlanesForEdgeBetweenFaces( sourceMesh, sourceFace.Handle, sourceFace.Transform,
			targetMesh, targetFace.Handle, targetFace.Transform,
			out var fromPlane, out var toPlane );

		RotateTextureCoordinatesAroundEdge( fromPlane, toPlane, ref vAxisU, ref vAxisV, vScale );

		targetMesh.SetFaceTextureParameters( targetFace.Handle, vAxisU, vAxisV, vScale );
	}

	private static void RotateTextureCoordinatesAroundEdge( Plane fromPlane, Plane toPlane, ref Vector4 pInOutAxisU, ref Vector4 pInOutAxisV, Vector2 scale )
	{
		Vector3 vAxisUOld = (Vector3)pInOutAxisU;
		Vector3 vAxisVOld = (Vector3)pInOutAxisV;
		var flShiftUOld = pInOutAxisU.w * scale.x;
		var flShiftVOld = pInOutAxisV.w * scale.y;

		var vEdge = fromPlane.Normal.Cross( toPlane.Normal ).Normal;
		var vEdgePoint = Plane.GetIntersection( fromPlane, toPlane, new Plane( vEdge, 0.0f ) );

		var vAxisUNew = vAxisUOld;
		var vAxisVNew = vAxisVOld;
		var flShiftUNew = flShiftUOld;
		var flShiftVNew = flShiftVOld;

		if ( vEdgePoint.HasValue )
		{
			var vProjFromNormal = fromPlane.Normal - vEdge * vEdge.Dot( fromPlane.Normal );
			var vProjToNormal = toPlane.Normal - vEdge * vEdge.Dot( toPlane.Normal );

			vProjFromNormal = vProjFromNormal.Normal;
			vProjToNormal = vProjToNormal.Normal;

			var flPlanesDot = vProjFromNormal.Dot( vProjToNormal ).Clamp( -1.0f, 1.0f );
			var flRotationAngle = System.MathF.Acos( flPlanesDot ) * (180.0f / System.MathF.PI);

			if ( flPlanesDot < 0.0f )
			{
				flRotationAngle = 180.0f - flRotationAngle;
			}

			var mEdgeRotation = Rotation.FromAxis( vEdge, flRotationAngle );
			vAxisUNew = vAxisUOld * mEdgeRotation;
			vAxisVNew = vAxisVOld * mEdgeRotation;

			var edgePoint = vEdgePoint.Value;
			var flPointU = (Vector3.Dot( vAxisUOld, edgePoint ) + flShiftUOld) / scale.x;
			var flPointV = (Vector3.Dot( vAxisVOld, edgePoint ) + flShiftVOld) / scale.y;

			var flNewPointUnshiftedU = Vector3.Dot( vAxisUNew, edgePoint ) / scale.x;
			var flNewPointUnshiftedV = Vector3.Dot( vAxisVNew, edgePoint ) / scale.y;

			var flNeededShiftU = flPointU - flNewPointUnshiftedU;
			var flNeededShiftV = flPointV - flNewPointUnshiftedV;

			flShiftUNew = flNeededShiftU * scale.x;
			flShiftVNew = flNeededShiftV * scale.y;
		}

		pInOutAxisU = new Vector4( vAxisUNew, flShiftUNew / scale.x );
		pInOutAxisV = new Vector4( vAxisVNew, flShiftVNew / scale.y );
	}

	private void SelectAllConnectedFaces( MeshFace startFace )
	{
		if ( !startFace.IsValid() )
			return;

		var mesh = startFace.Component.Mesh;

		if ( !Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Shift ) )
			Selection.Clear();

		var queue = new Queue<FaceHandle>();
		var visited = new HashSet<FaceHandle>();

		queue.Enqueue( startFace.Handle );
		visited.Add( startFace.Handle );

		while ( queue.Count > 0 )
		{
			var currentHandle = queue.Dequeue();

			var meshFace = new MeshFace( startFace.Component, currentHandle );

			if ( Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Ctrl ) )
			{
				if ( Selection.Contains( meshFace ) )
					Selection.Remove( meshFace );
				else
					Selection.Add( meshFace );
			}
			else
			{
				Selection.Add( meshFace );
			}

			var edges = mesh.GetFaceEdges( currentHandle );
			foreach ( var edge in edges )
			{
				mesh.GetFacesConnectedToEdge( edge, out var faceA, out var faceB );

				var neighbor = faceA == currentHandle ? faceB : faceA;
				if ( neighbor.IsValid && !visited.Contains( neighbor ) )
				{
					visited.Add( neighbor );
					queue.Enqueue( neighbor );
				}
			}
		}
	}

	private void SelectContiguousFaces()
	{
		var targetFace = TraceFace();
		if ( !targetFace.IsValid() )
			return;

		if ( Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Alt ) && (SelectByNormal || SelectByMaterial) )
		{
			SelectFacesByNormal( targetFace );
			return;
		}

		if ( Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Shift ) && TrySelectFacePath( targetFace ) )
			return;

		SelectAllConnectedFaces( targetFace );
	}

	/// <summary>
	/// Select connected faces with normals within a threshold angle (default 12 degrees)
	/// Uses flood-fill to only select adjacent faces with similar normals
	/// </summary>
	private void SelectFacesByNormal( MeshFace targetFace )
	{
		if ( !targetFace.IsValid() ) return;

		var mesh = targetFace.Component.Mesh;

		Vector3 targetNormal = Vector3.Zero;
		float dotThreshold = 1f;
		if ( SelectByNormal )
		{
			mesh.ComputeFaceNormal( targetFace.Handle, out targetNormal );
			dotThreshold = MathF.Cos( NormalThreshold * MathF.PI / 180f );
		}

		Material targetMaterial = null;
		if ( SelectByMaterial )
		{
			targetMaterial = targetFace.Material;
		}

		if ( !Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Shift ) )
			Selection.Clear();

		var queue = new Queue<FaceHandle>();
		var visited = new HashSet<FaceHandle>();
		var matchingFaces = new List<MeshFace>();

		queue.Enqueue( targetFace.Handle );
		visited.Add( targetFace.Handle );

		while ( queue.Count > 0 )
		{
			var currentHandle = queue.Dequeue();
			bool matches = true;

			if ( SelectByNormal && matches )
			{
				mesh.ComputeFaceNormal( currentHandle, out var currentNormal );
				float dot = Vector3.Dot( targetNormal, currentNormal );
				matches = dot >= dotThreshold;
			}

			if ( SelectByMaterial && matches )
			{
				var currentFace = new MeshFace( targetFace.Component, currentHandle );
				var currentMaterial = currentFace.Material;

				matches = (targetMaterial == null && currentMaterial == null) || (targetMaterial != null && currentMaterial != null && targetMaterial.ResourcePath == currentMaterial.ResourcePath);
			}

			if ( matches )
			{
				matchingFaces.Add( new MeshFace( targetFace.Component, currentHandle ) );

				var edges = mesh.GetFaceEdges( currentHandle );
				foreach ( var edge in edges )
				{
					mesh.GetFacesConnectedToEdge( edge, out var faceA, out var faceB );

					var neighbor = faceA == currentHandle ? faceB : faceA;
					if ( neighbor.IsValid && !visited.Contains( neighbor ) )
					{
						visited.Add( neighbor );
						queue.Enqueue( neighbor );
					}
				}
			}
		}

		foreach ( var face in matchingFaces )
		{
			if ( Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Ctrl ) )
			{
				if ( Selection.Contains( face ) )
					Selection.Remove( face );
				else
					Selection.Add( face );
			}
			else
			{
				Selection.Add( face );
			}
		}
	}

	private bool TrySelectFacePath( MeshFace targetFace )
	{
		var selected = Selection.OfType<MeshFace>()
			.Where( f => f.IsValid() && f.Component == targetFace.Component )
			.ToList();

		if ( selected.Count == 0 || selected.Count > 2 )
			return false;

		var startFace = selected.FirstOrDefault( f => f.Handle != targetFace.Handle );

		if ( !startFace.IsValid() )
			return false;

		var path = FindShortestFacePath( startFace, targetFace );
		if ( path == null || path.Count == 0 )
			return false;

		foreach ( var face in path.Where( f => !Selection.Contains( f ) ) )
			Selection.Add( face );

		return true;
	}

	private List<MeshFace> FindShortestFacePath( MeshFace start, MeshFace end )
	{
		if ( start.Component != end.Component )
			return null;

		var mesh = start.Component.Mesh;
		var queue = new Queue<FaceHandle>();
		var visited = new HashSet<FaceHandle>();
		var parent = new Dictionary<FaceHandle, FaceHandle>();

		queue.Enqueue( start.Handle );
		visited.Add( start.Handle );

		while ( queue.Count > 0 )
		{
			var current = queue.Dequeue();

			if ( current == end.Handle )
			{
				var path = new List<MeshFace>();
				var step = end.Handle;

				while ( step.IsValid )
				{
					path.Add( new MeshFace( start.Component, step ) );
					if ( step == start.Handle )
						break;
					if ( !parent.TryGetValue( step, out step ) )
						break;
				}

				path.Reverse();
				return path;
			}

			var edges = mesh.GetFaceEdges( current );
			foreach ( var edge in edges )
			{
				mesh.GetFacesConnectedToEdge( edge, out var faceA, out var faceB );

				var neighbor = faceA == current ? faceB : faceA;
				if ( neighbor.IsValid && !visited.Contains( neighbor ) )
				{
					visited.Add( neighbor );
					parent[neighbor] = current;
					queue.Enqueue( neighbor );
				}
			}
		}

		return null;
	}

	public override List<MeshFace> ExtrudeSelection( Vector3 delta = default )
	{
		var faces = Selection.OfType<MeshFace>().ToArray();

		Selection.Clear();

		var connectingFaces = new List<MeshFace>();

		var components = faces.Select( x => x.Component ).Distinct();

		foreach ( var group in faces.GroupBy( x => x.Component ) )
		{
			var offset = group.Key.WorldRotation.Inverse * delta;
			group.Key.Mesh.ExtrudeFaces( group.Select( x => x.Handle ).ToArray(), out var newFaces, out var newConnectingFaces, offset );

			foreach ( var hFace in newFaces )
				Selection.Add( new MeshFace( group.Key, hFace ) );

			foreach ( var hFace in newConnectingFaces )
				connectingFaces.Add( new MeshFace( group.Key, hFace ) );
		}

		CalculateSelectionVertices();

		return connectingFaces;
	}

	public override Rotation CalculateSelectionBasis()
	{
		if ( GlobalSpace ) return Rotation.Identity;

		var face = Selection.OfType<MeshFace>().FirstOrDefault();
		if ( face.IsValid() )
		{
			face.Component.Mesh.ComputeFaceNormal( face.Handle, out var normal );
			var vAxis = ComputeTextureVAxis( normal );
			var basis = Rotation.LookAt( normal, vAxis * -1.0f );
			return face.Transform.RotationToWorld( basis );
		}

		return Rotation.Identity;
	}

	private void DrawBounds()
	{
		using ( Gizmo.Scope( "Face Size" ) )
		{
			var box = CalculateSelectionBounds();
			DimensionDisplay.DrawBounds( box );
		}
	}

	static void ComputeHotspotUVsForFaces( PolygonMesh mesh, Transform transform, IReadOnlyList<FaceHandle> faces, RectEditor.RectAssetData subrectInfo, int mappingWidth, int mappingHeight, bool perFace, bool useTiling, bool conforming )
	{
		List<List<FaceHandle>> faceIslands = [];
		if ( perFace )
		{
			for ( int iFace = 0; iFace < faces.Count; ++iFace )
			{
				faceIslands.Add( [faces[iFace]] );
			}
		}
		else
		{
			mesh.SplitFacesIntoIslandsForUVMapping( faces, out faceIslands );
		}

		foreach ( var island in faceIslands )
		{
			var align = FindBestAlignmentEdge( mesh, transform, island, out var edgeA, out var edgeB );

			mesh.GenerateUVsForFaces( CollectionsMarshal.AsSpan( island ), conforming ? 2 : 0, (int)align, edgeA, edgeB, out var faceVertices, out var uvs );

			ScaleUVsToWorldSpace( mesh, transform, island, align, edgeA, edgeB, faceVertices, uvs );
			ScaleUVs( uvs, new Vector2( 1f / mappingWidth, 1f / mappingHeight ) );

			ComputeCurrentSubrectForVertices( mesh, faceVertices, out var subMin, out var subMax );

			var currentSubrect = new RectEditor.RectAssetData.Subrect
			{
				Min = [(int)(subMin.x.Clamp( 0, 1 ) * 32768), (int)(subMin.y.Clamp( 0, 1 ) * 32768)],
				Max = [(int)(subMax.x.Clamp( 0, 1 ) * 32768), (int)(subMax.y.Clamp( 0, 1 ) * 32768)],
			};

			subrectInfo.FindBestSubrectForUVIsland( uvs, currentSubrect, false, RectEditor.RectAssetData.FindSubrectMode.Next, mappingWidth, mappingHeight, out var rectMin, out var rectMax, out var rotated, out var tiling );

			if ( rotated )
			{
				var iA = faceVertices.IndexOf( edgeA );
				var iB = faceVertices.IndexOf( edgeB );
				AlignUVsToEdge( uvs, iA, iB, align == AlignEdgeUV.U ? AlignEdgeUV.V : AlignEdgeUV.U );
			}

			var tileUV = TileUV.None;

			if ( tiling && useTiling )
			{
				tileUV = TileUV.MaintainRatioU;
			}

			RescaleUVsToRectangle( uvs, rectMin, rectMax, tileUV );

			if ( tiling && useTiling )
			{
				var uOffset = Random.Shared.Float( -128.0f, 128.0f );

				for ( int i = 0; i < uvs.Count; ++i )
				{
					var uv = uvs[i];
					uv.x += uOffset;

					if ( uv.x > 1.0f )
					{
						uv.x -= 1.0f;
					}
					else if ( uv.x < 0.0f )
					{
						uv.x += 1.0f;
					}

					uvs[i] = uv;
				}
			}

			ApplyUVsToFaceVertices( mesh, faceVertices, uvs );
		}

		mesh.ComputeFaceTextureParametersFromCoordinates( faces );
	}

	enum AlignEdgeUV
	{
		None,
		U,
		V
	}

	enum TileUV
	{
		None,
		RepeatU,
		RepeatV,
		MaintainRatioU,
		MaintainRatioV,
	}

	static void ApplyUVsToFaceVertices( PolygonMesh pMesh, List<HalfEdgeHandle> faceVertices, List<Vector2> uvs )
	{
		for ( var i = 0; i < faceVertices.Count; ++i )
			pMesh.SetTextureCoord( faceVertices[i], uvs[i] );
	}

	static void ScaleUVs( List<Vector2> uvs, Vector2 scale )
	{
		for ( int i = 0; i < uvs.Count; ++i )
			uvs[i] *= scale;
	}

	static void AlignUVsToEdge( List<Vector2> uvs, int a, int b, AlignEdgeUV align )
	{
		if ( align == AlignEdgeUV.None || a < 0 || b < 0 ) return;

		var uvA = uvs[a];
		var dir = (uvs[b] - uvA).Normal;

		var axisU = align == AlignEdgeUV.U ? dir : new Vector2( dir.y, -dir.x );
		var axisV = align == AlignEdgeUV.U ? new Vector2( -dir.y, dir.x ) : dir;

		for ( var i = 0; i < uvs.Count; ++i )
		{
			var d = uvs[i] - uvA;
			uvs[i] = new Vector2( axisU.Dot( d ), axisV.Dot( d ) );
		}
	}

	static void ComputeCurrentSubrectForVertices( PolygonMesh mesh, List<HalfEdgeHandle> faceVertices, out Vector2 min, out Vector2 max )
	{
		var uvs = new List<Vector2>( faceVertices.Count );
		for ( var i = 0; i < faceVertices.Count; ++i )
			uvs.Add( mesh.GetTextureCoord( faceVertices[i] ) );

		ComputeUVBounds( uvs, out min, out max );

		var offset = new Vector2( MathF.Floor( min.x ), MathF.Floor( min.y ) );
		min -= offset;
		max -= offset;
	}

	static void ComputeUVBounds( List<Vector2> uvs, out Vector2 min, out Vector2 max )
	{
		min = new( float.MaxValue, float.MaxValue );
		max = new( -float.MaxValue, -float.MaxValue );

		for ( var i = 0; i < uvs.Count; ++i )
		{
			var uv = uvs[i];
			min = new( uv.x < min.x ? uv.x : min.x, uv.y < min.y ? uv.y : min.y );
			max = new( uv.x > max.x ? uv.x : max.x, uv.y > max.y ? uv.y : max.y );
		}
	}

	static void RescaleUVsToRectangle( List<Vector2> uvs, Vector2 requestedMin, Vector2 requestedMax, TileUV tileMode, float scale = 1.0f )
	{
		ComputeUVBounds( uvs, out var currentMin, out var currentMax );

		var uvScale = (requestedMax - requestedMin) / (currentMax - currentMin);

		switch ( tileMode )
		{
			case TileUV.RepeatU: uvScale.x = scale / (currentMax.x - currentMin.x); break;
			case TileUV.RepeatV: uvScale.y = scale / (currentMax.y - currentMin.y); break;
			case TileUV.MaintainRatioU: uvScale.x = uvScale.y * scale; break;
			case TileUV.MaintainRatioV: uvScale.y = uvScale.x * scale; break;
		}

		var uvOffset = requestedMin - currentMin * uvScale;

		for ( var i = 0; i < uvs.Count; ++i )
			uvs[i] = uvs[i] * uvScale + uvOffset;
	}

	static bool ScaleUVsOnAxisToMatchWorldSpace( AlignEdgeUV axis, int a, int b, List<Vector3> positions, List<Vector2> uvs )
	{
		if ( axis == AlignEdgeUV.None ) return false;

		if ( (uint)a >= (uint)positions.Count || (uint)b >= (uint)positions.Count ||
			 (uint)a >= (uint)uvs.Count || (uint)b >= (uint)uvs.Count )
			return false;

		var desiredLenSq = positions[a].DistanceSquared( positions[b] );

		var d = uvs[a] - uvs[b];
		const float eps = 1e-8f;

		if ( axis == AlignEdgeUV.U )
		{
			var denom = MathF.Abs( d.x );
			if ( denom < eps ) return false;

			var scale = MathF.Sqrt( MathF.Max( 0f, desiredLenSq - d.y * d.y ) ) / denom;
			ScaleUVs( uvs, new Vector2( scale, 1f ) );
			return true;
		}

		if ( axis == AlignEdgeUV.V )
		{
			var denom = MathF.Abs( d.y );
			if ( denom < eps ) return false;

			var scale = MathF.Sqrt( MathF.Max( 0f, desiredLenSq - d.x * d.x ) ) / denom;
			ScaleUVs( uvs, new Vector2( 1f, scale ) );
			return true;
		}

		return false;
	}

	static bool ScaleUVsToWorldSpace( PolygonMesh mesh, Transform transform, List<FaceHandle> faces, AlignEdgeUV axis, HalfEdgeHandle alignedA, HalfEdgeHandle alignedB, List<HalfEdgeHandle> faceVertices, List<Vector2> uvs )
	{
		if ( axis == AlignEdgeUV.None ) return false;

		var positions = new List<Vector3>( faceVertices.Count );
		var faceVertexToUv = new Dictionary<HalfEdgeHandle, int>( faceVertices.Count );

		for ( var i = 0; i < faceVertices.Count; ++i )
		{
			var vert = mesh.GetVertexConnectedToFaceVertex( faceVertices[i] );
			mesh.GetVertexPosition( vert, transform, out var pos );
			positions.Add( pos );
			faceVertexToUv[faceVertices[i]] = i;
		}

		var iA = faceVertexToUv.TryGetValue( alignedA, out var a ) ? a : -1;
		var iB = faceVertexToUv.TryGetValue( alignedB, out var b ) ? b : -1;
		if ( !ScaleUVsOnAxisToMatchWorldSpace( axis, iA, iB, positions, uvs ) ) return false;

		var ortho = axis == AlignEdgeUV.U ? AlignEdgeUV.V : AlignEdgeUV.U;
		var best = 0.01f;
		var bestA = -1;
		var bestB = -1;

		for ( var i = 0; i < faces.Count; ++i )
		{
			mesh.GetFaceVerticesConnectedToFace( faces[i], out var verts );

			for ( var j = 0; j < verts.Length; ++j )
			{
				var fa = verts[j];
				var fb = verts[(j + 1) % verts.Length];

				var ua = faceVertexToUv.TryGetValue( fa, out a ) ? a : -1;
				var ub = faceVertexToUv.TryGetValue( fb, out b ) ? b : -1;
				if ( ua < 0 || ub < 0 ) continue;

				var dir = (uvs[ub] - uvs[ua]).Normal;
				var align = MathF.Abs( ortho == AlignEdgeUV.U ? dir.x : dir.y );

				if ( align <= best ) continue;
				best = align;
				bestA = ua;
				bestB = ub;
			}
		}

		return ScaleUVsOnAxisToMatchWorldSpace( ortho, bestA, bestB, positions, uvs );
	}

	static AlignEdgeUV FindBestAlignmentEdge( PolygonMesh mesh, Transform transform, List<FaceHandle> faces, out HalfEdgeHandle faceVertexA, out HalfEdgeHandle faceVertexB )
	{
		mesh.FindBoundaryEdgesConnectedToFaces( faces, out var boundaryEdges );

		var bestValue = 0f;
		var bestAxis = 0;
		var bestEdge = HalfEdgeHandle.Invalid;
		var axisWeights = new Vector3( 1f, 1f, 1.01f );

		for ( var i = 0; i < boundaryEdges.Count; ++i )
		{
			var edge = boundaryEdges[i];
			mesh.GetVerticesConnectedToEdge( edge, out var a, out var b );
			mesh.GetVertexPosition( a, transform, out var posA );
			mesh.GetVertexPosition( b, transform, out var posB );

			var dir = (posB - posA).Normal * axisWeights;
			var axis = MathF.Abs( dir.x ) > MathF.Abs( dir.y )
				? (MathF.Abs( dir.x ) > MathF.Abs( dir.z ) ? 0 : 2)
				: (MathF.Abs( dir.y ) > MathF.Abs( dir.z ) ? 1 : 2);

			var value = MathF.Abs( dir[axis] );
			if ( value <= bestValue ) continue;

			bestValue = value;
			bestEdge = edge;
			bestAxis = axis;
		}

		mesh.GetFacesConnectedToEdge( bestEdge, out var faceA, out var faceB );
		var face = faces.Contains( faceA ) ? faceA : faceB;

		mesh.GetVerticesConnectedToEdge( bestEdge, face, out var vertA, out var vertB );
		mesh.GetVertexPosition( vertA, transform, out var pos1 );
		mesh.GetVertexPosition( vertB, transform, out var pos2 );

		var dir2 = (pos2 - pos1).Normal;
		var axis2 = MathF.Abs( dir2.x ) > MathF.Abs( dir2.y )
			? (MathF.Abs( dir2.x ) > MathF.Abs( dir2.z ) ? 0 : 2)
			: (MathF.Abs( dir2.y ) > MathF.Abs( dir2.z ) ? 1 : 2);

		if ( (axis2 == 0 && dir2[axis2] < 0) || (axis2 != 0 && dir2[axis2] > 0) )
			(vertA, vertB) = (vertB, vertA);

		faceVertexA = mesh.FindFaceVertexConnectedToVertex( vertA, face );
		faceVertexB = mesh.FindFaceVertexConnectedToVertex( vertB, face );
		return faceVertexA == HalfEdgeHandle.Invalid || faceVertexB == HalfEdgeHandle.Invalid
			? AlignEdgeUV.None
			: bestAxis == 0 ? AlignEdgeUV.U : AlignEdgeUV.V;
	}
}
