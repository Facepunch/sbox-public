namespace Editor.MeshEditor;

/// <summary>
/// Draw walls / a floor plan by clicking grid-snapped points. Click the first point again to close the loop.
/// </summary>
[Title( "Wall" ), Icon( "meshtools/primitve_tools/create.png" )]
public sealed class WallEditor( PrimitiveTool tool ) : PrimitiveEditor( tool )
{
	public enum WallMode
	{
		Solid,
		[Title( "Single Face" )] SingleFace,
	}

	public enum WallAlignment
	{
		Center,
		Inside,
		Outside,
	}

	public enum FaceSide
	{
		Outside,
		Inside,
	}

	readonly List<Vector3> _points = [];

	// Per segment (point i to point i + 1) bend offset from the segment midpoint. Zero is a straight segment.
	readonly List<Vector3> _bends = [];

	// Ground offset at each point. The base of the wall interpolates between them.
	readonly List<float> _offsets = [];
	bool _closed;
	Model _previewModel;
	Material _activeMaterial = tool.ActiveMaterial;

	public override bool CanBuild => _points.Count >= 2;
	public override bool InProgress => _points.Count > 0;

	public override PolygonMesh Build()
	{
		if ( !CanBuild ) return null;

		var material = Tool.ActiveMaterial;
		var mesh = new PolygonMesh();
		var closed = _closed && _points.Count >= 3;

		BuildGraph( closed, out var nodes, out var nodeHeights, out var edges );
		if ( edges.Count == 0 ) return null;

		Vector3 Up( int node ) => Vector3.Up * Math.Max( 0.1f, nodeHeights[node] );

		var vertexCache = new Dictionary<(int, int, int), HalfEdgeMesh.VertexHandle>();
		HalfEdgeMesh.VertexHandle V( Vector3 p )
		{
			var key = ((int)MathF.Round( p.x * 100 ), (int)MathF.Round( p.y * 100 ), (int)MathF.Round( p.z * 100 ));
			if ( !vertexCache.TryGetValue( key, out var h ) )
				vertexCache[key] = h = mesh.AddVertices( [p] )[0];
			return h;
		}

		void AddFace( params Vector3[] points )
		{
			var verts = points.Select( V ).Distinct().ToArray();
			if ( verts.Length < 3 ) return;
			var face = mesh.AddFace( verts );
			if ( face.IsValid ) mesh.SetFaceMaterial( face, material );
		}

		if ( Mode == WallMode.SingleFace )
		{
			// Right of travel direction is "outside" unless the loop winds clockwise.
			var faceRight = Side == FaceSide.Outside;
			if ( closed && SignedArea() < 0 ) faceRight = !faceRight;

			foreach ( var (a, b) in edges )
			{
				var pa = nodes[a];
				var pb = nodes[b];
				var ua = Up( a );
				var ub = Up( b );
				if ( faceRight ) AddFace( pa, pb, pb + ub, pa + ua );
				else AddFace( pb, pa, pa + ua, pb + ub );
			}
		}
		else
		{
			var thickness = Math.Max( 0.1f, Thickness );

			// Width on the right / left of each edge's drawn direction. Outside is to the right unless a closed loop winds clockwise.
			var outsideRight = !(closed && SignedArea() < 0);
			var edgeRight = Alignment switch
			{
				WallAlignment.Inside => outsideRight ? thickness : 0.0f,
				WallAlignment.Outside => outsideRight ? 0.0f : thickness,
				_ => thickness * 0.5f,
			};
			var edgeLeft = thickness - edgeRight;

			// Corners per edge end (index edge * 2 + (start ? 0 : 1)), left/right relative to the direction leaving that node.
			var left = new Vector3[edges.Count * 2];
			var right = new Vector3[edges.Count * 2];

			for ( int n = 0; n < nodes.Count; n++ )
			{
				var node = nodes[n];
				var up = Up( n );
				var incident = new List<(int End, Vector3 Dir, float LeftW, float RightW)>();
				for ( int e = 0; e < edges.Count; e++ )
				{
					if ( edges[e].A == n ) incident.Add( (e * 2, (nodes[edges[e].B] - node).WithZ( 0 ).Normal, edgeLeft, edgeRight) );
					if ( edges[e].B == n ) incident.Add( (e * 2 + 1, (nodes[edges[e].A] - node).WithZ( 0 ).Normal, edgeRight, edgeLeft) );
				}

				if ( incident.Count == 0 ) continue;
				incident.Sort( ( x, y ) => MathF.Atan2( x.Dir.y, x.Dir.x ).CompareTo( MathF.Atan2( y.Dir.y, y.Dir.x ) ) );

				if ( incident.Count == 1 )
				{
					var (end, dir, lw, rw) = incident[0];
					var r = dir.Cross( Vector3.Up ).Normal;
					left[end] = node - r * lw;
					right[end] = node + r * rw;

					// End cap
					AddFace( left[end], right[end], right[end] + up, left[end] + up );
					continue;
				}

				var hub = new List<Vector3>();
				for ( int k = 0; k < incident.Count; k++ )
				{
					var a = incident[k];
					var b = incident[(k + 1) % incident.Count];
					var leftA = -a.Dir.Cross( Vector3.Up ).Normal;
					var rightB = b.Dir.Cross( Vector3.Up ).Normal;

					var corner = IntersectLines( node + leftA * a.LeftW, a.Dir, node + rightB * b.RightW, b.Dir, out var hit )
						? hit
						: node + leftA * a.LeftW;

					var maxDist = thickness * 3.0f;
					if ( corner.Distance( node ) > maxDist )
						corner = node + (corner - node).Normal * maxDist;

					left[a.End] = corner;
					right[b.End] = corner;
					hub.Add( corner );
				}

				// Fill the gap in the middle of a junction
				if ( incident.Count >= 3 )
				{
					if ( Top ) AddFace( [.. hub.Select( x => x + up )] );
					if ( Bottom ) AddFace( [.. Enumerable.Reverse( hub )] );
				}
			}

			for ( int e = 0; e < edges.Count; e++ )
			{
				var ra = right[e * 2];
				var la = left[e * 2];
				var rb = left[e * 2 + 1];
				var lb = right[e * 2 + 1];

				var ua = Up( edges[e].A );
				var ub = Up( edges[e].B );

				AddFace( ra, rb, rb + ub, ra + ua );
				AddFace( lb, la, la + ua, lb + ub );
				if ( Top ) AddFace( ra + ua, rb + ub, lb + ub, la + ua );
				if ( Bottom ) AddFace( la, lb, rb, ra );
			}
		}

		mesh.TextureAlignToGrid( Transform.Zero );
		mesh.SetSmoothingAngle( 40.0f );
		return mesh;
	}

	const float GraphEpsilon = 0.05f;

	/// <summary>
	/// Turns the drawn path into a graph, splitting segments where they cross or touch so junctions (T, X, P shapes) connect cleanly.
	/// </summary>
	void BuildGraph( bool closed, out List<Vector3> nodes, out List<float> nodeHeights, out List<(int A, int B)> edges )
	{
		var nodeList = new List<Vector3>();
		var heightList = new List<float>();
		var edgeList = new List<(int A, int B)>();
		nodes = nodeList;
		nodeHeights = heightList;
		edges = edgeList;

		int NodeAt( Vector3 p, float h )
		{
			for ( int i = 0; i < nodeList.Count; i++ )
			{
				if ( nodeList[i].Distance( p ) >= GraphEpsilon ) continue;

				// Where walls of different heights meet, the junction takes the tallest
				heightList[i] = MathF.Max( heightList[i], h );
				return i;
			}

			nodeList.Add( p );
			heightList.Add( h );
			return nodeList.Count - 1;
		}

		var path = BuildCurvedPath( closed, out var pathHeights );
		var segments = new List<(Vector3 P0, Vector3 P1, float H0, float H1)>();
		for ( int i = 0; i < path.Count - 1; i++ )
			segments.Add( (path[i], path[i + 1], pathHeights[i], pathHeights[i + 1]) );
		if ( closed )
			segments.Add( (path[^1], path[0], pathHeights[^1], pathHeights[0]) );

		var seen = new HashSet<(int, int)>();

		for ( int s = 0; s < segments.Count; s++ )
		{
			var (p0, p1, h0, h1) = segments[s];
			var ts = new List<float> { 0.0f, 1.0f };

			for ( int o = 0; o < segments.Count; o++ )
			{
				if ( o == s ) continue;
				if ( SegmentIntersection( p0, p1, segments[o].P0, segments[o].P1, out var t ) )
					ts.Add( t );
			}

			foreach ( var q in path )
			{
				if ( PointOnSegment( q, p0, p1, out var t ) )
					ts.Add( t );
			}

			ts.Sort();

			var prev = NodeAt( p0, h0 );
			for ( int i = 1; i < ts.Count; i++ )
			{
				var next = NodeAt( Vector3.Lerp( p0, p1, ts[i] ), MathX.Lerp( h0, h1, ts[i] ) );
				if ( next == prev ) continue;

				if ( seen.Add( (Math.Min( prev, next ), Math.Max( prev, next )) ) )
					edgeList.Add( (prev, next) );

				prev = next;
			}
		}
	}

	/// <summary>
	/// Samples each segment as a quadratic curve passing through its bend handle.
	/// </summary>
	List<Vector3> BuildCurvedPath( bool closed ) => BuildCurvedPath( closed, out _ );

	/// <summary>
	/// The curved path at the clicked points, without ground offset.
	/// </summary>
	List<Vector3> BuildGroundPath( bool closed ) => BuildCurvedPath( closed, out _, false );

	List<Vector3> BuildCurvedPath( bool closed, out List<float> heights, bool withOffset = true )
	{
		var count = _points.Count;
		heights = [.. _points.Select( _ => Height )];
		if ( count < 2 )
			return [.. _points.Select( ( p, i ) => withOffset ? p + Vector3.Up * _offsets[i] : p )];

		var result = new List<Vector3> { _points[0] + Vector3.Up * (withOffset ? _offsets[0] : 0) };
		heights = [Height];
		var segments = closed ? count : count - 1;

		for ( int i = 0; i < segments; i++ )
		{
			var a = _points[i];
			var b = _points[(i + 1) % count];
			var bend = _bends[i];
			var control = (a + b) * 0.5f + bend * 2.0f;
			var steps = bend.LengthSquared > 0.0001f ? Math.Max( 2, CurveSegments ) : 1;

			for ( int s = 1; s <= steps; s++ )
			{
				// The graph adds the closing edge itself.
				if ( closed && i == segments - 1 && s == steps ) continue;
				var t = s / (float)steps;
				var u = 1.0f - t;
				var offset = withOffset ? MathX.Lerp( _offsets[i], _offsets[(i + 1) % count], t ) : 0;
				result.Add( (s == steps ? b : u * u * a + 2.0f * u * t * control + t * t * b) + Vector3.Up * offset );
				heights.Add( Height );
			}
		}

		return result;
	}

	static float Cross2( Vector3 a, Vector3 b ) => a.x * b.y - a.y * b.x;

	static bool SegmentIntersection( Vector3 p, Vector3 p1, Vector3 q, Vector3 q1, out float t )
	{
		t = 0;
		var r = (p1 - p).WithZ( 0 );
		var s = (q1 - q).WithZ( 0 );
		var denom = Cross2( r, s );
		if ( MathF.Abs( denom ) < 0.0001f ) return false;

		var qp = (q - p).WithZ( 0 );
		t = Cross2( qp, s ) / denom;
		var u = Cross2( qp, r ) / denom;
		return t > 0.0f && t < 1.0f && u >= 0.0f && u <= 1.0f;
	}

	static bool PointOnSegment( Vector3 q, Vector3 p0, Vector3 p1, out float t )
	{
		t = 0;
		var r = (p1 - p0).WithZ( 0 );
		var lenSq = r.LengthSquared;
		if ( lenSq < 0.0001f ) return false;

		t = (q - p0).WithZ( 0 ).Dot( r ) / lenSq;
		if ( t <= 0.0f || t >= 1.0f ) return false;

		return Vector3.Lerp( p0, p1, t ).Distance( q ) < GraphEpsilon;
	}

	static bool IntersectLines( Vector3 p1, Vector3 d1, Vector3 p2, Vector3 d2, out Vector3 hit )
	{
		hit = default;
		var denom = Cross2( d1, d2 );
		if ( MathF.Abs( denom ) < 0.0001f ) return false;

		var t = Cross2( (p2 - p1).WithZ( 0 ), d2 ) / denom;
		hit = p1 + d1 * t;
		return true;
	}

	float SignedArea()
	{
		float area = 0;
		for ( int i = 0; i < _points.Count; i++ )
		{
			var a = _points[i];
			var b = _points[(i + 1) % _points.Count];
			area += a.x * b.y - b.x * a.y;
		}
		return area * 0.5f;
	}

	public override void OnCreated( MeshComponent component )
	{
		OnCancel();
		SceneEditorSession.Active.Selection.Set( component.GameObject );
	}

	public override void OnUpdate( SceneTrace trace )
	{
		if ( _activeMaterial != Tool.ActiveMaterial )
		{
			_activeMaterial = Tool.ActiveMaterial;
			BuildPreview();
		}

		if ( UpdateOffsetDrag() ) { DrawGizmos( trace ); DrawOffsetText(); return; }

		if ( _placingIndex >= 0 )
			UpdatePlacingBend();
		else if ( !Gizmo.Pressed.Any && !Gizmo.HasHovered && Gizmo.WasLeftMousePressed && TryGetPoint( trace, out var point ) )
			AddPoint( point );

		DrawGizmos( trace );
	}

	Vector2? _offsetDragStart;
	float _offsetDragBase;
	Vector3 _offsetAnchor;

	void DrawOffsetText()
	{
		var text = new TextRendering.Scope
		{
			Text = $"Offset: {GroundOffset:0.##}",
			TextColor = Color.White,
			FontSize = 16 * Gizmo.Settings.GizmoScale * Application.DpiScale,
			FontName = "Roboto Mono",
			FontWeight = 600,
			LineHeight = 1,
			Outline = new TextRendering.Outline() { Color = Color.Black, Enabled = true, Size = 3 }
		};

		var raised = _offsetAnchor + Vector3.Up * GroundOffset;
		Gizmo.Draw.Color = Color.Cyan.WithAlpha( 0.8f );
		Gizmo.Draw.LineThickness = 1;
		DrawDashedLine( _offsetAnchor, raised );
		Gizmo.Draw.ScreenText( text, raised, Vector2.Up * 24 );
	}

	/// <summary>
	/// Shift + middle mouse drag up/down sets the ground offset.
	/// </summary>
	bool UpdateOffsetDrag()
	{
		var middleDown = Application.MouseButtons.HasFlag( MouseButtons.Middle );

		if ( _offsetDragStart is null )
		{
			if ( !middleDown || !Gizmo.IsShiftPressed ) return false;
			_offsetDragStart = Application.UnscaledCursorPosition;
			_offsetDragBase = GroundOffset;
			_offsetAnchor = _points.Count > 0 ? _points[^1] : Vector3.Zero;
			if ( new Plane( Vector3.Up, _offsetAnchor.z ).TryTrace( Gizmo.CurrentRay, out var hit, true ) )
				_offsetAnchor = hit;
		}

		if ( !middleDown )
		{
			_offsetDragStart = null;
			SceneOverlayWidget.Active?.Parent?.Cursor = CursorShape.None;
			return false;
		}

		var delta = _offsetDragStart.Value.y - Application.UnscaledCursorPosition.y;
		Application.UnscaledCursorPosition = _offsetDragStart.Value;
		SceneOverlayWidget.Active?.Parent?.Cursor = CursorShape.Blank;

		_offsetDragBase += delta * 0.5f;
		var value = _offsetDragBase;
		if ( Gizmo.Settings.SnapToGrid != Gizmo.IsCtrlPressed )
		{
			var spacing = Math.Max( 0.125f, Gizmo.Settings.GridSpacing );
			value = MathF.Round( value / spacing ) * spacing;
		}
		GroundOffset = value;
		return true;
	}

	int _placingIndex = -1;
	State _placingBefore;

	/// <summary>
	/// While the mouse is held after placing a point, dragging bends the segment leading into it.
	/// </summary>
	void UpdatePlacingBend()
	{
		var i = _placingIndex;
		if ( i <= 0 || i >= _points.Count )
		{
			FinishPlacing();
			return;
		}

		if ( Gizmo.IsLeftMouseDown )
		{
			var plane = new Plane( Vector3.Up, _points[i].z );
			if ( plane.TryTrace( Gizmo.CurrentRay, out var hit, true ) )
			{
				var offset = (SnapToGrid( hit ) - _points[i]).WithZ( 0 );
				if ( !_bends[i - 1].AlmostEqual( offset ) )
				{
					_bends[i - 1] = offset;
					BuildPreview();
				}
			}
			return;
		}

		FinishPlacing();
	}

	void FinishPlacing()
	{
		if ( _placingBefore is not null )
			PushUndo( "Add Wall Point", _placingBefore );

		_placingIndex = -1;
		_placingBefore = null;
	}

	public override void OnCancel()
	{
		PopUndo();
		_points.Clear();
		_bends.Clear();
		_offsets.Clear();
		ResetInteraction();
		_closed = false;
		_previewModel = null;
	}

	void ResetInteraction()
	{
		_placingIndex = -1;
		_placingBefore = null;
		_dragIndex = -1;
		_dragBefore = null;
		_dragMoved = false;
		_bendDragIndex = -1;
		_bendDragBefore = null;
	}

	void AddPoint( Vector3 point )
	{
		if ( _points.Count > 0 && _points[^1].AlmostEqual( point ) ) return;

		if ( _points.Count >= 3 && _points[0].AlmostEqual( point ) )
		{
			_closed = true;
			BuildPreview();
			Tool.Create();
			return;
		}

		var before = Snapshot();
		_points.Add( point );
		_bends.Add( Vector3.Zero );
		_offsets.Add( GroundOffset );
		BuildPreview();

		if ( _points.Count >= 2 )
		{
			_placingIndex = _points.Count - 1;
			_placingBefore = before;
		}
		else
		{
			PushUndo( "Add Wall Point", before );
		}
	}

	void RemovePoint()
	{
		if ( _points.Count == 0 || _placingIndex >= 0 || _dragIndex >= 0 || _bendDragIndex >= 0 ) return;

		var before = Snapshot();
		_points.RemoveAt( _points.Count - 1 );
		_bends.RemoveAt( _bends.Count - 1 );
		_offsets.RemoveAt( _offsets.Count - 1 );
		if ( _bends.Count > 0 ) _bends[^1] = Vector3.Zero;
		BuildPreview();
		PushUndo( "Remove Wall Point", before );
	}

	sealed record State( Vector3[] Points, Vector3[] Bends, float[] Offsets );

	State Snapshot() => new( [.. _points], [.. _bends], [.. _offsets] );

	void Restore( State state )
	{
		ResetInteraction();
		_points.Clear();
		_points.AddRange( state.Points );
		_bends.Clear();
		_bends.AddRange( state.Bends );
		_offsets.Clear();
		_offsets.AddRange( state.Offsets );
		BuildPreview();
	}

	void PushUndo( string name, State before )
	{
		var after = Snapshot();
		PushUndo( name, undo: () => Restore( before ), redo: () => Restore( after ) );
	}

	void BuildPreview()
	{
		_previewModel = Build()?.Rebuild();
	}

	static Vector3 SnapToGrid( Vector3 point )
	{
		// Holding Ctrl inverts the editor's grid snap setting
		if ( Gizmo.Settings.SnapToGrid == Gizmo.IsCtrlPressed )
			return point;

		var spacing = Math.Max( 0.125f, Gizmo.Settings.GridSpacing );
		return new Vector3(
			MathF.Round( point.x / spacing ) * spacing,
			MathF.Round( point.y / spacing ) * spacing,
			MathF.Round( point.z / spacing ) * spacing );
	}

	static bool TryGetPoint( SceneTrace trace, out Vector3 point )
	{
		var tr = trace.Run();
		if ( tr.Hit )
		{
			point = SnapToGrid( tr.EndPosition );
			return true;
		}

		var ground = new Plane( Vector3.Up, 0.0f );
		if ( ground.TryTrace( Gizmo.CurrentRay, out point, true ) )
		{
			point = SnapToGrid( point );
			return true;
		}

		point = default;
		return false;
	}

	void DrawGizmos( SceneTrace trace )
	{
		Gizmo.Draw.IgnoreDepth = true;
		Gizmo.Draw.LineThickness = 2;

		if ( _previewModel.IsValid() && !_previewModel.IsError )
		{
			Gizmo.Draw.Color = Color.White;
			Gizmo.Draw.Model( _previewModel );
		}

		Gizmo.Draw.Color = Color.Yellow;
		var guide = BuildCurvedPath( _closed && _points.Count >= 3 );
		for ( int i = 0; i < guide.Count - 1; i++ )
			Gizmo.Draw.Line( guide[i], guide[i + 1] );

		if ( _offsets.Any( x => MathF.Abs( x ) > 0.01f ) )
		{
			Gizmo.Draw.LineThickness = 1;
			Gizmo.Draw.Color = Color.Cyan.WithAlpha( 0.8f );
			for ( int i = 0; i < _points.Count; i++ )
				DrawDashedLine( _points[i], _points[i] + Vector3.Up * _offsets[i] );

			Gizmo.Draw.Color = Color.Cyan.WithAlpha( 0.4f );
			var ground = BuildGroundPath( _closed && _points.Count >= 3 );
			for ( int i = 0; i < ground.Count - 1; i++ )
				DrawDashedLine( ground[i], ground[i + 1] );
			Gizmo.Draw.LineThickness = 2;
		}

		for ( int i = 0; i < _points.Count - 1; i++ )
			DrawBendHandle( i );

		for ( int i = 0; i < _points.Count; i++ )
			DrawPointHandle( i );

		if ( _placingIndex >= 0 || Gizmo.HasHovered || !TryGetPoint( trace, out var preview ) ) return;

		var closing = _points.Count >= 3 && _points[0].AlmostEqual( preview );
		Gizmo.Draw.Color = closing ? Color.Green : Color.White;
		Gizmo.Draw.SolidSphere( preview, 4.0f * Gizmo.Camera.Position.Distance( preview ) / 1000.0f );
		var baseline = preview + Vector3.Up * GroundOffset;
		if ( GroundOffset != 0 ) DrawDashedLine( preview, baseline );
		Gizmo.Draw.Line( baseline, baseline + Vector3.Up * Height );

		if ( _points.Count > 0 )
		{
			Gizmo.Draw.Color = Color.Yellow;
			Gizmo.Draw.Line( _points[^1] + Vector3.Up * _offsets[^1], baseline );
		}
	}

	static void DrawDashedLine( Vector3 a, Vector3 b )
	{
		var length = a.Distance( b );
		if ( length < 0.01f ) return;

		var dash = Math.Max( 1f, Gizmo.Camera.Position.Distance( a ) / 150f );
		var dir = (b - a) / length;
		for ( float d = 0; d < length; d += dash * 2 )
			Gizmo.Draw.Line( a + dir * d, a + dir * MathF.Min( d + dash, length ) );
	}

	int _dragIndex = -1;
	bool _dragMoved;
	State _dragBefore;

	int _bendDragIndex = -1;
	State _bendDragBefore;

	/// <summary>
	/// Handle at the middle of each segment. Drag it to curve the segment, drag it back to the middle to straighten.
	/// </summary>
	void DrawBendHandle( int i )
	{
		if ( _placingIndex >= 0 ) return;
		var a = _points[i];
		var b = _points[i + 1];
		var mid = (a + b) * 0.5f;
		var handle = mid + _bends[i];
		var size = 2.5f * Gizmo.Camera.Position.Distance( handle ) / 1000.0f;

		using ( Gizmo.Scope( $"wall bend {i}" ) )
		{
			Gizmo.Hitbox.DepthBias = 0.01f;
			Gizmo.Hitbox.Sphere( new Sphere( handle, size * 2 ) );

			if ( Gizmo.Pressed.This )
			{
				if ( _bendDragIndex != i )
				{
					_bendDragIndex = i;
					_bendDragBefore = Snapshot();
				}

				var plane = new Plane( Vector3.Up, mid.z );
				if ( plane.TryTrace( Gizmo.CurrentRay, out var hit, true ) )
				{
					var offset = hit.Distance( mid ) < size * 3 ? Vector3.Zero : (SnapToGrid( hit ) - mid).WithZ( 0 );
					if ( !_bends[i].AlmostEqual( offset ) )
					{
						_bends[i] = offset;
						BuildPreview();
					}
				}
			}
			else if ( _bendDragIndex == i )
			{
				var before = _bendDragBefore;
				_bendDragIndex = -1;
				_bendDragBefore = null;

				if ( !before.Bends.SequenceEqual( _bends ) )
					PushUndo( "Bend Wall Segment", before );
			}

			var hovered = Gizmo.IsHovered || _bendDragIndex == i;
			Gizmo.Draw.Color = hovered ? Color.Yellow : Color.Cyan;
			Gizmo.Draw.SolidSphere( handle, hovered ? size * 2 : size );
		}
	}

	void DrawPointHandle( int i )
	{
		if ( _placingIndex >= 0 ) return;
		var point = _points[i];
		var size = 3.0f * Gizmo.Camera.Position.Distance( point ) / 1000.0f;

		using ( Gizmo.Scope( $"wall point {i}" ) )
		{
			Gizmo.Hitbox.DepthBias = 0.01f;
			Gizmo.Hitbox.Sphere( new Sphere( point, size * 2 ) );

			if ( Gizmo.Pressed.This )
			{
				if ( _dragIndex != i )
				{
					_dragIndex = i;
					_dragMoved = false;
					_dragBefore = Snapshot();
				}

				// Drag across the horizontal plane the point sits on
				var plane = new Plane( Vector3.Up, _dragBefore.Points[i].z );
				if ( plane.TryTrace( Gizmo.CurrentRay, out var hit, true ) )
				{
					hit = SnapToGrid( hit ).WithZ( _dragBefore.Points[i].z );
					if ( !_points[i].AlmostEqual( hit ) )
					{
						_points[i] = hit;
						_dragMoved = true;
						BuildPreview();
					}
				}
			}
			else if ( _dragIndex == i )
			{
				var before = _dragBefore;
				var moved = _dragMoved && !before.Points.SequenceEqual( _points );
				_dragIndex = -1;
				_dragBefore = null;

				if ( moved )
					PushUndo( "Move Wall Point", before );
				else if ( !_dragMoved && i == 0 && _points.Count >= 3 )
				{
					// Clicking the first point closes the loop
					_closed = true;
					BuildPreview();
					Tool.Create();
					return;
				}
			}

			var hovered = Gizmo.IsHovered || _dragIndex == i;
			Gizmo.Draw.Color = hovered ? (i == 0 && _points.Count >= 3 ? Color.Green : Color.Yellow) : Color.White;
			Gizmo.Draw.SolidSphere( point, hovered ? size * 2 : size );
		}
	}

	public override Widget CreateWidget() => new WallEditorWidget( this );

	[Title( "Mode" ), WideMode, Description( "Solid wall with thickness, or a single face." )]
	public WallMode Mode
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			BuildPreview();
		}
	} = WallMode.Solid;

	[Title( "Alignment" ), WideMode, Description( "Where the solid wall sits relative to the drawn line: centered on it, or with the line on its inside or outside face." )]
	public WallAlignment Alignment
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			BuildPreview();
		}
	} = WallAlignment.Center;

	[Title( "Face Side" ), HideIf( nameof( Mode ), WallMode.Solid ), WideMode]
	public FaceSide Side
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			BuildPreview();
		}
	} = FaceSide.Outside;

	[Range( 1f, 4096f, slider: false ), Step( 1f ), Title( "Height" ), WideMode, Description( "Height of the wall." )]
	public float Height
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			BuildPreview();
		}
	} = 128f;

	[Range( 0.5f, 512f, slider: false ), Step( 0.5f ), WideMode, Description( "Wall thickness (solid mode)." )]
	public float Thickness
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			BuildPreview();
		}
	} = 8f;

	[Range( 1, 64, slider: false ), Step( 1 ), WideMode, Description( "How many segments are used for each curved wall section." )]
	public int CurveSegments
	{
		get;
		set
		{
			var clamped = Math.Clamp( value, 1, 64 );
			if ( field == clamped ) return;
			field = clamped;
			BuildPreview();
		}
	} = 12;

	[Title( "Ground Offset" ), Range( -4096f, 4096f, slider: false ), Step( 1f ), WideMode, Description( "Raise new points above where they are clicked. Change it between clicks to build ramps and steps. Shift + middle drag in the viewport to adjust." )]
	public float GroundOffset
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
		}
	}

	[Description( "Add a top face (solid mode)." )]
	public bool Top
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			BuildPreview();
		}
	} = true;

	[Description( "Add a bottom face (solid mode)." )]
	public bool Bottom
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			BuildPreview();
		}
	} = true;

	class WallEditorWidget : ToolSidebarWidget
	{
		readonly WallEditor _editor;

		public WallEditorWidget( WallEditor editor )
		{
			_editor = editor;
			Layout.Margin = 0;

			var group = AddGroup( "Wall Properties" );
			var sheet = new ControlSheet();
			sheet.AddProperty( editor, x => x.Mode );
			sheet.AddProperty( editor, x => x.Alignment );
			sheet.AddProperty( editor, x => x.Side );
			sheet.AddProperty( editor, x => x.Height );
			sheet.AddProperty( editor, x => x.Thickness );
			sheet.AddProperty( editor, x => x.GroundOffset );
			sheet.AddProperty( editor, x => x.CurveSegments );
			sheet.AddProperty( editor, x => x.Top );
			sheet.AddProperty( editor, x => x.Bottom );
			group.Add( sheet );

			AddShortcuts(
				("Add Point", "Left Click"),
				("Curve Segment", "Left Drag"),
				("Toggle Snap", "Ctrl"),
				("Ground Offset", "Shift + Middle Drag"),
				("Remove Last Point", "DEL") );
		}

		[Shortcut( "editor.delete", "DEL", typeof( SceneViewWidget ) )]
		public void DeletePoint() => _editor.RemovePoint();
	}
}
