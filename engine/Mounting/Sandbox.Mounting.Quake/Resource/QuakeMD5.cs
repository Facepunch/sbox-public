using Sandbox;
using System;
using System.Globalization;
using System.Runtime.InteropServices;

class QuakeModelMD5( string pakDir, string fileName ) : ResourceLoader<QuakeMount>
{
	public string PakDir { get; set; } = pakDir;
	public string FileName { get; set; } = fileName;

	protected override object Load()
	{
		using var reader = new StreamReader( Host.GetFileStream( PakDir, FileName ) );
		var builder = Model.Builder;
		var text = reader.ReadToEnd();
		var mesh = ParseMd5Mesh( text, builder );

		var animFile = System.IO.Path.ChangeExtension( FileName, ".md5anim" );
		if ( Host.FileExists( PakDir, animFile ) )
		{
			using var animReader = new StreamReader( Host.GetFileStream( PakDir, animFile ) );
			var animText = animReader.ReadToEnd();
			ParseMd5Anim( animText, builder );
		}

		return builder.WithName( Path ).AddMesh( mesh ).Create();
	}

	private static void ParseMd5Anim( string text, ModelBuilder builder )
	{
		int numFrames = 0, numJoints = 0, frameRate = 24, numAnimatedComponents = 0;
		var hierarchy = new List<(int parent, int flags, int startIndex)>();
		var baseFrames = new List<(Vector3 pos, Vector3 rot)>();
		var frameData = new List<float[]>();

		var lines = text.Split( '\n' );
		bool inHierarchy = false, inBaseframe = false, inFrame = false;
		int currentFrame = -1;
		int writeCursor = 0;

		foreach ( var raw in lines )
		{
			var t = raw.Trim();
			if ( string.IsNullOrWhiteSpace( t ) ) continue;

			if ( t.StartsWith( "numFrames" ) ) numFrames = int.Parse( t.Split()[1] );
			else if ( t.StartsWith( "numJoints" ) ) numJoints = int.Parse( t.Split()[1] );
			else if ( t.StartsWith( "frameRate" ) ) frameRate = int.Parse( t.Split()[1] );
			else if ( t.StartsWith( "numAnimatedComponents" ) ) numAnimatedComponents = int.Parse( t.Split()[1] );
			else if ( t.StartsWith( "hierarchy" ) ) { inHierarchy = true; continue; }
			else if ( t.StartsWith( "baseframe" ) ) { inBaseframe = true; continue; }
			else if ( t.StartsWith( "frame" ) )
			{
				inFrame = true;
				currentFrame = int.Parse( t.Split()[1] );
				frameData.Insert( currentFrame, new float[numAnimatedComponents] );
				writeCursor = 0;
				continue;
			}
			else if ( t.StartsWith( "}" ) )
			{
				inHierarchy = false;
				inBaseframe = false;
				inFrame = false;
				continue;
			}

			if ( inHierarchy )
			{
				var p = t.Split( ' ', StringSplitOptions.RemoveEmptyEntries );
				hierarchy.Add( (int.Parse( p[1] ), int.Parse( p[2] ), int.Parse( p[3] )) );
			}
			else if ( inBaseframe )
			{
				int s0 = t.IndexOf( '(' ); int e0 = t.IndexOf( ')', s0 + 1 );
				var pos = t.Substring( s0 + 1, e0 - s0 - 1 ).Split( ' ', StringSplitOptions.RemoveEmptyEntries );
				int s1 = t.IndexOf( '(', e0 + 1 ); int e1 = t.IndexOf( ')', s1 + 1 );
				var rot = t.Substring( s1 + 1, e1 - s1 - 1 ).Split( ' ', StringSplitOptions.RemoveEmptyEntries );

				var pVec = new Vector3(
					float.Parse( pos[0], CultureInfo.InvariantCulture ),
					float.Parse( pos[1], CultureInfo.InvariantCulture ),
					float.Parse( pos[2], CultureInfo.InvariantCulture ) );

				var rQuat = new Vector3(
					float.Parse( rot[0], CultureInfo.InvariantCulture ),
					float.Parse( rot[1], CultureInfo.InvariantCulture ),
					float.Parse( rot[2], CultureInfo.InvariantCulture ) );

				baseFrames.Add( (pVec, rQuat) );
			}
			else if ( inFrame && currentFrame >= 0 )
			{
				var values = t.Split( ' ', StringSplitOptions.RemoveEmptyEntries );
				for ( int i = 0; i < values.Length; i++ )
					frameData[currentFrame][writeCursor++] = float.Parse( values[i], CultureInfo.InvariantCulture );
			}
		}

		var anim = builder.AddAnimation( "default", frameRate );

		for ( int frameIndex = 0; frameIndex < numFrames; frameIndex++ )
		{
			var transforms = new Transform[numJoints];

			for ( int i = 0; i < numJoints; i++ )
			{
				var (parent, flags, startIndex) = hierarchy[i];
				var pos = baseFrames[i].pos;
				var rot = baseFrames[i].rot;

				int index = startIndex;
				if ( (flags & 1) != 0 ) pos.x = frameData[frameIndex][index++];
				if ( (flags & 2) != 0 ) pos.y = frameData[frameIndex][index++];
				if ( (flags & 4) != 0 ) pos.z = frameData[frameIndex][index++];
				if ( (flags & 8) != 0 ) rot.x = frameData[frameIndex][index++];
				if ( (flags & 16) != 0 ) rot.y = frameData[frameIndex][index++];
				if ( (flags & 32) != 0 ) rot.z = frameData[frameIndex][index++];

				var position = ConvertPosition( pos );
				var rotation = ConvertRotation( ParseOrientation( rot.x, rot.y, rot.z ) );

				transforms[i] = new Transform( position, rotation );
			}

			anim.AddFrame( transforms );
		}
	}

	private Mesh ParseMd5Mesh( string text, ModelBuilder builder )
	{
		var joints = new List<Md5Joint>();
		var vertices = new List<Md5Vertex>();
		var weights = new List<Md5Weight>();
		var triangles = new List<Md5Triangle>();
		var inJoints = false;
		var inMesh = false;
		var shaderName = string.Empty;

		foreach ( var raw in text.Split( '\n' ) )
		{
			var t = raw.Trim();
			if ( string.IsNullOrWhiteSpace( t ) ) continue;
			if ( t.StartsWith( "joints" ) ) { inJoints = true; continue; }
			if ( t.StartsWith( "mesh" ) ) { inMesh = true; continue; }
			if ( t.StartsWith( "}" ) ) { inJoints = false; inMesh = false; continue; }

			if ( inJoints )
			{
				var p = t.Split( ' ', StringSplitOptions.RemoveEmptyEntries );
				if ( p.Length < 11 ) continue;

				var md5Pos = new Vector3(
					float.Parse( p[3], CultureInfo.InvariantCulture ),
					float.Parse( p[4], CultureInfo.InvariantCulture ),
					float.Parse( p[5], CultureInfo.InvariantCulture ) );
				var pos = ConvertPosition( md5Pos );

				var orient = ParseOrientation(
					float.Parse( p[8], CultureInfo.InvariantCulture ),
					float.Parse( p[9], CultureInfo.InvariantCulture ),
					float.Parse( p[10], CultureInfo.InvariantCulture ) );
				var rot = ConvertRotation( orient );

				joints.Add( new Md5Joint
				{
					Name = p[0].Trim( '"' ),
					Parent = int.Parse( p[1] ),
					Position = pos,
					Orientation = rot.Normal
				} );
			}
			else if ( inMesh )
			{
				if ( t.StartsWith( "shader" ) )
				{
					int start = t.IndexOf( '"' ) + 1;
					int end = t.LastIndexOf( '"' );
					shaderName = t[start..end];
				}
				else if ( t.StartsWith( "vert" ) )
				{
					var s = t.IndexOf( '(' );
					var e = t.IndexOf( ')' );
					var uv = t.Substring( s + 1, e - s - 1 )
							   .Split( ' ', StringSplitOptions.RemoveEmptyEntries );
					var parts = t.Split( ' ', StringSplitOptions.RemoveEmptyEntries );

					vertices.Add( new Md5Vertex
					{
						UV = new Vector2(
							float.Parse( uv[0], CultureInfo.InvariantCulture ),
							float.Parse( uv[1], CultureInfo.InvariantCulture ) ),
						StartWeight = int.Parse( parts[^2] ),
						WeightCount = int.Parse( parts[^1] )
					} );
				}
				else if ( t.StartsWith( "tri" ) )
				{
					var p = t.Split( ' ', StringSplitOptions.RemoveEmptyEntries );
					triangles.Add( new Md5Triangle
					{
						Index0 = int.Parse( p[2] ),
						Index1 = int.Parse( p[3] ),
						Index2 = int.Parse( p[4] )
					} );
				}
				else if ( t.StartsWith( "weight" ) )
				{
					var p = t.Split( ' ', StringSplitOptions.RemoveEmptyEntries );
					var s = t.IndexOf( '(' );
					var e = t.IndexOf( ')' );
					var posTokens = t.Substring( s + 1, e - s - 1 )
									 .Split( ' ', StringSplitOptions.RemoveEmptyEntries );

					var md5Pos = new Vector3(
						float.Parse( posTokens[0], CultureInfo.InvariantCulture ),
						float.Parse( posTokens[1], CultureInfo.InvariantCulture ),
						float.Parse( posTokens[2], CultureInfo.InvariantCulture ) );
					var pos = ConvertPosition( md5Pos );

					weights.Add( new Md5Weight
					{
						Joint = int.Parse( p[2] ),
						Bias = float.Parse( p[3], CultureInfo.InvariantCulture ),
						Position = pos
					} );
				}
			}
		}

		var positions = new Vector3[vertices.Count];
		for ( int i = 0; i < vertices.Count; i++ )
		{
			var v = vertices[i];
			var pos = Vector3.Zero;
			for ( int j = 0; j < v.WeightCount; j++ )
			{
				var w = weights[v.StartWeight + j];
				var joint = joints[w.Joint];
				pos += (joint.Position + joint.Orientation * w.Position) * w.Bias;
			}
			positions[i] = pos;
		}

		var normals = new Vector3[vertices.Count];
		foreach ( var tri in triangles )
		{
			var n = Vector3.Cross(
				positions[tri.Index1] - positions[tri.Index0],
				positions[tri.Index2] - positions[tri.Index0] ).Normal;
			normals[tri.Index0] += n;
			normals[tri.Index1] += n;
			normals[tri.Index2] += n;
		}
		for ( int i = 0; i < normals.Length; i++ ) normals[i] = normals[i].Normal;

		var verts = new List<SkinnedVertex>( vertices.Count );
		for ( int i = 0; i < vertices.Count; i++ )
		{
			var v = vertices[i];

			var boneIndices = new int[4];
			var weightValues = new float[4];

			for ( int j = 0; j < Math.Min( 4, v.WeightCount ); j++ )
			{
				var w = weights[v.StartWeight + j];
				boneIndices[j] = w.Joint;
				weightValues[j] = w.Bias;
			}

			var w0 = (int)(weightValues[0] * 255.0f + 0.5f);
			var w1 = (int)(weightValues[1] * 255.0f + 0.5f);
			var w2 = (int)(weightValues[2] * 255.0f + 0.5f);
			var w3 = (int)(weightValues[3] * 255.0f + 0.5f);

			var sum = w0 + w1 + w2 + w3;
			if ( sum != 255 )
			{
				var diff = 255 - sum;
				var max = Math.Max( Math.Max( w0, w1 ), Math.Max( w2, w3 ) );
				if ( w0 == max ) w0 += diff;
				else if ( w1 == max ) w1 += diff;
				else if ( w2 == max ) w2 += diff;
				else w3 += diff;
			}

			var blendIndices = new Color32( (byte)boneIndices[0], (byte)boneIndices[1], (byte)boneIndices[2], (byte)boneIndices[3] );
			var blendWeights = new Color32( (byte)w0, (byte)w1, (byte)w2, (byte)w3 );

			verts.Add( new SkinnedVertex( positions[i], normals[i], Vector3.Zero, vertices[i].UV, blendIndices, blendWeights ) );
		}

		var indices = new List<int>();
		foreach ( var tri in triangles )
		{
			indices.Add( tri.Index0 );
			indices.Add( tri.Index1 );
			indices.Add( tri.Index2 );
		}

		for ( int i = 0; i < joints.Count; i++ )
		{
			var j = joints[i];
			builder.AddBone( j.Name, j.Position, j.Orientation, j.Parent >= 0 ? joints[j.Parent].Name : null );
		}

		var material = Material.Create( "model", "simple_color" );

		if ( !string.IsNullOrWhiteSpace( shaderName ) )
		{
			var directoryPath = System.IO.Path.GetDirectoryName( FileName );
			var texturePath = Host.GetFullFilePath( PakDir, $"{directoryPath}/{shaderName}.lmp" );
			texturePath ??= Host.GetFullFilePath( PakDir, $"{directoryPath}/{shaderName}_00_00.lmp" );

			var texture = texturePath != null
				? Texture.Load( $"mount://quake/{PakDir}/{texturePath}.vtex" )
				: Texture.White;

			material?.Set( "g_tColor", texture );

		}
		else
		{
			material?.Set( "g_tColor", Texture.White );
		}

		var mesh = new Mesh( material );
		mesh.CreateVertexBuffer( verts.Count, verts );
		mesh.CreateIndexBuffer( indices.Count, indices.ToArray() );
		mesh.Bounds = BBox.FromPoints( positions );
		return mesh;
	}

	private static Vector3 ConvertPosition( Vector3 p )
	{
		return new Vector3( p.x, -p.y, p.z );
	}

	private static Rotation ConvertRotation( Rotation r )
	{
		return new Rotation( r.x, -r.y, r.z, r.w );
	}

	private static Rotation ParseOrientation( float x, float y, float z )
	{
		var w = MathF.Sqrt( MathF.Max( 0, 1.0f - (x * x + y * y + z * z) ) );
		return new Rotation( x, y, z, w );
	}

	private struct Md5Joint { public string Name; public int Parent; public Vector3 Position; public Rotation Orientation; }
	private struct Md5Weight { public int Joint; public float Bias; public Vector3 Position; }
	private struct Md5Vertex { public Vector2 UV; public int StartWeight; public int WeightCount; }
	private struct Md5Triangle { public int Index0, Index1, Index2; }
}

[StructLayout( LayoutKind.Sequential )]
file struct SkinnedVertex( Vector3 position, Vector3 normal, Vector3 tangent, Vector2 texcoord, Color32 blendIndices, Color32 blendWeights )
{
	[VertexLayout.Position]
	public Vector3 position = position;

	[VertexLayout.Normal]
	public Vector3 normal = normal;

	[VertexLayout.Tangent]
	public Vector3 tangent = tangent;

	[VertexLayout.TexCoord]
	public Vector2 texcoord = texcoord;

	[VertexLayout.BlendIndices]
	public Color32 blendIndices = blendIndices;

	[VertexLayout.BlendWeight]
	public Color32 blendWeights = blendWeights;

	public static readonly VertexAttribute[] Layout =
	[
		new ( VertexAttributeType.Position, VertexAttributeFormat.Float32, 3 ),
			new ( VertexAttributeType.Normal, VertexAttributeFormat.Float32, 3 ),
			new ( VertexAttributeType.Tangent, VertexAttributeFormat.Float32, 3 ),
			new ( VertexAttributeType.TexCoord, VertexAttributeFormat.Float32, 2 ),
			new ( VertexAttributeType.BlendIndices, VertexAttributeFormat.UInt8, 4 ),
			new ( VertexAttributeType.BlendWeights, VertexAttributeFormat.UInt8, 4 )
	];
}
