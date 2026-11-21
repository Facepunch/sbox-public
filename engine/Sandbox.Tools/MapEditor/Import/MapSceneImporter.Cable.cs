using System;
using System.Globalization;

namespace Editor.MapEditor;

static partial class MapSceneImporter
{
	readonly record struct CableSample( Vector3 Position, float Radius, Color32 Color );

	// Hammer only builds particle-rope collision when both entity keys opt into it.
	internal static bool HasParticleRopeCollision( Element keys )
	{
		return HammerInteger( keys.Get<string>( "static_collision", null ), 0 ) > 0
			&& float.TryParse( keys.Get( "radius", "" ), CultureInfo.InvariantCulture, out var radius ) && radius > 0;
	}

	static SceneMeshData[] ReadCable( Element cable, Transform transform )
	{
		var nodes = cable.SceneChildren.Where( n => n.ClassName == "CMapPathNode" ).ToArray();
		var keys = cable.Get<Element>( "entity_properties", null );
		bool particleRope = keys?.Get( "classname", "" ) == "path_particle_rope";
		int sides = cable.Get( "numSides", 8 );
		if ( nodes.Length < 2 || sides < 3 ) return null;
		// Hammer's Convert To Static Cables copies these four values from the rope's entity keys.
		float RopeValue( string key, float fallback ) => float.TryParse( keys.Get( key, "" ), CultureInfo.InvariantCulture, out var value ) ? value : fallback;
		float spacing = particleRope ? RopeValue( "particle_spacing", 16f ) : cable.Get( "tessellationSpacing", 16f );
		float radius = particleRope ? RopeValue( "radius", 16f ) : cable.Get( "radius", 16f );
		bool collision = particleRope ? HasParticleRopeCollision( keys ) : cable.Get( "collisionEnabled", true );
		var white = new Color32( 255, 255, 255, 255 );
		var tint = particleRope ? Color32.Parse( keys.Get( "color_tint", "" ) ) ?? white : cable.Get( "tintColor", white );
		if ( spacing <= 0 ) throw new System.IO.InvalidDataException( "Cable tessellation spacing must be positive." );

		var points = ReadPathPoints( cable, nodes, transform.WithScale( 1 ) );
		var samples = SampleCable( points, nodes, radius, spacing, false );
		var material = Material.Load( cable.Get( "materialName", "materials/default/white.vmat" ) );
		var tags = keys?.Get( "tags", "" );
		var render = new SceneMeshData( BuildCableMesh( cable, transform, samples, sides, 1, material, true ), 40, SceneMeshPhysics.None, tint, true, tags );
		if ( !collision ) return [render];

		if ( particleRope ) samples = SampleCable( points, nodes, radius, spacing, true );
		int collisionSides = particleRope ? 6 : sides <= 8 ? sides : Math.Clamp( sides / 2, 8, 16 );
		float error = particleRope ? 0 : cable.Get( "physicsSimplificationError", 2f );
		var collider = BuildCableMesh( cable, transform, samples, collisionSides, particleRope ? 1 : 1.025f, particleRope ? null : material, false, error );
		string surface = particleRope ? keys.Get( "surface_properties", "" ) : null;
		return [render, new( collider, 0, SceneMeshPhysics.Mesh, white, false, tags, HideInGame: true,
			CollisionSurface: string.IsNullOrEmpty( surface ) ? null : Surface.FindByName( surface ) )];
	}

	static List<CableSample> SampleCable( Spline.Point[] points, Element[] nodes, float radius, float spacing, bool useCurvature )
	{
		var white = new Color32( 255, 255, 255, 255 );
		var samples = new List<CableSample>();
		for ( int i = 0; i < nodes.Length - 1; i++ )
		{
			var segment = new PathSegment( points[i], points[i + 1] );
			int count;
			if ( useCurvature ) segment.Length( 0.05f, out count );
			else count = Math.Max( 4, (int)MathF.Ceiling( segment.Length() / spacing ) );
			float radiusA = nodes[i].Get( "radiusScale", 1f ), radiusB = nodes[i + 1].Get( "radiusScale", 1f );
			Color colorA = nodes[i].Get( "tintColor", white ), colorB = nodes[i + 1].Get( "tintColor", white );
			float t = 0, stepSize = 1.0f / (count - 1);
			int limit = i + 2 == nodes.Length ? count : count - 1;
			for ( int j = 0; j < limit; j++, t += stepSize )
			{
				float fraction = (float)j / (count - 1);
				// Hammer truncates path RGB to bytes and takes opacity from the cable itself.
				var color = Color.Lerp( colorA, colorB, fraction, false );
				var rgb = new Color32( (byte)(color.r * 255), (byte)(color.g * 255), (byte)(color.b * 255), 255 );
				samples.Add( new( segment.Point( t ), (radiusA + fraction * (radiusB - radiusA)) * radius, rgb ) );
			}
		}

		return samples;
	}

	static PolygonMesh BuildCableMesh( Element cable, Transform transform, List<CableSample> samples, int sides, float radiusScale, Material material, bool render, float error = 0 )
	{
		var mesh = new PolygonMesh();
		bool horizontal = cable.Get( "textureOrientation", 0 ) == 0;
		var texture = render && material is { IsError: false } && !string.IsNullOrEmpty( cable.Get<string>( "materialName", null ) )
			? material.Attributes.GetTexture( "RepresentativeTexture" ) : null;
		int dimension = texture is { IsValid: true } ? (horizontal ? texture.Width : texture.Height) : 512;
		float texelsPerUnit = 1.0f / cable.Get( "textureScale", 0.25f );
		float pathOffset = cable.Get( "textureOffsetAlongPath", 0f );
		float pathUV = 0;
		float offset = cable.Get( "textureOffsetCircumference", 0f );
		float repeats = cable.Get( "textureRepeatsCircumference", 1f );
		int vertexCount = checked(samples.Count * (sides + 1));
		var vertices = error > 0 ? null : new HalfEdgeMesh.VertexHandle[vertexCount];
		var positions = error > 0 ? new List<Vector3>( vertexCount ) : null;
		var indices = error > 0 ? new List<int>( checked((samples.Count - 1) * sides * 6) ) : null;
		var corners = render ? new (Vector2 UV, Vector3 Normal)[vertexCount] : null;
		var profile = new Vector2[sides];
		float angle = 0, angleStep = (float)(2 * Math.PI / sides);
		for ( int j = 0; j < sides; j++, angle += angleStep ) profile[j] = new( MathF.Sin( angle ), MathF.Cos( angle ) );
		// Hammer builds the tube from world-space path nodes and applies only the cable rotation and origin.
		var scale = transform.SafeScale;
		Vector3 forward = default, up = default;
		for ( int i = 0; i < samples.Count; i++ )
		{
			var point = samples[i];
			if ( i + 1 < samples.Count )
			{
				var direction = (samples[i + 1].Position - point.Position).Normal;
				if ( i == 0 )
				{
					up = new Vector3( direction.z + direction.y * direction.y * (1 - direction.z), 0, -direction.x ).Normal;
					up = (up - direction * up.Dot( direction )).Normal;
				}
				else
				{
					float dot = Math.Clamp( forward.Dot( direction ), -1f, 1f );
					if ( MathF.Abs( dot ) <= 0.999f ) up = Rotation.FromAxis( forward.Cross( direction ).Normal, MathF.Acos( dot ).RadianToDegree() ) * up;
					up = direction.Cross( up.Cross( direction ).Normal ).Normal;
				}
				forward = direction;
			}
			if ( render && i > 0 ) pathUV += (point.Position - samples[i - 1].Position).Length * texelsPerUnit / dimension;
			float along = pathUV + pathOffset;
			var left = up.Cross( forward );
			for ( int j = 0; j <= sides; j++ )
			{
				var unit = profile[j % sides];
				int index = i * (sides + 1) + j;
				var radial = left * unit.x + up * unit.y;
				var position = point.Position + radial * (point.Radius * radiusScale);
				if ( positions is null ) vertices[index] = mesh.AddVertex( position / scale );
				else positions.Add( position );
				if ( !render ) continue;
				float around = ((float)j / sides) * repeats + offset;
				corners[index] = (horizontal ? new( along, around ) : new( around, along ), (radial * scale).Normal);
			}
		}
		void Triangle( int a, int b, int c )
		{
			if ( indices is not null )
			{
				indices.Add( a );
				indices.Add( b );
				indices.Add( c );
				return;
			}
			var face = mesh.AddFace( vertices[a], vertices[b], vertices[c] );
			if ( material is not null ) mesh.SetFaceMaterial( face, material );
			if ( !render ) return;
			var corner = face.Edge;
			foreach ( int index in (ReadOnlySpan<int>)[a, b, c] )
			{
				mesh.SetTextureCoord( corner, corners[index].UV );
				mesh.SetCornerNormal( corner, corners[index].Normal );
				mesh.SetVertexColor( corner, samples[index / (sides + 1)].Color );
				corner = corner.NextEdge;
			}
		}
		for ( int i = 0; i < samples.Count - 1; i++ )
			for ( int j = 0; j < sides; j++ )
			{
				int a = i * (sides + 1) + j, b = a + sides + 1;
				Triangle( a, b, a + 1 );
				Triangle( a + 1, b, b + 1 );
			}
		if ( positions is not null )
		{
			// Hammer simplifies in cable coordinates, before applying the placement transform.
			var (points, triangles) = MeshCollisionFitting.SimplifySurface( positions, indices, Transform.Zero, error );
			var handles = new HalfEdgeMesh.VertexHandle[points.Length];
			for ( int i = 0; i < points.Length; i++ ) handles[i] = mesh.AddVertex( points[i] / scale );
			for ( int i = 0; i < triangles.Length; i += 3 )
			{
				var face = mesh.AddFace( handles[triangles[i]], handles[triangles[i + 1]], handles[triangles[i + 2]] );
				mesh.SetFaceMaterial( face, material );
			}
		}
		if ( render ) mesh.SetSmoothingAngle( 40 );
		return mesh;
	}
}
