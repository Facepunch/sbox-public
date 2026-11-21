/// <summary>
/// The plane a building block is drawn on. <see cref="Right"/>, <see cref="Forward"/> and <see cref="Normal"/>
/// are a right-handed frame and become the block's local X, Y and Z. On a prop face, <see cref="FaceMin"/> and
/// <see cref="FaceMax"/> are the face's edges in plane coordinates, which points snap to as well as the grid.
/// </summary>
public readonly record struct BuilderPlane( Vector3 Origin, Vector3 Right, Vector3 Forward, Vector3 Normal, bool HasFace = false, Vector2 FaceMin = default, Vector2 FaceMax = default )
{
	/// <summary>A world point in grid coordinates on this plane.</summary>
	public Vector2 ToPlane( Vector3 point )
	{
		var offset = point - Origin;
		return new Vector2( Vector3.Dot( offset, Right ), Vector3.Dot( offset, Forward ) );
	}

	public Vector3 ToWorld( Vector2 point ) => Origin + Right * point.x + Forward * point.y;

	/// <summary>Whether a plane point lies on the face, give or take <paramref name="margin"/>. Always true without a face.</summary>
	public bool Contains( Vector2 point, float margin ) => !HasFace
		|| (point.x >= FaceMin.x - margin && point.x <= FaceMax.x + margin && point.y >= FaceMin.y - margin && point.y <= FaceMax.y + margin);

	/// <summary>
	/// The nearest grid point, or face edge when that's closer, so a block's corners can always be picked
	/// even when its size isn't a multiple of the grid.
	/// </summary>
	public Vector2 Snap( Vector2 point, float grid ) => new( SnapAxis( point.x, grid, FaceMin.x, FaceMax.x ), SnapAxis( point.y, grid, FaceMin.y, FaceMax.y ) );

	float SnapAxis( float value, float grid, float min, float max )
	{
		var snapped = BuilderPlacement.Snap( value, grid );
		if ( !HasFace ) return snapped;

		var middle = (min + max) * 0.5f;
		if ( MathF.Abs( value - min ) < MathF.Abs( value - snapped ) ) snapped = min;
		if ( MathF.Abs( value - max ) < MathF.Abs( value - snapped ) ) snapped = max;
		if ( MathF.Abs( value - middle ) < MathF.Abs( value - snapped ) ) snapped = middle;
		return snapped;
	}
}

/// <summary>
/// A flat face of a prop: a side of its box, or one of a building block's recorded faces. See <see cref="BuilderPlacement.TryGetFacePlane"/>.
/// </summary>
public readonly record struct BuilderFaceRef( GameObject Object, int Id, BuilderShapeKey? Key = null );

/// <summary>
/// Placement math for the Builder tool: the plane under the first click, the footprint corners on it and the height off it.
/// </summary>
public static class BuilderPlacement
{
	/// <summary>How far a corner can be from the eyes, same as the toolgun's reach.</summary>
	public const float Reach = 4096f;

	public const int MinGrid = 4;
	public const int MaxGrid = 2048;
	public const int GridStep = 4;

	const float AxisAlignedDot = 0.985f;

	const float EdgeTolerance = 4f;

	const float FlatTolerance = 0.5f;

	public static bool IsFinite( Vector3 v ) => !v.IsNaN && !v.IsInfinity;

	public static int ClampGrid( int grid ) => Math.Clamp( grid / GridStep * GridStep, MinGrid, MaxGrid );

	public static float Snap( float value, float grid ) => grid * MathF.Round( value / grid );

	/// <summary>
	/// The corner of a footprint from <paramref name="start"/> towards <paramref name="aim"/> whose sides keep the
	/// <paramref name="ratio"/> (plane right : forward). The shorter side is a whole number of grid cells (pass 0 for off
	/// grid), so it stays on the grid even when it starts on a face edge.
	/// </summary>
	public static Vector2 Proportional( Vector2 start, Vector2 aim, Vector2 ratio, float grid )
	{
		ratio = new Vector2( ratio.x > 0 ? ratio.x : 1, ratio.y > 0 ? ratio.y : 1 );

		var delta = aim - start;
		var scale = MathF.Max( MathF.Abs( delta.x ) / ratio.x, MathF.Abs( delta.y ) / ratio.y );
		var shortest = MathF.Min( ratio.x, ratio.y );
		if ( grid > 0 ) scale = MathF.Max( grid, Snap( scale * shortest, grid ) ) / shortest;

		var side = ratio * scale;
		return start + new Vector2( delta.x < 0 ? -side.x : side.x, delta.y < 0 ? -side.y : side.y );
	}

	/// <summary>
	/// The plane under a clicked point. World surfaces use a lattice anchored at the world origin. Prop faces use the
	/// prop's own axes and a lattice anchored at its bounds corner, so blocks sized in grid steps stay on the grid.
	/// <paramref name="face"/> is the prop face it's on, if any, so a tool can keep using it while the aim wanders off its geometry.
	/// </summary>
	public static bool TryGetPlane( ToolMode.SelectionPoint hit, Vector3 view, out BuilderPlane plane, out Vector2 point, out BuilderFaceRef? face )
	{
		plane = default;
		point = default;
		face = null;
		if ( !hit.IsValid() ) return false;

		var tx = hit.WorldTransform();
		var position = tx.Position;
		var normal = tx.Rotation.Forward;
		if ( !IsFinite( position ) || !IsFinite( normal ) || normal.IsNearZeroLength ) return false;

		if ( !hit.IsWorld && FindFace( hit.GameObject, position, normal.Normal, view ) is { } id && TryGetFacePlane( new BuilderFaceRef( hit.GameObject, id, hit.GameObject.GetComponentInParent<BuilderProp>()?.BuiltKey ), out plane ) )
		{
			face = new BuilderFaceRef( hit.GameObject, id, hit.GameObject.GetComponentInParent<BuilderProp>()?.BuiltKey );
			point = plane.ToPlane( position );
			return true;
		}

		normal = normal.Normal;
		Vector3 right, forward;
		if ( MathF.Abs( normal.z ) > 0.7f )
		{
			right = (Vector3.Forward - normal * Vector3.Dot( Vector3.Forward, normal )).Normal;
			forward = Vector3.Cross( normal, right );
		}
		else
		{
			forward = (Vector3.Up - normal * Vector3.Dot( Vector3.Up, normal )).Normal;
			right = Vector3.Cross( forward, normal );
		}

		plane = new BuilderPlane( normal * Vector3.Dot( position, normal ), right, forward, normal );
		point = plane.ToPlane( position );
		return true;
	}

	readonly record struct FaceShape( Vector3 Corner, Vector3 EdgeA, Vector3 EdgeB, Vector3 Normal );

	readonly struct FaceSource
	{
		public readonly bool Valid;
		public readonly Transform Transform;
		public readonly BBox Bounds;
		public readonly BuilderFaces FlatSides;
		public readonly BuilderFace[] Faces;
		public readonly BuilderShapeKey? Key;

		public FaceSource( GameObject go )
		{
			this = default;

			var renderer = go.GetComponentInChildren<ModelRenderer>();
			if ( !renderer.IsValid() || renderer.Model is not { IsError: false } model ) return;

			Valid = true;
			Transform = renderer.WorldTransform;
			Bounds = model.Bounds;
			FlatSides = BuilderFaces.All;

			if ( go.GetComponentInParent<BuilderProp>()?.BuiltKey is { } key )
			{
				Key = key;
				FlatSides = BuilderShapes.Get( key.Shape )?.GetFlatFaces( key ) ?? BuilderFaces.All;
				if ( BuilderModelCache.TryGetFaces( key, out var faces ) ) Faces = faces;
			}
		}

		public int Count => BoxSides + (Faces?.Length ?? 0);
	}

	const int BoxSides = 6;

	static int? FindFace( GameObject go, Vector3 position, Vector3 normal, Vector3 view )
	{
		var source = new FaceSource( go );
		if ( !source.Valid ) return null;

		int? onFace = null;
		var onDistance = float.MaxValue;

		for ( int id = 0; id < source.Count; id++ )
		{
			if ( !TryGetFaceShape( source, id, out var face ) ) continue;
			if ( Vector3.Dot( face.Normal, normal ) < AxisAlignedDot ) continue;

			var distance = MathF.Abs( Vector3.Dot( position - face.Corner, face.Normal ) );
			if ( distance > FlatTolerance || distance >= onDistance || !Contains( face, position, FlatTolerance ) ) continue;

			onFace = id;
			onDistance = distance;
		}

		if ( onFace is not { } hitId || !TryGetFaceShape( source, hitId, out var hitFace ) ) return null;

		var best = hitId;
		var bestUp = hitFace.Normal.z;
		for ( int id = 0; id < source.Count; id++ )
		{
			if ( id == hitId || !TryGetFaceShape( source, id, out var face ) ) continue;
			if ( Vector3.Dot( face.Normal, view ) >= 0 || face.Normal.z <= bestUp + 0.01f ) continue;
			if ( MathF.Abs( Vector3.Dot( position - face.Corner, face.Normal ) ) > EdgeTolerance || !Contains( face, position, EdgeTolerance ) ) continue;

			best = id;
			bestUp = face.Normal.z;
		}

		return best;
	}

	/// <summary>The plane of a prop face, with the face's corners, edge middles and centre to snap to.</summary>
	public static bool TryGetFacePlane( BuilderFaceRef face, out BuilderPlane plane )
	{
		plane = default;
		if ( !face.Object.IsValid() ) return false;

		var source = new FaceSource( face.Object );
		if ( !source.Valid || face.Key != source.Key || !TryGetFaceShape( source, face.Id, out var shape ) ) return false;

		var (right, forward) = FaceAxes( shape.Normal, shape.EdgeA.Normal, shape.EdgeB.Normal );
		var unbounded = new BuilderPlane( shape.Corner, right, forward, shape.Normal );

		var a = unbounded.ToPlane( shape.Corner );
		var b = unbounded.ToPlane( shape.Corner + shape.EdgeA );
		var c = unbounded.ToPlane( shape.Corner + shape.EdgeA + shape.EdgeB );
		var d = unbounded.ToPlane( shape.Corner + shape.EdgeB );

		plane = unbounded with
		{
			HasFace = true,
			FaceMin = a.ComponentMin( b ).ComponentMin( c ).ComponentMin( d ),
			FaceMax = a.ComponentMax( b ).ComponentMax( c ).ComponentMax( d )
		};
		return true;
	}

	static bool Contains( in FaceShape face, Vector3 point, float margin )
	{
		var offset = point - face.Corner;
		var lengthA = face.EdgeA.Length;
		var lengthB = face.EdgeB.Length;
		if ( lengthA < 0.001f || lengthB < 0.001f ) return false;

		var u = Vector3.Dot( offset, face.EdgeA / lengthA );
		var v = Vector3.Dot( offset, face.EdgeB / lengthB );
		return u >= -margin && u <= lengthA + margin && v >= -margin && v <= lengthB + margin;
	}

	static bool TryGetFaceShape( in FaceSource source, int id, out FaceShape face )
	{
		face = default;
		var transform = source.Transform;

		if ( id >= BoxSides )
		{
			if ( source.Faces is not { } faces || id - BoxSides >= faces.Length ) return false;

			var recorded = faces[id - BoxSides];
			var corner = transform.PointToWorld( recorded.A );
			var edgeA = transform.PointToWorld( recorded.B ) - corner;
			var edgeB = transform.PointToWorld( recorded.D ) - corner;
			var normal = Vector3.Cross( edgeA, edgeB );
			if ( normal.IsNearZeroLength ) return false;

			if ( transform.Scale.x * transform.Scale.y * transform.Scale.z < 0 ) normal = -normal;

			face = new FaceShape( corner, edgeA, edgeB, normal.Normal );
			return true;
		}

		var axis = id / 2;
		var localSign = id % 2 == 0 ? 1 : -1;
		if ( (source.FlatSides & BuilderShapes.Face( axis, localSign )) == 0 ) return false;

		var bounds = source.Bounds;
		var cornerLocal = bounds.Mins;
		cornerLocal[axis] = localSign > 0 ? bounds.Maxs[axis] : bounds.Mins[axis];
		var endA = cornerLocal;
		endA[(axis + 1) % 3] = bounds.Maxs[(axis + 1) % 3];
		var endB = cornerLocal;
		endB[(axis + 2) % 3] = bounds.Maxs[(axis + 2) % 3];

		var origin = transform.PointToWorld( cornerLocal );
		var sideA = transform.PointToWorld( endA ) - origin;
		var sideB = transform.PointToWorld( endB ) - origin;

		var axisLocal = default( Vector3 );
		axisLocal[axis] = localSign * MathF.Sign( transform.Scale[axis] );
		var world = transform.Rotation * axisLocal;

		face = new FaceShape( origin, sideA, sideB, world.Normal );
		return true;
	}

	static (Vector3 Right, Vector3 Forward) FaceAxes( Vector3 normal, Vector3 a, Vector3 b )
	{
		var floor = MathF.Abs( normal.z ) > 0.7f;
		var reference = floor ? Vector3.Forward : Vector3.Up;

		Span<Vector3> candidates = stackalloc Vector3[4] { a, -a, b, -b };
		var best = candidates[0];
		for ( int i = 1; i < 4; i++ )
		{
			if ( Vector3.Dot( candidates[i], reference ) > Vector3.Dot( best, reference ) + 0.001f )
				best = candidates[i];
		}

		return floor
			? (best, Vector3.Cross( normal, best ))
			: (Vector3.Cross( best, normal ), best);
	}

	/// <summary>Where a ray crosses the plane, in plane coordinates. False if it's parallel, behind, or out of reach.</summary>
	public static bool TryProject( in BuilderPlane plane, Ray ray, out Vector2 point )
	{
		point = default;

		var denominator = Vector3.Dot( ray.Forward, plane.Normal );
		if ( MathF.Abs( denominator ) < 0.0001f ) return false;

		var distance = Vector3.Dot( plane.Origin - ray.Position, plane.Normal ) / denominator;
		if ( distance < 0 || distance > Reach ) return false;

		point = plane.ToPlane( ray.Position + ray.Forward * distance );
		return true;
	}

	/// <summary>
	/// How far along <paramref name="normal"/> from <paramref name="origin"/> the aim ray passes closest, which is the
	/// height the player is pointing at. False when looking straight along the normal, or the point is behind or out of reach.
	/// </summary>
	public static bool TryGetHeight( Vector3 origin, Vector3 normal, Ray ray, out float height )
	{
		height = 0;

		var along = Vector3.Dot( normal, ray.Forward );
		var denominator = 1f - along * along;
		if ( denominator < 0.001f ) return false;

		var toOrigin = origin - ray.Position;
		var d = Vector3.Dot( normal, toOrigin );
		var e = Vector3.Dot( ray.Forward, toOrigin );

		var distance = (e - along * d) / denominator;
		if ( distance < 0 || distance > Reach ) return false;

		height = (along * e - d) / denominator;
		return float.IsFinite( height );
	}

	/// <summary>
	/// The block spanning corners <paramref name="a"/> and <paramref name="b"/>, rising <paramref name="height"/> off the
	/// plane (negative goes below it), with its shape turned by <paramref name="orientation"/>. <paramref name="size"/> is
	/// in the shape's own axes.
	/// </summary>
	public static bool TryGetBlock( in BuilderPlane plane, Vector2 a, Vector2 b, float height, BuilderOrientation orientation, out Transform transform, out Vector3 size )
	{
		transform = default;
		size = default;

		var min = a.ComponentMin( b );
		var max = a.ComponentMax( b );
		var footprint = max - min;
		var thickness = MathF.Abs( height );
		if ( footprint.x < BuilderShapeKey.MinSize || footprint.y < BuilderShapeKey.MinSize || thickness < BuilderShapeKey.MinSize ) return false;

		var center = plane.ToWorld( (min + max) * 0.5f ) + plane.Normal * (height * 0.5f);
		size = orientation.ToShape( footprint.x, footprint.y, thickness );
		transform = new Transform( center, orientation.Rotation( plane ) );
		return true;
	}
}

/// <summary>
/// How a shape sits on its plane. It starts with its <see cref="Up"/> axis along the surface normal, then
/// <see cref="Twist"/> (a quarter-turn rotation in the plane frame: X right, Y forward, Z normal) turns it from there.
/// Each turn or tip is applied in the plane frame, so a turn is always about the surface normal and a tip always rolls
/// the shape over the same way, however it's already been turned. Together they reach every axis-aligned orientation.
/// </summary>
public readonly record struct BuilderOrientation( BuilderUpAxis Up, Rotation Twist )
{
	/// <summary>A quarter turn about the surface normal.</summary>
	public static Rotation Turn( Rotation twist ) => Snap( global::Rotation.FromAxis( Vector3.Up, 90f ) * twist );

	/// <summary>
	/// A quarter tip that rolls the top of the shape away from someone looking along <paramref name="view"/>
	/// (in plane coordinates), about whichever in-plane axis is closest to their right.
	/// </summary>
	public static Rotation Tip( Rotation twist, Vector3 view )
	{
		var axis = new Vector3( -view.y, view.x, 0 );
		axis = MathF.Abs( axis.x ) >= MathF.Abs( axis.y ) ? new Vector3( MathF.Sign( axis.x ), 0, 0 ) : new Vector3( 0, MathF.Sign( axis.y ), 0 );
		if ( axis.IsNearZeroLength ) axis = Vector3.Forward;

		return Snap( global::Rotation.FromAxis( axis, 90f ) * twist );
	}

	static Rotation Snap( Rotation rotation )
	{
		static Vector3 Round( Vector3 v ) => new( MathF.Round( v.x ), MathF.Round( v.y ), MathF.Round( v.z ) );
		return global::Rotation.LookAt( Round( rotation.Forward ), Round( rotation.Up ) );
	}

	/// <summary>The shape axis (0 X, 1 Y, 2 Z) lying along the plane's Right.</summary>
	public int RightAxis => PlaneAxisOwner( 0 );

	/// <summary>The shape axis lying along the plane's Forward.</summary>
	public int ForwardAxis => PlaneAxisOwner( 1 );

	/// <summary>The shape axis along the plane normal.</summary>
	public int NormalAxis => PlaneAxisOwner( 2 );

	Rotation Local
	{
		get
		{
			var up = Up == BuilderUpAxis.Y ? global::Rotation.FromAxis( Vector3.Forward, 90f ) : global::Rotation.Identity;
			return Twist * up;
		}
	}

	public Rotation Rotation( in BuilderPlane plane ) => global::Rotation.LookAt( plane.Right, plane.Normal ) * Local;

	int PlaneAxisOwner( int planeAxis )
	{
		var local = Local;
		var best = 0;
		var bestDot = 0f;

		for ( int i = 0; i < 3; i++ )
		{
			var axis = default( Vector3 );
			axis[i] = 1;

			var dot = MathF.Abs( (local * axis)[planeAxis] );
			if ( dot <= bestDot ) continue;

			best = i;
			bestDot = dot;
		}

		return best;
	}

	/// <summary>A size along the plane's Right, Forward and normal, in the shape's own axes.</summary>
	public Vector3 ToShape( float right, float forward, float normal )
	{
		var size = default( Vector3 );
		size[RightAxis] = right;
		size[ForwardAxis] = forward;
		size[NormalAxis] = normal;
		return size;
	}

	/// <summary>Shape-local proportions as they lie on the plane: (right, forward, normal).</summary>
	public Vector3 ToPlane( Vector3 shape ) => new( shape[RightAxis], shape[ForwardAxis], shape[NormalAxis] );
}
