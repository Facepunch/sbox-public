using System;

namespace Editor.MapEditor;

static partial class MapSceneImporter
{
	static int PathLoopValue( Element path ) =>
		HammerInteger( path.Get<Element>( "entity_properties", null )?.GetValueOrDefault( "looped" ) as string, 0 );

	static Spline.Point[] ReadPathPoints( Element path, Element[] nodes, Transform transform )
	{
		// Hammer closes the curve for any nonzero value, but wraps endpoint tangents only for 1.
		bool loop = PathLoopValue( path ) == 1;
		int interpolation = path.Get( "interpolationType", 1 );
		Vector3 Position( int i ) => nodes[i].Get( "origin", System.Numerics.Vector3.Zero );
		Vector3 Tangent( int i, bool incoming )
		{
			int previous = i > 0 ? i - 1 : loop ? nodes.Length - 1 : i;
			int next = i + 1 < nodes.Length ? i + 1 : loop ? 0 : i;
			if ( incoming ) (previous, next) = (next, previous);
			var node = nodes[i];
			var type = interpolation == 2 ? node.Get( incoming ? "inTangentType" : "outTangentType", 0 ) : interpolation;
			var delta = Position( next ) - Position( i );
			if ( type == 0 ) return delta.Normal * (delta.Length / 3);
			if ( type == 1 ) return (Position( next ) - Position( previous )).Normal * (delta.Length / 3);
			Vector3 tangent = node.Get( incoming ? "inTangent" : "outTangent", System.Numerics.Vector3.Zero );
			var angles = node.Get( "angles", System.Numerics.Vector3.Zero );
			var rotation = new Angles( angles.X, angles.Y, angles.Z ).ToRotation();
			return rotation * (type == 2 ? tangent.Normal * (delta.Length / 3) : tangent * (Vector3)node.Get( "scales", System.Numerics.Vector3.One ));
		}

		var points = new Spline.Point[nodes.Length];
		for ( int i = 0; i < points.Length; i++ )
		{
			points[i] = new Spline.Point
			{
				Mode = Spline.HandleMode.Split,
				Position = transform.PointToLocal( Position( i ) ),
				In = (transform.Rotation.Inverse * Tangent( i, true )) / transform.SafeScale,
				Out = (transform.Rotation.Inverse * Tangent( i, false )) / transform.SafeScale,
				Scale = nodes[i].Get( "radiusScale", 1f )
			};
		}
		return points;
	}

	// Hammer samples the cubic parameter using its estimated segment length.
	// Both cable tessellation and model spacing depend on this stopping criterion.
	internal readonly struct PathSegment( Spline.Point start, Spline.Point end )
	{
		readonly Vector3 origin = start.Position;
		readonly Vector3 a = -start.Position + 3 * (start.Position + start.Out) - 3 * (end.Position + end.In) + end.Position;
		readonly Vector3 b = 3 * start.Position - 6 * (start.Position + start.Out) + 3 * (end.Position + end.In);
		readonly Vector3 c = -3 * start.Position + 3 * (start.Position + start.Out);

		public Vector3 Point( float t ) => a * t * t * t + b * t * t + c * t + origin;
		public Vector3 Tangent( float t ) => 3 * a * t * t + 2 * b * t + c;

		public float Length() => Length( 0.01f, out _ );

		public float Length( float tolerance, out int samples )
		{
			float length = 0;
			for ( samples = 2; samples <= 128; samples *= 2 )
			{
				float previous = length;
				length = 0;
				var last = origin;
				float step = 1f / (samples - 1);
				for ( int i = 1; i < samples; i++ )
				{
					var point = Point( i * step );
					length += point.Distance( last );
					last = point;
				}
				if ( MathF.Abs( previous - length ) / length < tolerance ) break;
			}
			return length;
		}
	}

	static SourceNode[] ReadModelPath( Element path, Transform transform )
	{
		var model = path.Get( "modelPath", "" );
		if ( string.IsNullOrEmpty( model ) ) return [];
		var nodes = path.SceneChildren.Where( n => n.ClassName is "CMapPathNode" or "CTrajectoryPathNode" ).ToArray();
		var points = ReadPathPoints( path, nodes, Transform.Zero );
		var placements = new List<SourceNode>();
		var keys = new Dictionary<string, string> { ["classname"] = "prop_static", ["model"] = model, ["solid"] = "6" };
		float spacing = MathF.Max( 1, path.Get( "spacing", 50f ) );
		bool orient = path.Get( "orientateToPath", true );
		float distance = 0;
		for ( int i = 1; i < points.Length; i++ )
		{
			var segment = new PathSegment( points[i - 1], points[i] );
			float length = segment.Length();
			if ( length <= 0 ) continue;
			for ( float along = placements.Count * spacing - distance; along <= length; along += spacing )
			{
				float t = along / length;
				var rotation = orient ? Rotation.LookAt( segment.Tangent( t ), Vector3.Up ) : transform.Rotation;
				var at = new Transform( segment.Point( t ), rotation );
				placements.Add( new SourceNode
				{
					Kind = NodeKind.Entity,
					TypeString = "CMapEntity",
					Transform = at,
					EntityKeys = keys,
					Entity = CreateEntityEntry( keys, at ),
					Meshes = [],
					Children = []
				} );
			}
			distance += length;
		}
		return placements.ToArray();
	}
}
