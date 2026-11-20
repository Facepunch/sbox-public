/// <summary>
/// Manages a world-space snap grid overlay projected onto a surface plane.
/// </summary>
public sealed partial class SnapGrid
{
	public float CellSize { get; set; }
	public float MaskRadius { get; set; }

	/// <summary>
	/// Fallback half-extent used when the hovered object has no usable bounds.
	/// </summary>
	public float GridSize { get; set; }

	[ConVar( "sandbox.snapgrid.nofade", ConVarFlags.Cheat )]
	private static bool NoFade { get; set; } = false;

	public SnapGrid( float cellSize = 4f, float maskRadius = 64f, float gridSize = 48f )
	{
		CellSize = cellSize;
		MaskRadius = maskRadius;
		GridSize = gridSize;
	}

	private sealed class SnapGridSceneObject : SceneDynamicObject
	{
		public SnapGridSceneObject( SceneWorld world ) : base( world )
		{
			Transform = Transform.Zero;
			Flags.IsOpaque = false;
			Flags.IsTranslucent = true;
			Flags.CastShadows = false;
			RenderLayer = SceneRenderLayer.OverlayWithDepth;
		}

		public void WriteSurface( List<Vertex> vertices )
		{
			Bounds = BBox.FromPoints( vertices.Select( v => v.Position ) );
			Init( Graphics.PrimitiveType.Triangles );
			AddVertex( System.Runtime.InteropServices.CollectionsMarshal.AsSpan( vertices ) );
		}

		public void Write( Vector3 faceOrigin, Vector3 faceRight, Vector3 faceUp, Vector2 faceHalfExtents )
		{
			var halfW = faceHalfExtents.x;
			var halfH = faceHalfExtents.y;

			// The quad follows the surface bounds, independently of the grid's anchor.
			var quadCenter = faceOrigin;

			var v00 = quadCenter - faceRight * halfW - faceUp * halfH;
			var v10 = quadCenter + faceRight * halfW - faceUp * halfH;
			var v11 = quadCenter + faceRight * halfW + faceUp * halfH;
			var v01 = quadCenter - faceRight * halfW + faceUp * halfH;

			Bounds = BBox.FromPositionAndSize( quadCenter, MathF.Sqrt( halfW * halfW + halfH * halfH ) * 2f );

			Span<Vertex> verts = stackalloc Vertex[6]
			{
				new Vertex( v00 ),
				new Vertex( v11 ),
				new Vertex( v10 ),
				new Vertex( v00 ),
				new Vertex( v01 ),
				new Vertex( v11 ),
			};

			Init( Graphics.PrimitiveType.Triangles );
			AddVertex( verts );
		}
	}

	private SnapGridSceneObject _sceneObj;
	private Material _material;

	private Vector3 _cachedOrigin;
	private Vector3 _worldPatchCenter;
	private Vector3 _cachedNormal;
	private Vector3 _cachedRight;
	private Vector3 _cachedUp;
	private Vector2 _cachedHalfExtents;
	private GameObject _cachedObject;
	private bool _hasPlane;
	private Vertex[] _snapVertices;
	private uint[] _snapIndices;

	/// <summary>
	/// The snapped world-space position of the highlighted corner
	/// </summary>
	public Vector3 LastSnapWorldPos { get; private set; }
	public Vector3 LastSnapWorldNormal { get; private set; }
	public bool HasValidSnap { get; private set; }

	/// <summary>
	/// Returns the nearest grid corner index (cx, cy) and its world-space position,
	/// given the face plane in world space and the aim world position.
	/// </summary>
	public static (int cx, int cy, Vector3 snapPos) ComputeSnap(
		Vector3 faceOriginWs,
		Vector3 faceRightWs,
		Vector3 faceUpWs,
		float cellSize,
		Vector3 aimPosWs )
	{
		var offset = aimPosWs - faceOriginWs;
		var cx = (int)MathF.Round( Vector3.Dot( offset, faceRightWs ) / cellSize );
		var cy = (int)MathF.Round( Vector3.Dot( offset, faceUpWs ) / cellSize );

		var snapPos = faceOriginWs + faceRightWs * (cx * cellSize) + faceUpWs * (cy * cellSize);
		return (cx, cy, snapPos);
	}

	/// <summary>
	/// Updates the collision-based snap target and render-mesh overlay.
	/// After calling, read <see cref="LastSnapWorldPos"/> for a snapped position
	/// </summary>
	public void Update( SceneWorld world, GameObject hoveredObject, Vector3 aimWorldPos, Vector3 hitNormalWorld )
	{
		if ( !_sceneObj.IsValid() )
		{
			_material ??= Material.FromShader( "shaders/snap_grid.shader" );
			_sceneObj = new SnapGridSceneObject( world ) { Material = _material };
		}

		_sceneObj.RenderingEnabled = true;

		// Refresh the surface frame to follow moving props and stepped faces.
		var faceNormal = hitNormalWorld.Normal;
		var isWorld = hoveredObject.Tags.Has( "world" );
		if ( !isWorld )
		{
			var renderer = hoveredObject.GetComponentInChildren<ModelRenderer>( false );
			var rotation = renderer.IsValid() ? renderer.WorldRotation : hoveredObject.WorldRotation;
			var n = rotation.Inverse * faceNormal;
			var x = MathF.Abs( n.x ); var y = MathF.Abs( n.y ); var z = MathF.Abs( n.z );
			var axis = z >= x && z >= y ? new Vector3( 0, 0, MathF.Sign( n.z ) )
				: x >= y ? new Vector3( MathF.Sign( n.x ), 0, 0 ) : new Vector3( 0, MathF.Sign( n.y ), 0 );
			faceNormal = rotation * axis;
		}
		// Object planes are refreshed so translated, scaled and stepped surfaces stay aligned.

		if ( isWorld )
		{
			UpdateWorldPlane( hoveredObject, aimWorldPos, faceNormal );
		}
		else
		{
			_cachedNormal = faceNormal;
			_cachedObject = hoveredObject;

			var (obbCenter, obbHalfExtents, obbRotation, obbValid) = GetObjectOBB( hoveredObject );

			if ( obbValid )
			{
				// Align the grid so its "up" direction matches the object's world-space up
				// projected onto the face plane. This orients grid lines with the object's
				// natural rotation on every face, including tilted surfaces.
				var objUp = obbRotation * Vector3.Up;
				var dotUp = Vector3.Dot( objUp, faceNormal );

				if ( MathF.Abs( dotUp ) < 0.99f )
				{
					// Project the object's up onto the face plane and use it as the grid up.
					_cachedUp    = (objUp - faceNormal * dotUp).Normal;
					_cachedRight = Vector3.Cross( faceNormal, _cachedUp ).Normal;
				}
				else
				{
					// Degenerate: object's up is nearly parallel to the face normal
					// (e.g., hovering the top/bottom face of an upright object).
					// Fall back to the OBB axis most parallel to the face plane.
					var a0 = obbRotation * new Vector3( 1, 0, 0 );
					var a1 = obbRotation * new Vector3( 0, 1, 0 );
					var a2 = obbRotation * new Vector3( 0, 0, 1 );
					var d0 = MathF.Abs( Vector3.Dot( a0, faceNormal ) );
					var d1 = MathF.Abs( Vector3.Dot( a1, faceNormal ) );
					var d2 = MathF.Abs( Vector3.Dot( a2, faceNormal ) );
					var refAxis = d0 <= d1 && d0 <= d2 ? a0 : d1 <= d2 ? a1 : a2;
					_cachedRight = (refAxis - faceNormal * Vector3.Dot( refAxis, faceNormal )).Normal;
					_cachedUp    = Vector3.Cross( _cachedRight, faceNormal ).Normal;
				}

				_cachedOrigin = obbCenter;
				_cachedHalfExtents = ProjectedHalfExtents( obbHalfExtents, obbRotation, _cachedRight, _cachedUp );
			}
			else
			{
				// Fallback: generic tangent for world geometry.
				var refAxis = MathF.Abs( Vector3.Dot( faceNormal, Vector3.Up ) ) > 0.9f
					? Vector3.Forward
					: Vector3.Up;
				_cachedRight = Vector3.Cross( faceNormal, refAxis ).Normal;
				_cachedUp = Vector3.Cross( _cachedRight, faceNormal ).Normal;
				_cachedOrigin = aimWorldPos;
				_cachedHalfExtents = new Vector2( GridSize, GridSize );
			}

			_hasPlane = true;
		}

		var meshSurface = !isWorld && BuildCollisionSurface( hoveredObject, faceNormal );
		var planarSurface = meshSurface && TryPlanarSurface( aimWorldPos );
		var roundSurface = meshSurface && !planarSurface && TryRoundSurface( hoveredObject );
		if ( roundSurface ) faceNormal = _cachedNormal;
		if ( meshSurface && !planarSurface && !roundSurface ) SelectConnectedSurface( aimWorldPos );

		var halfExtents = _cachedHalfExtents;

		// Scale down cell size so at least one cell fits within the object's face.
		var cellSize = CellSize;
		var minHalf = MathF.Min( halfExtents.x, halfExtents.y );
		while ( minHalf < cellSize && cellSize > 0.1f )
			cellSize *= 0.5f;

		// Anchor prop grids to the fitted face edge rather than an arbitrary model centre.
		var gridOrigin = isWorld || roundSurface ? _cachedOrigin : _cachedOrigin - _cachedRight * halfExtents.x - _cachedUp * halfExtents.y;

		// Fit complete cells between the selected rectangular face boundaries.
		var spacing = new Vector2( cellSize, cellSize );
		if ( !isWorld && !roundSurface )
		{
			spacing.x = halfExtents.x * 2f / Math.Max( 1, (int)MathF.Round( halfExtents.x * 2f / cellSize ) );
			spacing.y = halfExtents.y * 2f / Math.Max( 1, (int)MathF.Round( halfExtents.y * 2f / cellSize ) );
		}
		// Compute the nearest snap line
		var (_, _, snapPos) = ComputeSnap( gridOrigin, _cachedRight, _cachedUp, cellSize, aimWorldPos );
		if ( !isWorld && !roundSurface )
		{
			var offset = aimWorldPos - gridOrigin;
			snapPos = gridOrigin
				+ _cachedRight * (MathF.Round( Vector3.Dot( offset, _cachedRight ) / spacing.x ) * spacing.x)
				+ _cachedUp * (MathF.Round( Vector3.Dot( offset, _cachedUp ) / spacing.y ) * spacing.y);
		}
		if ( roundSurface ) snapPos = ComputeRoundSnap( aimWorldPos, cellSize );
		HasValidSnap = true;
		LastSnapWorldNormal = faceNormal;
		if ( meshSurface )
		{
			snapPos = SnapToSurface( snapPos, aimWorldPos, faceNormal, gridOrigin, cellSize, spacing, roundSurface );
		}
		LastSnapWorldPos = snapPos;
		_sceneObj.RenderingEnabled = HasValidSnap;

		// Unfitted surfaces still need a face plane, not the object's centre plane.
		// Translate only in the normal direction so the lattice remains stable.
		if ( !isWorld && !planarSurface && !roundSurface )
		{
			var planeOffset = faceNormal * Vector3.Dot( snapPos - _cachedOrigin, faceNormal );
			_cachedOrigin += planeOffset;
			gridOrigin += planeOffset;
		}
		var boundsOrigin = isWorld ? _worldPatchCenter : _cachedOrigin;
		if ( meshSurface ) _sceneObj.WriteSurface( _surfaceVertices );
		else _sceneObj.Write( boundsOrigin, _cachedRight, _cachedUp, halfExtents );
		_sceneObj.Attributes.Set( "MeshSurface", meshSurface ? 1f : 0f );
		_sceneObj.Attributes.Set( "PlanarSurface", planarSurface ? 1f : 0f );
		_sceneObj.Attributes.Set( "RoundSurface", roundSurface ? 1f : 0f );
		_sceneObj.Attributes.Set( "RoundRadius", _roundRadius );
		_sceneObj.Attributes.Set( "AngleStep", _roundAngleStep );

		_sceneObj.Attributes.Set( "SnapPoint", snapPos );
		_sceneObj.Attributes.Set( "SnapNormal", LastSnapWorldNormal );
		_sceneObj.Attributes.Set( "GridOrigin", gridOrigin );
		_sceneObj.Attributes.Set( "BoundsOrigin", boundsOrigin );
		_sceneObj.Attributes.Set( "GridRight", _cachedRight );
		_sceneObj.Attributes.Set( "GridUp", _cachedUp );
		_sceneObj.Attributes.Set( "AimPoint", aimWorldPos );
		_sceneObj.Attributes.Set( "MaskRadius", NoFade ? float.MaxValue : MaskRadius );
		_sceneObj.Attributes.Set( "HalfExtents", halfExtents );
		_sceneObj.Attributes.Set( "CellSize", cellSize );
		_sceneObj.Attributes.Set( "CellSpacing", spacing );
		_sceneObj.Attributes.Set( "SnapCornerX", Vector3.Dot( snapPos - gridOrigin, _cachedRight ) / spacing.x );
		_sceneObj.Attributes.Set( "SnapCornerY", Vector3.Dot( snapPos - gridOrigin, _cachedUp ) / spacing.y );
	}

	private static bool InsideTriangle( Vector3 point, Vector3 a, Vector3 b, Vector3 c, Vector3 normal )
	{
		const float tolerance = 0.001f;
		return Vector3.Dot( Vector3.Cross( b - a, point - a ), normal ) >= -tolerance
			&& Vector3.Dot( Vector3.Cross( c - b, point - b ), normal ) >= -tolerance
			&& Vector3.Dot( Vector3.Cross( a - c, point - c ), normal ) >= -tolerance;
	}
	private void UpdateWorldPlane( GameObject hoveredObject, Vector3 aimPos, Vector3 normal )
	{
		var planeChanged = !_hasPlane || hoveredObject != _cachedObject ||
			Vector3.Dot( normal, _cachedNormal ) < 0.9999f ||
			MathF.Abs( Vector3.Dot( aimPos - _cachedOrigin, _cachedNormal ) ) > 0.01f;

		if ( planeChanged )
		{
			_cachedObject = hoveredObject;
			_cachedNormal = normal;
			var reference = MathF.Abs( Vector3.Dot( normal, Vector3.Up ) ) > 0.9f ? Vector3.Forward : Vector3.Up;
			_cachedRight = Vector3.Cross( normal, reference ).Normal;
			_cachedUp = Vector3.Cross( _cachedRight, normal ).Normal;
			// Project the world origin so returning to this plane uses the same lattice.
			_cachedOrigin = ProjectOntoPlane( Vector3.Zero, aimPos, normal );
			_worldPatchCenter = _cachedOrigin;
			_hasPlane = true;
		}

		// Leave enough margin for the cursor's fade mask before moving the patch.
		var halfSize = MathF.Ceiling( MathF.Max( GridSize, MaskRadius + CellSize ) * 2f / CellSize ) * CellSize;
		_cachedHalfExtents = new Vector2( halfSize, halfSize );
		var offset = aimPos - _worldPatchCenter;
		var u = Vector3.Dot( offset, _cachedRight );
		var v = Vector3.Dot( offset, _cachedUp );
		var threshold = halfSize - MaskRadius - CellSize;
		if ( planeChanged || MathF.Abs( u ) > threshold || MathF.Abs( v ) > threshold )
		{
			var anchorOffset = aimPos - _cachedOrigin;
			var cellU = MathF.Round( Vector3.Dot( anchorOffset, _cachedRight ) / CellSize );
			var cellV = MathF.Round( Vector3.Dot( anchorOffset, _cachedUp ) / CellSize );
			_worldPatchCenter = _cachedOrigin + _cachedRight * (cellU * CellSize) + _cachedUp * (cellV * CellSize);
		}
	}

	/// <summary>
	/// Hide the overlay
	/// </summary>
	public void Hide()
	{
		if ( _sceneObj != null && _sceneObj.IsValid() )
			_sceneObj.RenderingEnabled = false;
		_hasPlane = false;
	}

	// ---------------------------------------------------------------------------
	// Helpers

	/// <summary>
	/// Returns the OBB (oriented bounding box) of <paramref name="go"/> in world space.
	/// Prefers model-local bounds (tight, rotation-aware) over collider world AABB.
	/// </summary>
	private static (Vector3 Center, Vector3 HalfExtents, Rotation Rotation, bool Valid) GetObjectOBB( GameObject go )
	{
		var worldTx = go.WorldTransform;
		var rot = worldTx.Rotation;
		var scale = worldTx.Scale;

		// Try model-local bounds first — these are tight and unaffected by world rotation.
		BBox? localBox = null;
		var mr = go.GetComponentInChildren<ModelRenderer>( false );
		if ( mr != null && mr.Model != null )
			{
			localBox = mr.Model.PhysicsBounds;
			worldTx = mr.WorldTransform;
			rot = worldTx.Rotation;
			scale = worldTx.Scale;
		}

		if ( localBox == null )
		{
			var smr = go.GetComponentInChildren<SkinnedModelRenderer>( false );
			if ( smr != null && smr.Model != null )
				localBox = smr.Model.PhysicsBounds;
		}

		if ( localBox != null )
		{
			var lb = localBox.Value;
			if ( lb.Size.Length > 0.1f )
			{
				var center = worldTx.PointToWorld( lb.Center );
				var halfExtents = lb.Size * 0.5f * new Vector3( MathF.Abs( scale.x ), MathF.Abs( scale.y ), MathF.Abs( scale.z ) );
				return (center, halfExtents, rot, true);
			}
		}

		// Fallback: world AABB from colliders, treated as an identity-rotation OBB.
		var colliders = go.GetComponentsInChildren<Collider>( false, true ).ToArray();
		if ( colliders.Length > 0 )
		{
			var box = colliders[0].GetWorldBounds();
			for ( int i = 1; i < colliders.Length; i++ )
				box = box.AddBBox( colliders[i].GetWorldBounds() );

			if ( box.Size.Length > 0.1f )
				return (box.Center, box.Size * 0.5f, Rotation.Identity, true);
		}

		return (Vector3.Zero, Vector3.Zero, Rotation.Identity, false);
	}

	/// <summary>
	/// Projects <paramref name="point"/> onto the plane defined by <paramref name="planePoint"/> and <paramref name="normal"/>.
	/// </summary>
	private static Vector3 ProjectOntoPlane( Vector3 point, Vector3 planePoint, Vector3 normal )
	{
		return point - normal * Vector3.Dot( point - planePoint, normal );
	}

	/// <summary>
	/// Returns the half-extents of an OBB projected along <paramref name="right"/> and <paramref name="up"/>.
	/// Uses the OBB support function, which correctly handles rotated objects.
	/// </summary>
	private static Vector2 ProjectedHalfExtents( Vector3 obbHalfExtents, Rotation obbRotation, Vector3 right, Vector3 up )
	{
		// Explicitly rotate the three local unit axes into world space to avoid
		// ambiguity with named helpers (Right/Up/Forward vary by coordinate convention).
		var ax = obbRotation * new Vector3( 1, 0, 0 );
		var ay = obbRotation * new Vector3( 0, 1, 0 );
		var az = obbRotation * new Vector3( 0, 0, 1 );
		var e = obbHalfExtents;

		var halfRight = MathF.Abs( e.x * Vector3.Dot( ax, right ) )
					  + MathF.Abs( e.y * Vector3.Dot( ay, right ) )
					  + MathF.Abs( e.z * Vector3.Dot( az, right ) );

		var halfUp = MathF.Abs( e.x * Vector3.Dot( ax, up ) )
				   + MathF.Abs( e.y * Vector3.Dot( ay, up ) )
				   + MathF.Abs( e.z * Vector3.Dot( az, up ) );

		return new Vector2( halfRight, halfUp );
	}

	/// <summary>
	/// Destroys the underlying scene object.
	/// </summary>
	public void Destroy()
	{
		_sceneObj?.Delete();
		_sceneObj = null;
	}
}
