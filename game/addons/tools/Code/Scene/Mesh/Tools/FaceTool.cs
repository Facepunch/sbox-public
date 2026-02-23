
using HalfEdgeMesh;

namespace Editor.MeshEditor;

/// <summary>
/// Select and edit face geometry.
/// </summary>
[Title( "Face Tool" )]
[Icon( "change_history" )]
[Alias( "tools.face-tool" )]
[Group( "3" )]
public sealed partial class FaceTool( MeshTool tool ) : SelectionTool<MeshFace>( tool )
{
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

		if ( Gizmo.IsHovered && Tool.MoveMode.AllowSceneSelection && !IsLassoSelecting )
		{
			SelectFace();

			if ( Gizmo.IsDoubleClicked )
				SelectContiguousFaces();
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
	/// Select all faces connected to the target face through shared edges (flood-fill)
	/// </summary>
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

	/// <summary>
	/// Select connected faces with normals within a threshold angle (default 12 degrees)
	/// Uses flood-fill to only select adjacent faces with similar normals
	/// </summary>
	private void SelectFacesByNormal( MeshFace targetFace )
	{
		if ( !targetFace.IsValid() )
			return;

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

	protected override IEnumerable<MeshFace> GetConnectedSelectionElements()
	{
		var unique = new HashSet<MeshFace>();

		foreach ( var component in Selection.OfType<GameObject>()
			.Select( x => x.GetComponent<MeshComponent>() )
			.Where( x => x.IsValid() ) )
		{
			foreach ( var face in component.Mesh.FaceHandles )
			{
				unique.Add( new MeshFace( component, face ) );
			}
		}

		foreach ( var edge in Selection.OfType<MeshEdge>() )
		{
			edge.Component.Mesh.GetFacesConnectedToEdge( edge.Handle, out var faceA, out var faceB );
			unique.Add( new MeshFace( edge.Component, faceA ) );
			unique.Add( new MeshFace( edge.Component, faceB ) );
		}

		foreach ( var vertex in Selection.OfType<MeshVertex>() )
		{
			vertex.Component.Mesh.GetFacesConnectedToVertex( vertex.Handle, out var faces );

			foreach ( var face in faces )
			{
				unique.Add( new MeshFace( vertex.Component, face ) );
			}
		}

		return unique;
	}
}
