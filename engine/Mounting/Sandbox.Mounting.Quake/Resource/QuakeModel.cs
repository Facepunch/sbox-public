using Sandbox;
using System;
using System.Text;

class QuakeModel( string pakDir, string fileName ) : ResourceLoader<QuakeMount>
{
	public string PakDir { get; set; } = pakDir;
	public string FileName { get; set; } = fileName;

	public BinaryReader Read()
	{
		return new BinaryReader( Host.GetFileStream( PakDir, FileName ) );
	}

	protected override object Load()
	{
		using var br = Read();

		var header = new
		{
			Ident = br.ReadInt32(),
			Version = br.ReadInt32(),
			Scale = new Vector3( br.ReadSingle(), br.ReadSingle(), br.ReadSingle() ),
			Translate = new Vector3( br.ReadSingle(), br.ReadSingle(), br.ReadSingle() ),
			BoundingRadius = br.ReadSingle(),
			EyePosition = new Vector3( br.ReadSingle(), br.ReadSingle(), br.ReadSingle() ),
			NumSkins = br.ReadInt32(),
			SkinWidth = br.ReadInt32(),
			SkinHeight = br.ReadInt32(),
			NumVerts = br.ReadInt32(),
			NumTris = br.ReadInt32(),
			NumFrames = br.ReadInt32(),
			Synctype = br.ReadInt32(),
			Flags = br.ReadInt32(),
			Size = br.ReadSingle()
		};

		if ( header.Ident != 0x4F504449 || header.Version != 6 )
			throw new Exception( "Invalid MDL file format" );

		byte[] skinData = null;
		for ( var i = 0; i < header.NumSkins; i++ )
		{
			var group = br.ReadInt32();
			if ( group == 0 )
			{
				var data = br.ReadBytes( header.SkinWidth * header.SkinHeight );
				if ( i == 0 ) skinData = data;
			}
			else if ( group == 1 )
			{
				var numGroupSkins = br.ReadInt32();
				br.BaseStream.Seek( numGroupSkins * sizeof( float ), SeekOrigin.Current );

				if ( i == 0 )
				{
					skinData = br.ReadBytes( header.SkinWidth * header.SkinHeight );
					br.BaseStream.Seek( (numGroupSkins - 1) * header.SkinWidth * header.SkinHeight, SeekOrigin.Current );
				}
				else
				{
					br.BaseStream.Seek( numGroupSkins * header.SkinWidth * header.SkinHeight, SeekOrigin.Current );
				}
			}
		}

		var stVerts = new (int onSeam, int s, int t)[header.NumVerts];
		for ( var i = 0; i < header.NumVerts; i++ )
		{
			stVerts[i] = (br.ReadInt32(), br.ReadInt32(), br.ReadInt32());
		}

		var triangles = new (int front, int v1, int v2, int v3)[header.NumTris];
		for ( var i = 0; i < header.NumTris; i++ )
		{
			triangles[i] = (br.ReadInt32(), br.ReadInt32(), br.ReadInt32(), br.ReadInt32());
		}

		int frameType = br.ReadInt32();
		if ( frameType == 1 )
		{
			var nb = br.ReadInt32();
			br.BaseStream.Seek( 8 + (nb * sizeof( float )), SeekOrigin.Current );
		}

		var minX = br.ReadByte(); var minY = br.ReadByte(); var minZ = br.ReadByte(); br.ReadByte();
		var maxX = br.ReadByte(); var maxY = br.ReadByte(); var maxZ = br.ReadByte(); br.ReadByte();

		var frameName = Encoding.UTF8.GetString( br.ReadBytes( 16 ) );
		var positions = new Vector3[header.NumVerts];
		var normals = new int[header.NumVerts];
		var normalTable = Anorms.Values;

		for ( var i = 0; i < header.NumVerts; i++ )
		{
			var x = br.ReadByte();
			var y = br.ReadByte();
			var z = br.ReadByte();
			var normalIndex = br.ReadByte();

			var position = new Vector3(
				header.Scale.x * x + header.Translate.x,
				header.Scale.y * y + header.Translate.y,
				header.Scale.z * z + header.Translate.z
			);

			positions[i] = position;
			normals[i] = normalIndex;
		}

		Texture texture = null;
		if ( skinData != null )
		{
			var palette = Host.GetPalette( PakDir );
			if ( palette is not null )
			{
				var width = header.SkinWidth;
				var height = header.SkinHeight;
				var length = width * height;
				var imageData = new byte[length * 4];
				int offset = 0;

				for ( var i = 0; i < length; i++ )
				{
					var index = skinData[i];
					var paletteOffset = index * 3;

					imageData[offset++] = palette[paletteOffset];
					imageData[offset++] = palette[paletteOffset + 1];
					imageData[offset++] = palette[paletteOffset + 2];
					imageData[offset++] = (index == 255) ? (byte)0 : (byte)255;
				}

				texture = Texture.Create( width, height )
					.WithData( imageData )
					.WithMips()
					.Finish();
			}
		}

		var material = Material.Create( "model", "simple_color" );
		material?.Set( "g_tColor", texture ?? Texture.White );
		var mesh = new Mesh( material );

		var uniqueVertices = new List<SimpleVertex>( header.NumVerts );
		var vertexMap = new Dictionary<(int, int, float, float), int>();

		Vector2 ComputeUV( (int onSeam, int s, int t) st, bool front )
		{
			var s = (float)st.s;
			if ( !front && st.onSeam != 0 ) s += header.SkinWidth * 0.5f;
			var u = (s + 0.5f) / header.SkinWidth;
			var v = (st.t + 0.5f) / header.SkinHeight;
			return new Vector2( u, v );
		}

		var indices = new int[header.NumTris * 3];
		var traceIndices = new int[header.NumTris * 3];
		var indexCount = 0;

		for ( var i = 0; i < header.NumTris; i++ )
		{
			var (front, v1, v2, v3) = triangles[i];
			var isFront = front != 0;
			var triVerts = new int[] { v3, v2, v1 };

			for ( var j = 0; j < 3; j++ )
			{
				var vertexIndex = triVerts[j];
				var uv = ComputeUV( stVerts[vertexIndex], isFront );
				var key = (vertexIndex, normals[vertexIndex], uv.x, uv.y);

				if ( !vertexMap.TryGetValue( key, out int index ) )
				{
					index = uniqueVertices.Count;
					uniqueVertices.Add( new SimpleVertex(
						positions[vertexIndex],
						normalTable[normals[vertexIndex]],
						Vector3.Zero,
						uv
					) );
					vertexMap[key] = index;
				}

				indices[indexCount] = index;
				traceIndices[indexCount] = vertexIndex;

				indexCount++;
			}
		}

		mesh.CreateVertexBuffer( uniqueVertices.Count, uniqueVertices );
		mesh.CreateIndexBuffer( indices.Length, indices );
		mesh.Bounds = BBox.FromPoints( uniqueVertices.Select( x => x.position ) );

		return Model.Builder
			.WithName( Path )
			.AddMesh( mesh )
			.AddTraceMesh( positions, traceIndices )
			.Create();
	}
}
