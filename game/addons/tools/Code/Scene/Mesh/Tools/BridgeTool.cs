using HalfEdgeMesh;

namespace Editor.MeshEditor;

[Alias( "tools.bridge-tool" )]
public partial class BridgeTool( MeshEdge[] edges = null, MeshFace[] faces = null ) : EditorTool
{
	MeshComponent _meshA;
	MeshComponent _meshB;
	PolygonMesh _originalMeshA;
	PolygonMesh _originalMeshB;
	PolygonMesh _editMesh;
	List<HalfEdgeHandle> _fromEdges;
	List<HalfEdgeHandle> _toEdges;
	List<HalfEdgeHandle> _createdEdges;

	bool _controlPointDragFree;
	bool _controlPointDragLocked;

	struct ControlPointBasis
	{
		public Vector3 Position;
		public Vector3 Normal;
		public Vector3 Tangent;
		public bool HasPlane;
		public Plane MotionPlane;
	}

	enum BridgeControlPoint
	{
		Invalid = -1,
		From = 0,
		To = 1,
		Count = 2
	}

	readonly ControlPointBasis[] _basis = new ControlPointBasis[(int)BridgeControlPoint.Count];
	readonly Vector3[] _controlPoints = new Vector3[(int)BridgeControlPoint.Count];

	public override void OnEnabled()
	{
		if ( (edges == null || edges.Length == 0) && (faces == null || faces.Length == 0) )
			return;

		var editMesh = new PolygonMesh();

		List<HalfEdgeHandle> allEdges = new();

		if ( faces != null && faces.Length > 0 )
		{
			var faceGroups = faces.GroupBy( x => x.Component ).ToList();
			if ( faceGroups.Count == 0 || faceGroups.Count > 2 )
				return;

			var a = faceGroups[0].Key;

			editMesh.Transform = a.Mesh.Transform;
			editMesh.MergeMesh( a.Mesh, Transform.Zero, out _, out _, out var remappedFaces );

			var faceHandles = faceGroups[0]
				.Select( x => x.Handle )
				.Select( x => remappedFaces[x] )
				.ToList();

			_meshA = a;
			_originalMeshA = a.Mesh;

			if ( faceGroups.Count == 2 )
			{
				var b = faceGroups[1].Key;
				var transform = a.WorldTransform.ToLocal( b.WorldTransform );

				editMesh.MergeMesh( b.Mesh, transform, out _, out _, out remappedFaces );

				var faceHandlesB = faceGroups[1]
					.Select( x => x.Handle )
					.Select( x => remappedFaces[x] )
					.ToList();

				faceHandles.AddRange( faceHandlesB );

				_meshB = b;
				_originalMeshB = b.Mesh;
			}

			editMesh.FindBoundaryEdgesConnectedToFaces( faceHandles, out allEdges );
			editMesh.FindClosedFaces( faceHandles, out var facesToDelete );
			editMesh.RemoveFaces( facesToDelete );
		}
		else
		{
			var edgeGroups = edges.GroupBy( x => x.Component ).ToList();
			var numMeshes = edgeGroups.Count;
			if ( numMeshes <= 0 || numMeshes > 2 ) return;

			var a = edgeGroups[0].Key;

			editMesh.Transform = a.Mesh.Transform;
			editMesh.MergeMesh( a.Mesh, Transform.Zero, out _, out var remappedEdges, out _ );

			allEdges.AddRange(
				edgeGroups[0]
					.Select( x => x.Handle )
					.Select( x => remappedEdges[x] )
			);

			_meshA = a;
			_originalMeshA = a.Mesh;

			if ( edgeGroups.Count == 2 )
			{
				var b = edgeGroups[1].Key;
				var transform = a.WorldTransform.ToLocal( b.WorldTransform );

				editMesh.MergeMesh( b.Mesh, transform, out _, out remappedEdges, out _ );

				allEdges.AddRange(
					edgeGroups[1]
						.Select( x => x.Handle )
						.Select( x => remappedEdges[x] )
				);

				_meshB = b;
				_originalMeshB = b.Mesh;
			}
		}

		editMesh.FindOpenEdgeIslands( allEdges, out var edgeIslands );

		if ( edgeIslands.Count == 2 )
		{
			var edgesA = edgeIslands[0];
			var edgesB = edgeIslands[1];

			if ( edgesA.Count == edgesB.Count )
			{
				if ( editMesh.CorrelateOpenEdges( edgesA, edgesB, out _fromEdges, out _toEdges ) )
				{
					_meshA?.Mesh = editMesh;
					_meshB?.Mesh = new PolygonMesh();
					_editMesh = editMesh;

					ComputeBasis();
					ComputeDefaultControlPoints();
				}
			}
		}
	}

	public override void OnUpdate()
	{
		if ( _meshA is null || _meshA.Mesh is null )
			return;

		var mesh = _meshA.Mesh;
		var transform = _meshA.WorldTransform;

		if ( _createdEdges is not null )
		{
			Gizmo.Draw.LineThickness = 2;
			Gizmo.Draw.Color = new Color( 0.3137f, 0.7843f, 1f, 0.5f );

			foreach ( var edge in _createdEdges )
			{
				if ( !edge.IsValid ) continue;

				mesh.GetEdgeVertexPositions( edge, transform, out var a, out var b );
				Gizmo.Draw.Line( a, b );
			}
		}

		DrawControlPoints();
	}

	int _steps;
	int _twist;
	PolygonMesh.BridgeUVMode _uvMode;
	float _repeatsU;
	float _repeatsV;

	void UpdateBridge( int steps, int twist, PolygonMesh.BridgeUVMode uvMode, float repeatsU, float repeatsV )
	{
		if ( _editMesh is null ) return;

		steps = steps.Clamp( 1, 128 );
		twist = twist.Clamp( -100, 100 );

		_steps = steps;
		_twist = twist;
		_uvMode = uvMode;
		_repeatsU = repeatsU;
		_repeatsV = repeatsV;

		var mesh = new PolygonMesh();
		mesh.Transform = _editMesh.Transform;
		mesh.MergeMesh( _editMesh, Transform.Zero, out _, out var remappedEdges, out _ );

		var fromEdges = _fromEdges.Select( x => remappedEdges[x] ).ToList();
		var toEdges = _toEdges.Select( x => remappedEdges[x] ).ToList();

		var numEdges = toEdges.Count;

		if ( twist < 0 )
		{
			twist = -twist;
			twist = numEdges - (twist % numEdges);
		}

		var twistedToEdges = new List<HalfEdgeHandle>( numEdges );
		for ( int i = 0; i < numEdges; ++i )
			twistedToEdges.Add( toEdges[(i + twist) % numEdges] );

		var fromBasis = _basis[(int)BridgeControlPoint.From];
		var toBasis = _basis[(int)BridgeControlPoint.To];

		float distance = fromBasis.Position.Distance( toBasis.Position );

		if ( distance <= 0f )
			return;

		var fromDelta = (_controlPoints[(int)BridgeControlPoint.From] - fromBasis.Position) / distance;
		var toDelta = (_controlPoints[(int)BridgeControlPoint.To] - toBasis.Position) / distance;

		var parameters = new PolygonMesh.BridgeInterpolationParameters
		{
			NumSteps = steps,

			FromDeltaN = Vector3.Dot( fromDelta, fromBasis.Normal ),
			FromDeltaT = fromBasis.HasPlane ? Vector3.Dot( fromDelta, fromBasis.Tangent ) : 0f,

			ToDeltaN = Vector3.Dot( toDelta, toBasis.Normal ),
			ToDeltaT = toBasis.HasPlane ? Vector3.Dot( toDelta, toBasis.Tangent ) : 0f,

			RepeatsU = repeatsU,
			RepeatsV = repeatsV,
			UVMode = uvMode
		};

		if ( mesh.BridgeEdgesInterpolated( fromEdges, twistedToEdges, parameters, out _createdEdges ) )
			_createdEdges.AddRange( toEdges );

		_meshA.Mesh = mesh;
	}

	void GoBack()
	{
		EditorToolManager.SetSubTool( faces != null && faces.Length > 0 ? nameof( FaceTool ) : nameof( EdgeTool ) );
	}

	public override void OnDisabled()
	{
		_meshA?.Mesh = _originalMeshA;
		_meshB?.Mesh = _originalMeshB;

		Cleanup();
	}

	void Cancel()
	{
		_meshA?.Mesh = _originalMeshA;
		_meshB?.Mesh = _originalMeshB;

		Cleanup();

		GoBack();
	}

	void Apply()
	{
		if ( _editMesh is null ) return;

		var mesh = _meshA.Mesh;
		_meshA.Mesh = _originalMeshA;
		_meshB?.Mesh = _originalMeshB;

		using var scope = SceneEditorSession.Scope();
		var undo = SceneEditorSession.Active.UndoScope( "Apply Bridge" )
			.WithComponentChanges( _meshA );

		if ( _meshB is not null )
			undo.WithGameObjectDestructions( _meshB.GameObject );

		using ( undo.Push() )
		{
			_meshA.Mesh = mesh;
			_meshB?.GameObject.Destroy();

			Selection.Clear();

			if ( _createdEdges is not null )
			{
				Selection.Clear();

				if ( faces != null && faces.Length > 0 )
				{
					SelectionTool.ClearPreviousSelections<MeshFace>();

					var candidateFaces = new HashSet<MeshFace>();

					foreach ( var edge in _createdEdges )
					{
						mesh.GetFacesConnectedToEdge( edge, out var faceA, out var faceB );

						if ( faceA.IsValid )
							candidateFaces.Add( new MeshFace( _meshA, faceA ) );

						if ( faceB.IsValid )
							candidateFaces.Add( new MeshFace( _meshA, faceB ) );
					}

					foreach ( var f in candidateFaces )
					{
						Selection.Add( f );
					}
				}
				else
				{
					SelectionTool.ClearPreviousSelections<MeshEdge>();

					foreach ( var e in _createdEdges )
						Selection.Add( new MeshEdge( _meshA, e ) );
				}
			}
		}

		Cleanup();

		GoBack();
	}

	void Cleanup()
	{
		_meshA = null;
		_meshB = null;
		_originalMeshA = null;
		_originalMeshB = null;
		_editMesh = null;
		_fromEdges = null;
		_toEdges = null;
	}

	Vector3 ComputeFromCenter()
	{
		return ComputeEdgeListCenter( _fromEdges );
	}

	Vector3 ComputeToCenter()
	{
		return ComputeEdgeListCenter( _toEdges );
	}

	Vector3 ComputeFromNormal()
	{
		return ComputeEdgeListNormal( _fromEdges );
	}

	Vector3 ComputeToNormal()
	{
		return ComputeEdgeListNormal( _toEdges );
	}

	Vector3 ComputeEdgeListCenter( List<HalfEdgeHandle> edges )
	{
		if ( edges == null || edges.Count == 0 || _meshA == null )
			return Vector3.Zero;

		var mesh = _meshA.Mesh;
		var connectivity = mesh.ClassifyEdgeListConnectivity( edges );

		if ( connectivity == ComponentConnectivityType.Loop )
		{
			Vector3 sum = Vector3.Zero;
			float count = 0f;

			int numEdges = edges.Count;
			for ( int i = 0; i < numEdges; i++ )
			{
				var edge = edges[i];
				if ( !edge.IsValid )
					continue;

				mesh.GetEdgeVertexPositions( edge, _meshA.WorldTransform, out var a, out var b );

				sum += (a + b) * 0.5f;
				count += 1f;
			}

			return count > 0f ? sum / count : Vector3.Zero;
		}
		else
		{
			int numEdges = edges.Count;

			var edgeA = edges[numEdges / 2];
			var edgeB = edges[(numEdges - 1) / 2];

			mesh.GetEdgeVertexPositions( edgeA, _meshA.WorldTransform, out var a0, out var a1 );
			mesh.GetEdgeVertexPositions( edgeB, _meshA.WorldTransform, out var b0, out var b1 );

			return (a0 + a1 + b0 + b1) * 0.25f;
		}
	}

	Vector3 ComputeEdgeListNormal( List<HalfEdgeHandle> edges )
	{
		if ( edges == null || edges.Count == 0 || _meshA == null )
			return Vector3.Zero;

		var mesh = _meshA.Mesh;
		var transform = _meshA.WorldTransform;
		var connectivity = mesh.ClassifyEdgeListConnectivity( edges );

		Vector3 normal;

		if ( connectivity == ComponentConnectivityType.Loop )
		{
			mesh.ComputeNormalForOpenEdgeLoop( edges, transform, out normal, out _ );
		}
		else
		{
			normal = mesh.ComputeOpenEdgeExtendDirection( edges[edges.Count / 2] ).Normal;
			normal = transform.NormalToWorld( normal );
		}

		return normal;
	}

	void SetControlPointFromDeltas( BridgeControlPoint cp, float normalDelta, float tangentDelta )
	{
		var basis = _basis[(int)cp];
		var position = basis.Position + basis.Normal * normalDelta;

		if ( basis.HasPlane )
			position += basis.Tangent * tangentDelta;

		_controlPoints[(int)cp] = position;
	}

	void ComputeDefaultControlPoints()
	{
		var fromPosition = _basis[(int)BridgeControlPoint.From].Position;
		var toPosition = _basis[(int)BridgeControlPoint.To].Position;
		var fromNormal = _basis[(int)BridgeControlPoint.From].Normal;
		var toNormal = _basis[(int)BridgeControlPoint.To].Normal;

		const float epsilon = 0.01f;

		if ( Vector3.Dot( fromNormal, toPosition - fromPosition ) <= -epsilon )
		{
			fromNormal = -fromNormal;
		}

		if ( Vector3.Dot( toNormal, fromPosition - toPosition ) <= -epsilon )
		{
			toNormal = -toNormal;
		}

		float distance = fromPosition.Distance( toPosition );

		const float sqrt2 = 1.41421356f;
		const float kappa = (4f * (sqrt2 - 1f)) / 3f;
		float radius = distance / sqrt2;

		_controlPoints[(int)BridgeControlPoint.From] = fromPosition + fromNormal * radius * kappa;
		_controlPoints[(int)BridgeControlPoint.To] = toPosition + toNormal * radius * kappa;
	}

	void ComputeBasis()
	{
		var from = _basis[(int)BridgeControlPoint.From];
		var to = _basis[(int)BridgeControlPoint.To];

		from.Position = ComputeFromCenter();
		to.Position = ComputeToCenter();

		var fromNormalOriginal = ComputeFromNormal();
		var toNormalOriginal = ComputeToNormal();

		from.HasPlane = ComputeInterpolationTangentBasis(
			from.Position,
			to.Position,
			fromNormalOriginal,
			out var fromInterpNormal,
			out var fromTangent,
			out var fromPlane
		);

		to.HasPlane = ComputeInterpolationTangentBasis(
			to.Position,
			from.Position,
			toNormalOriginal,
			out var toInterpNormal,
			out var toTangent,
			out var toPlane
		);

		from.Normal = fromNormalOriginal;
		to.Normal = toNormalOriginal;

		from.Tangent = fromTangent;
		from.MotionPlane = fromPlane;

		to.Tangent = toTangent;
		to.MotionPlane = toPlane;

		_basis[(int)BridgeControlPoint.From] = from;
		_basis[(int)BridgeControlPoint.To] = to;
	}

	static bool ComputeInterpolationTangentBasis( Vector3 basePos, Vector3 targetPos, Vector3 originalNormal, out Vector3 outNormal, out Vector3 outTangent, out Plane outPlane )
	{
		outTangent = Vector3.Zero;
		outPlane = default;

		var toTarget = (targetPos - basePos).Normal;
		var plane = new Plane( basePos, originalNormal );
		var normal = plane.IsInFront( targetPos ) ? originalNormal : -originalNormal;

		outNormal = normal;

		if ( MathF.Abs( Vector3.Dot( toTarget, normal ) ) > 0.999f )
			return false;

		var binormal = Vector3.Cross( toTarget, normal ).Normal;
		var tangent = Vector3.Cross( binormal, normal ).Normal;

		outTangent = tangent;
		outPlane = new Plane( basePos, binormal );

		return true;
	}

	bool _dragging;
	BridgeControlPoint _draggingCP = BridgeControlPoint.Invalid;

	void DrawControlPoints()
	{
		Gizmo.Draw.IgnoreDepth = true;
		Gizmo.Draw.LineThickness = 2;

		var controlPointColor = new Color( 0.50196f, 1f, 1f );

		for ( int i = 0; i < (int)BridgeControlPoint.Count; i++ )
		{
			var cp = (BridgeControlPoint)i;
			var position = _controlPoints[i];
			var basis = _basis[i];

			var size = 3.0f * Gizmo.Camera.Position.Distance( position ) / 1000.0f;
			var axisLength = Gizmo.Camera.Position.Distance( basis.Position ) * 0.05f;

			using ( Gizmo.Scope( $"bridge_cp_{i}" ) )
			{
				Gizmo.Hitbox.DepthBias = 0.01f;
				Gizmo.Hitbox.Sphere( new Sphere( position, size * 2 ) );

				if ( Gizmo.Pressed.This )
				{
					if ( !_dragging )
					{
						_dragging = true;
						_draggingCP = cp;
					}

					UpdateControlPointDrag( cp );
				}
				else if ( _dragging && _draggingCP == cp )
				{
					_dragging = false;
					_draggingCP = BridgeControlPoint.Invalid;
					_controlPointDragFree = false;
					_controlPointDragLocked = false;
				}

				Gizmo.Draw.Color = controlPointColor;
				Gizmo.Draw.Line( basis.Position, position );

				Gizmo.Draw.Color = Color.Blue;
				Gizmo.Draw.Line( basis.Position, basis.Position + basis.Normal * axisLength );

				if ( basis.HasPlane )
				{
					Gizmo.Draw.Color = Color.Red;
					Gizmo.Draw.Line( basis.Position, basis.Position + basis.Tangent * axisLength );

					Gizmo.Draw.Color = Color.Green;
					Gizmo.Draw.Line( basis.Position, basis.Position + basis.MotionPlane.Normal * axisLength );
				}

				Gizmo.Draw.Color = controlPointColor;
				Gizmo.Draw.SolidSphere( position, Gizmo.IsHovered ? size * 2 : size );
			}
		}
	}

	void UpdateControlPointDrag( BridgeControlPoint cp )
	{
		var basis = _basis[(int)cp];
		var controlPointPosition = _controlPoints[(int)cp];

		if ( Gizmo.IsShiftPressed )
			_controlPointDragFree = true;

		if ( Gizmo.IsCtrlPressed )
			_controlPointDragLocked = true;

		var viewPlane = new Plane( controlPointPosition, Gizmo.Camera.Rotation.Forward );

		if ( !viewPlane.TryTrace( Gizmo.CurrentRay, out var targetPos, true ) )
			return;

		if ( basis.HasPlane && _controlPointDragFree )
		{
			targetPos = basis.MotionPlane.SnapToPlane( targetPos );
			controlPointPosition = GridSnapOnPlane( targetPos, basis.Position, basis.MotionPlane.Normal, basis.Tangent );
		}
		else
		{
			var pointA = basis.Position;
			var pointB = basis.Position + basis.Normal;

			var lineDir = (pointB - pointA).Normal;
			var projected = pointA + lineDir * Vector3.Dot( targetPos - pointA, lineDir );

			controlPointPosition = GridSnapAlongLine( projected, basis.Position, basis.Normal );
		}

		_controlPoints[(int)cp] = controlPointPosition;

		if ( _controlPointDragLocked )
		{
			var delta = controlPointPosition - basis.Position;
			float normalDelta = Vector3.Dot( delta, basis.Normal );
			float tangentDelta = basis.HasPlane ? Vector3.Dot( delta, basis.Tangent ) : 0.0f;

			SetControlPointFromDeltas( BridgeControlPoint.From, normalDelta, tangentDelta );
			SetControlPointFromDeltas( BridgeControlPoint.To, normalDelta, tangentDelta );
		}

		UpdateBridge( _steps, _twist, _uvMode, _repeatsU, _repeatsV );
	}

	static Vector3 GridSnapOnPlane( Vector3 point, Vector3 origin, Vector3 planeNormal, Vector3 tangent )
	{
		var basis = Rotation.LookAt( planeNormal, tangent );
		var local = (point - origin) * basis.Inverse;
		local = Gizmo.Snap( local, new Vector3( 0, 1, 1 ) );
		return origin + local * basis;
	}

	static Vector3 GridSnapAlongLine( Vector3 point, Vector3 origin, Vector3 axis )
	{
		var basis = Rotation.LookAt( axis );
		var local = (point - origin) * basis.Inverse;
		local = Gizmo.Snap( local, new Vector3( 1, 0, 0 ) );
		local.x = MathF.Max( 0, local.x );
		return origin + local * basis;
	}
}
