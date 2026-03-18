using HalfEdgeMesh;

namespace Editor.MeshEditor;

/// <summary>
/// Sculpt and displace mesh vertices.
/// </summary>
[Title( "Displacement Tool" )]
[Icon( "landscape" )]
[Alias( "tools.displacement-tool" )]
[Group( "7" )]
public partial class DisplacementTool( MeshTool tool ) : EditorTool
{
	protected MeshTool Tool { get; private init; } = tool;

	public enum DisplaceMode
	{
		[Icon( "arrow_upward" )] Raise,
		[Icon( "arrow_downward" )] Lower,
		[Icon( "blur_on" )] Smooth,
		[Icon( "horizontal_rule" )] Flatten,
		[Icon( "compress" )] Pinch,
		[Icon( "open_with" )] Move,
	}

	public enum NormalMode
	{
		/// <summary>
		/// Average of the surface normals inside the brush radius.
		/// </summary>
		[Icon( "auto_awesome" )] Average,
		/// <summary>
		/// Normal sampled at the stroke start, stays fixed for the whole stroke.
		/// </summary>
		[Icon( "push_pin" )] MouseDown,
		/// <summary>
		/// World-space X axis.
		/// </summary>
		[Icon( "arrow_forward" )] X,
		/// <summary>
		/// World-space Y axis.
		/// </summary>
		[Icon( "arrow_upward" )] Y,
		/// <summary>
		/// World-space Z (up axis).
		/// </summary>
		[Icon( "vertical_align_top" )] Z,
	}

	public enum FlattenMode
	{
		/// <summary>
		/// Push vertices toward the flatten plane from both sides.
		/// </summary>
		[Icon( "horizontal_rule" )] Center,
		/// <summary>
		/// Only raise vertices up to the flatten plane.
		/// </summary>
		[Icon( "arrow_upward" )] RaiseOnly,
		/// <summary>
		/// Only lower vertices down to the flatten plane.
		/// </summary>
		[Icon( "arrow_downward" )] LowerOnly,
	}

	enum DisplaceLimitMode
	{
		[Icon( "public" )] Everything,
		[Icon( "category" )] Objects,
		[Icon( "square" )] Faces,
		[Icon( "fiber_manual_record" )] Vertices
	}

	[WideMode]
	DisplaceLimitMode LimitMode
	{
		get => _limitMode;
		set { _limitMode = value; RebuildSelection(); }
	}
	DisplaceLimitMode _limitMode = DisplaceLimitMode.Objects;

	[WideMode] DisplaceMode Mode { get; set; } = DisplaceMode.Raise;
	[WideMode] FlattenMode FlattenSubMode { get; set; } = FlattenMode.Center;
	[WideMode] NormalMode NormalDir { get; set; } = NormalMode.Average;

	[WideMode, Range( 10, 1000 )] float Radius { get; set; } = 50;
	[WideMode, Range( 0, 1 )] float Strength { get; set; } = 0.5f;
	[WideMode, Range( 0, 1 )] float Hardness { get; set; } = 0.5f;

	bool PaintBackfacing { get; set; } = false;
	bool ShowVerts { get; set; } = true;

	readonly HashSet<MeshComponent> _selectedMeshes = [];
	readonly HashSet<int> _allowedVertexIndices = [];

	Dictionary<int, Vector3> _prevPositions;
	Dictionary<int, Vector3> _deltaPositions;

	PolygonMesh _activeMesh;
	MeshComponent _activeComponent;

	Vector3 _lastCheckedPos;
	float _distanceSinceLastDrop;
	Vector3 _lastHitPos;
	Vector3 _lastHitNormal;

	Vector3 _lastDisplaceNormal;
	Vector2? _cursorLockPosition;

	Vector3 _flattenPlanePoint;
	Vector3 _flattenPlaneNormal;

	Vector3 _strokeNormal;
	Vector3 _moveLastHitPos;

	const float DropSpacing = 8.0f;
	const float DisplaceAmplitude = 16f;
	IDisposable _undoScope;


	public override void OnEnabled()
	{
		RebuildSelection();
	}
	public override void OnSelectionChanged() => RebuildSelection();


	void RebuildSelection()
	{
		_selectedMeshes.Clear();
		_allowedVertexIndices.Clear();

		switch ( LimitMode )
		{
			case DisplaceLimitMode.Everything:
				break;

			case DisplaceLimitMode.Objects:
				_selectedMeshes.UnionWith( Selection
				.OfType<GameObject>()
				.Select( go => go.GetComponent<MeshComponent>() )
				.Where( mc => mc.IsValid() ) );

				foreach ( var face in SelectionTool.GetAllSelected<MeshFace>() )
					if ( face.IsValid() ) _selectedMeshes.Add( face.Component );

				foreach ( var edge in SelectionTool.GetAllSelected<MeshEdge>() )
					if ( edge.IsValid() ) _selectedMeshes.Add( edge.Component );

				foreach ( var vert in SelectionTool.GetAllSelected<MeshVertex>() )
					if ( vert.IsValid() ) _selectedMeshes.Add( vert.Component );
				break;

			case DisplaceLimitMode.Faces:
				foreach ( var face in SelectionTool.GetAllSelected<MeshFace>() )
				{
					if ( !face.IsValid() ) continue;
					face.Component.Mesh.GetVerticesConnectedToFace( face.Handle, out var vertices );
					if ( vertices != null )
						foreach ( var v in vertices )
							if ( v.IsValid ) _allowedVertexIndices.Add( v.Index );
				}
				break;

			case DisplaceLimitMode.Vertices:
				foreach ( var vert in SelectionTool.GetAllSelected<MeshVertex>() )
					if ( vert.IsValid() ) _allowedVertexIndices.Add( vert.Handle.Index );
				break;
		}
	}


	public override void OnUpdate()
	{
		if ( LimitMode != DisplaceLimitMode.Everything && Gizmo.IsShiftPressed && Gizmo.WasRightMousePressed )
		{
			var addFace = MeshTrace.TraceFace( out _ );
			if ( addFace.IsValid() )
			{
				var component = addFace.Component;
				var addMesh = component.Mesh;

				switch ( LimitMode )
				{
					case DisplaceLimitMode.Objects:
						_selectedMeshes.Add( component );
						Selection.Add( component.GameObject );
						break;

					case DisplaceLimitMode.Faces:
						addMesh.GetVerticesConnectedToFace( addFace.Handle, out var verts );
						if ( verts != null )
							foreach ( var v in verts )
								if ( v.IsValid ) _allowedVertexIndices.Add( v.Index );
						SelectionTool.AddToPreviousSelections( addFace );
						break;
				}
			}
			return;
		}

		var face = LimitMode != DisplaceLimitMode.Everything
		? TraceSelectedFace( out var hitPosition )
		: MeshTrace.TraceFace( out hitPosition );

		if ( !face.IsValid() )
			return;

		var mesh = face.Component.Mesh;
		mesh.ComputeFaceNormal( face.Handle, out var faceNormal );

		if ( Application.MouseButtons.HasFlag( MouseButtons.Middle ) )
		{
			_cursorLockPosition ??= Application.UnscaledCursorPosition;
			var d = Application.UnscaledCursorPosition - _cursorLockPosition.Value;

			if ( Gizmo.IsShiftPressed )
				Radius = (Radius + d.x * 0.25f).Clamp( 10, 1000 );
			else if ( Gizmo.IsCtrlPressed )
			{
				Strength = (Strength - d.y * 0.002f).Clamp( 0, 1 );
				Hardness = (Hardness + d.x * 0.002f).Clamp( 0, 1 );
			}

			Application.UnscaledCursorPosition = _cursorLockPosition.Value;
			SceneOverlay.Parent.Cursor = CursorShape.Blank;

			DrawBrushAdjustText();
			DrawBrush( _lastHitPos, _lastHitNormal, mesh );
			return;
		}
		else
		{
			if ( _cursorLockPosition.HasValue )
				SceneOverlay.Parent.Cursor = CursorShape.None;
			_cursorLockPosition = null;
		}

		_lastHitPos = hitPosition;
		_lastHitNormal = faceNormal;

		var strokeNormalSafe = _strokeNormal.LengthSquared > 0.001f ? _strokeNormal : faceNormal;
		_lastDisplaceNormal = NormalDir switch
		{
			NormalMode.Average => ComputeAverageNormalInRadius( mesh, hitPosition ),
			NormalMode.MouseDown => strokeNormalSafe,
			NormalMode.X => (mesh.Transform.Rotation.Inverse * Vector3.Right).Normal,
			NormalMode.Y => (mesh.Transform.Rotation.Inverse * Vector3.Forward).Normal,
			NormalMode.Z => (mesh.Transform.Rotation.Inverse * Vector3.Up).Normal,
			_ => faceNormal,
		};

		if ( Gizmo.WasLeftMousePressed )
			BeginStroke( face.Component, hitPosition, faceNormal );

		if ( _prevPositions != null && Gizmo.WasLeftMouseReleased )
			EndStroke();

		if ( _activeMesh != null && mesh != _activeMesh )
			return;

		if ( !Gizmo.IsLeftMouseDown )
		{
			DrawBrush( hitPosition, faceNormal, mesh );
			return;
		}

		var frameDist = hitPosition.Distance( _lastCheckedPos );
		_distanceSinceLastDrop += frameDist;
		_lastCheckedPos = hitPosition;

		if ( !Gizmo.WasLeftMousePressed && _distanceSinceLastDrop < DropSpacing && Mode != DisplaceMode.Move )
		{
			DrawBrush( hitPosition, faceNormal, mesh );
			return;
		}

		_distanceSinceLastDrop = 0f;
		ApplyDisplacement( hitPosition, faceNormal );
		DrawBrush( hitPosition, faceNormal, mesh );
	}


	MeshFace TraceSelectedFace( out Vector3 hitPosition )
	{
		var ray = Gizmo.CurrentRay;
		var depth = Gizmo.RayDepth;

		for ( int i = 0; i < 32 && depth > 0f; i++ )
		{
			var result = MeshTrace.Ray( ray, depth ).Run();
			if ( !result.Hit ) break;

			var advance = result.Distance + 0.01f;
			ray = new Ray( ray.Project( advance ), ray.Forward );
			depth -= advance;

			if ( result.Component is not MeshComponent component ) continue;

			var f = new MeshFace( component, component.Mesh.TriangleToFace( result.Triangle ) );
			if ( f.IsValid() && IsFaceAllowed( f ) )
			{
				hitPosition = result.HitPosition;
				return f;
			}
		}

		hitPosition = default;
		return default;
	}

	bool IsFaceAllowed( MeshFace face )
	{
		return LimitMode switch
		{
			DisplaceLimitMode.Everything => true,
			DisplaceLimitMode.Objects => _selectedMeshes.Contains( face.Component ),
			_ => HasAllowedVertex( face )
		};
	}

	bool HasAllowedVertex( MeshFace face )
	{
		face.Component.Mesh.GetVerticesConnectedToFace( face.Handle, out var vertices );
		return vertices?.Any( v => _allowedVertexIndices.Contains( v.Index ) ) ?? false;
	}

	bool IsVertexAllowed( VertexHandle vertex )
	{
		return LimitMode switch
		{
			DisplaceLimitMode.Everything => true,
			DisplaceLimitMode.Objects => true,
			_ => _allowedVertexIndices.Contains( vertex.Index )
		};
	}


	void BeginStroke( MeshComponent component, Vector3 hitWorldPos, Vector3 hitLocalNormal )
	{
		_activeComponent = component;
		_activeMesh = component.Mesh;

		_undoScope ??= SceneEditorSession.Active
		.UndoScope( "Displacement Stroke" )
		.WithComponentChanges( component )
		.Push();

		_prevPositions = new Dictionary<int, Vector3>();
		_deltaPositions = new Dictionary<int, Vector3>();

		foreach ( var vertex in _activeMesh.VertexHandles )
		{
			var pos = _activeMesh.GetVertexPosition( vertex );
			_prevPositions[vertex.Index] = pos;
			_deltaPositions[vertex.Index] = Vector3.Zero;
		}

		_flattenPlanePoint = _activeMesh.Transform.PointToLocal( hitWorldPos );
		_flattenPlaneNormal = hitLocalNormal;

		_strokeNormal = hitLocalNormal;
		_moveLastHitPos = hitWorldPos;

		_lastCheckedPos = hitWorldPos;
		_distanceSinceLastDrop = 0f;
	}

	void EndStroke()
	{
		_prevPositions = null;
		_deltaPositions = null;
		_activeMesh = null;
		_activeComponent = null;

		_undoScope?.Dispose();
		_undoScope = null;
	}


	void ApplyDisplacement( Vector3 hitWorldPos, Vector3 hitLocalNormal )
	{
		if ( _activeMesh == null || _prevPositions == null )
			return;

		var mesh = _activeMesh;
		var radiusSq = Radius * Radius;

		var effectiveMode = Mode;

		if ( Gizmo.IsShiftPressed )
		{
			effectiveMode = DisplaceMode.Smooth;
		}
		else if ( Gizmo.IsCtrlPressed )
		{
			effectiveMode = Mode switch
			{
				DisplaceMode.Raise => DisplaceMode.Lower,
				DisplaceMode.Lower => DisplaceMode.Raise,
				_ => Mode
			};
		}

		var displaceNormal = GetDisplacementNormal( mesh, hitWorldPos, hitLocalNormal );
		var localHitPos = mesh.Transform.PointToLocal( hitWorldPos );
		var boundaryVerts = effectiveMode == DisplaceMode.Smooth ? ComputeBoundaryVertices( mesh ) : null;

		var localMoveDelta = Vector3.Zero;
		if ( effectiveMode == DisplaceMode.Move )
		{
			var movePlane = new Plane( _moveLastHitPos, Gizmo.Camera.Rotation.Forward );
			if ( movePlane.TryTrace( Gizmo.CurrentRay, out var worldPos, true ) )
			{
				localMoveDelta = mesh.Transform.PointToLocal( worldPos ) - mesh.Transform.PointToLocal( _moveLastHitPos );
				_moveLastHitPos = worldPos;
			}
		}

		foreach ( var vertex in mesh.VertexHandles )
		{
			if ( !IsVertexAllowed( vertex ) ) continue;

			var localPos = mesh.GetVertexPosition( vertex );
			var worldPos = mesh.Transform.PointToWorld( localPos );
			var distSq = (worldPos - hitWorldPos).LengthSquared;

			if ( distSq > radiusSq ) continue;

			if ( !PaintBackfacing )
			{
				var vertNormal = ComputeVertexNormal( mesh, vertex );
				if ( hitLocalNormal.Dot( vertNormal ) < 0f ) continue;
			}

			var t = MathF.Sqrt( distSq ) / Radius;
			var falloff = Hardness >= 1f ? 1f : (1f - ((t - Hardness) / (1f - Hardness)).Clamp( 0f, 1f ));

			var prevLocal = _prevPositions[vertex.Index];
			var currentLocal = prevLocal + _deltaPositions[vertex.Index];

			Vector3 newDelta;

			switch ( effectiveMode )
			{
				case DisplaceMode.Raise:
				case DisplaceMode.Lower:
					{
						var dir = effectiveMode == DisplaceMode.Raise ? 1f : -1f;
						newDelta = _deltaPositions[vertex.Index] + displaceNormal * (dir * DisplaceAmplitude * Strength * falloff);
						break;
					}

				case DisplaceMode.Smooth:
					{
						var smoothTarget = ComputeSmoothTarget( mesh, vertex, boundaryVerts );
						var smoothed = Vector3.Lerp( currentLocal, smoothTarget, Strength * falloff );
						newDelta = smoothed - prevLocal;
						break;
					}

				case DisplaceMode.Flatten:
					{
						var distToPlane = Vector3.Dot( currentLocal - _flattenPlanePoint, _flattenPlaneNormal );

						if ( FlattenSubMode == FlattenMode.RaiseOnly && distToPlane >= 0f ) continue;
						if ( FlattenSubMode == FlattenMode.LowerOnly && distToPlane <= 0f ) continue;

						var projected = currentLocal - _flattenPlaneNormal * distToPlane;
						var flattened = Vector3.Lerp( currentLocal, projected, Strength * falloff );
						newDelta = flattened - prevLocal;
						break;
					}

				case DisplaceMode.Pinch:
					{
						var toCenter = localHitPos - currentLocal;
						var dist = toCenter.Length;
						if ( dist < 0.001f ) continue;

						var pinchDir = Gizmo.IsCtrlPressed ? -1f : 1f;
						var movement = toCenter.Normal * (pinchDir * DisplaceAmplitude * Strength * falloff);

						if ( movement.Length > dist ) movement = toCenter;
						newDelta = _deltaPositions[vertex.Index] + movement;
						break;
					}

				case DisplaceMode.Move:
					{
						newDelta = _deltaPositions[vertex.Index] + localMoveDelta * falloff;
						break;
					}

				default: continue;
			}

			_deltaPositions[vertex.Index] = newDelta;
			mesh.SetVertexPosition( vertex, prevLocal + newDelta );
		}

	}

	Vector3 GetDisplacementNormal( PolygonMesh mesh, Vector3 hitWorldPos, Vector3 hitLocalNormal )
	{
		return NormalDir switch
		{
			NormalMode.Average => ComputeAverageNormalInRadius( mesh, hitWorldPos ),
			NormalMode.MouseDown => _strokeNormal,
			NormalMode.X => (mesh.Transform.Rotation.Inverse * Vector3.Right).Normal,
			NormalMode.Y => (mesh.Transform.Rotation.Inverse * Vector3.Forward).Normal,
			NormalMode.Z => (mesh.Transform.Rotation.Inverse * Vector3.Up).Normal,
			_ => hitLocalNormal,
		};
	}

	Vector3 ComputeAverageNormalInRadius( PolygonMesh mesh, Vector3 hitWorldPos )
	{
		var sum = Vector3.Zero;
		var radiusSq = Radius * Radius;

		foreach ( var vertex in mesh.VertexHandles )
		{
			var worldPos = mesh.Transform.PointToWorld( mesh.GetVertexPosition( vertex ) );
			if ( (worldPos - hitWorldPos).LengthSquared > radiusSq ) continue;

			sum += ComputeVertexNormal( mesh, vertex );
		}

		return sum.LengthSquared > 0.001f ? sum.Normal : Vector3.Up;
	}

	Vector3 ComputeVertexNormal( PolygonMesh mesh, VertexHandle vertex )
	{
		mesh.GetFacesConnectedToVertex( vertex, out var faces );

		var sum = Vector3.Zero;
		var count = 0;

		if ( faces != null )
		{
			foreach ( var face in faces )
			{
				mesh.ComputeFaceNormal( face, out var n );
				sum += n;
				count++;
			}
		}

		return count > 0 ? sum.Normal : Vector3.Up;
	}

	Vector3 ComputeSmoothTarget( PolygonMesh mesh, VertexHandle vertex, HashSet<int> boundaryVerts )
	{
		mesh.GetEdgesConnectedToVertex( vertex, out var edges );
		if ( edges == null ) return mesh.GetVertexPosition( vertex );

		var onBoundary = boundaryVerts?.Contains( vertex.Index ) ?? false;
		var sum = Vector3.Zero;
		var count = 0;

		foreach ( var edge in edges )
		{
			mesh.GetEdgeVertices( edge, out var a, out var b );
			var neighbor = a == vertex ? b : a;
			if ( !neighbor.IsValid ) continue;
			if ( onBoundary && !boundaryVerts.Contains( neighbor.Index ) ) continue;
			sum += mesh.GetVertexPosition( neighbor );
			count++;
		}

		return count > 0 ? sum / count : mesh.GetVertexPosition( vertex );
	}

	HashSet<int> ComputeBoundaryVertices( PolygonMesh mesh )
	{
		var edgeFaceCount = new Dictionary<long, int>();

		foreach ( var face in mesh.FaceHandles )
		{
			mesh.GetVerticesConnectedToFace( face, out var verts );
			if ( verts == null ) continue;
			for ( var i = 0; i < verts.Length; i++ )
			{
				var key = EdgeKey( verts[i], verts[(i + 1) % verts.Length] );
				edgeFaceCount[key] = edgeFaceCount.GetValueOrDefault( key ) + 1;
			}
		}

		var boundary = new HashSet<int>();

		foreach ( var face in mesh.FaceHandles )
		{
			mesh.GetVerticesConnectedToFace( face, out var verts );
			if ( verts == null ) continue;
			for ( var i = 0; i < verts.Length; i++ )
			{
				var a = verts[i];
				var b = verts[(i + 1) % verts.Length];
				if ( edgeFaceCount[EdgeKey( a, b )] != 1 ) continue;
				boundary.Add( a.Index );
				boundary.Add( b.Index );
			}
		}

		return boundary;
	}

	static long EdgeKey( VertexHandle a, VertexHandle b ) => ((long)Math.Min( a.Index, b.Index ) << 32) | (uint)Math.Max( a.Index, b.Index );

	void DrawBrush( Vector3 position, Vector3 normal, PolygonMesh mesh = null )
	{
		var brushColor = GetBrushColor();
		var sections = (int)(MathF.Sqrt( Radius ) * 5.0f).Clamp( 16, 64 );

		var scopeRot = Rotation.LookAt( normal );
		var dispNormal = _lastDisplaceNormal.LengthSquared > 0.001f ? _lastDisplaceNormal : normal;
		var arrowDir = scopeRot.Inverse * dispNormal;
		using ( Gizmo.Scope( "DisplacementBrush", position, scopeRot ) )
		{
			var length = MathX.LerpTo( 25f * 0.75f, 25f * 2f, Strength );

			Gizmo.Draw.IgnoreDepth = true;
			Gizmo.Draw.Color = brushColor;
			Gizmo.Draw.LineThickness = 4;
			Gizmo.Draw.Line( Vector3.Zero, arrowDir * length );
			Gizmo.Draw.SolidSphere( arrowDir * length, 2 );
			Gizmo.Draw.LineCircle( Vector3.Zero, Radius, 32, sections: sections );

			Gizmo.Draw.LineThickness = 1;
			Gizmo.Draw.Color = brushColor.WithAlpha( 0.4f );
			Gizmo.Draw.LineCircle( Vector3.Zero, Radius * Hardness, 32, sections: sections );
		}

		if ( ShowVerts && mesh is not null )
			DrawVertexIndicators( position, mesh );
	}

	Color GetBrushColor()
	{
		if ( Gizmo.IsShiftPressed )
			return Color.Cyan;

		var effectiveMode = Mode;
		if ( Gizmo.IsCtrlPressed && (Mode == DisplaceMode.Raise || Mode == DisplaceMode.Lower) )
			effectiveMode = Mode == DisplaceMode.Raise ? DisplaceMode.Lower : DisplaceMode.Raise;

		return effectiveMode switch
		{
			DisplaceMode.Raise => Color.Green,
			DisplaceMode.Lower => Color.Red,
			DisplaceMode.Smooth => Color.Cyan,
			DisplaceMode.Flatten => Color.Yellow,
			DisplaceMode.Pinch => Gizmo.IsCtrlPressed ? new Color( 0.4f, 0.8f, 1f ) : new Color( 0.7f, 0.3f, 1f ),
			DisplaceMode.Move => new Color( 1f, 0.6f, 0.1f ),
			_ => Color.White,
		};
	}

	void DrawBrushAdjustText()
	{
		var textScope = new TextRendering.Scope
		{
			TextColor = Color.White,
			FontSize = 16 * Gizmo.Settings.GizmoScale * Application.DpiScale,
			FontName = "Roboto Mono",
			FontWeight = 600,
			LineHeight = 1,
			Outline = new TextRendering.Outline() { Color = Color.Black, Enabled = true, Size = 3 }
		};

		var offset = Vector2.Up * 24;

		if ( Gizmo.IsShiftPressed )
		{
			textScope.Text = $"Radius: {Radius:0.#}";
			Gizmo.Draw.ScreenText( textScope, _lastHitPos, offset );
		}
		else if ( Gizmo.IsCtrlPressed )
		{
			textScope.Text = $"Strength: {Strength:0.##}";
			Gizmo.Draw.ScreenText( textScope, _lastHitPos, offset + Vector2.Up * 18 );

			textScope.Text = $"Hardness: {Hardness:0.##}";
			Gizmo.Draw.ScreenText( textScope, _lastHitPos, offset );
		}
	}

	void DrawVertexIndicators( Vector3 brushWorldPos, PolygonMesh mesh )
	{
		var indicatorRadius = Radius * 2f;
		var radiusSq = indicatorRadius * indicatorRadius;

		using ( Gizmo.Scope( "VertexIndicators" ) )
		{
			Gizmo.Draw.IgnoreDepth = true;

			foreach ( var vertex in mesh.VertexHandles )
			{
				if ( !IsVertexAllowed( vertex ) ) continue;

				var worldPos = mesh.Transform.PointToWorld( mesh.GetVertexPosition( vertex ) );
				if ( (worldPos - brushWorldPos).LengthSquared > radiusSq ) continue;

				Gizmo.Draw.Color = Color.White.WithAlpha( 0.7f );
				Gizmo.Draw.Sprite( worldPos, 6f, null, false );
			}
		}
	}
}
