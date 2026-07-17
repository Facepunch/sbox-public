using System;
using System.Runtime.InteropServices;

partial class QuakeMap
{
	private const int LightmapPageSize = 4096;

	struct FaceSurface
	{
		public float umin;
		public float vmin;
		public Vector2 offset;
		public Vector2 size;
		public int page;
	}

	private Model BuildWorldModel( Quake.BSP.File file )
	{
		ParseLiquidAlphas( file );

		const int MaxLength = 64;
		const int StyleCount = 12;
		var byteData = new byte[MaxLength * 3 * 4];
		for ( int styleIndex = 0; styleIndex < StyleCount; styleIndex++ )
		{
			int row = styleIndex / 4;
			int channel = styleIndex % 4;

			var style = DefaultLightstyles[styleIndex];

			for ( int frame = 0; frame < MaxLength; frame++ )
			{
				char ch = style[frame % style.Length];
				int texelIndex = ((row * MaxLength) + frame) * 4;
				byteData[texelIndex + channel] = (byte)ch;
			}
		}

		var lightstyleTex = Texture.Create( MaxLength, 3, ImageFormat.RGBA8888 )
			.WithData( byteData )
			.Finish();

		_lightstyleTex = lightstyleTex;

		var litPath = System.IO.Path.ChangeExtension( FileName, ".lit" );
		var litData = Host.GetFileBytes( PakDir, litPath );
		var palette = Host.GetPalette( PakDir );

		var textureCount = file.Textures.Length;
		if ( palette is null && textureCount > 0 )
			throw new Exception( "Missing palette.lmp" );

		CreateSurfaces( file, litData );

		var decoded = new byte[textureCount][];
		var masks = new byte[textureCount][];
		var dims = new (int Width, int Height)[textureCount];

		System.Threading.Tasks.Parallel.For( 0, textureCount, i =>
		{
			var scale = file.TextureData[i].Scale;
			var t = file.Textures[i];
			int w = (int)t.Width * scale;
			int h = (int)t.Height * scale;
			var pixelCount = w * h;
			var textureData = new byte[pixelCount * 4];
			var maskData = new byte[pixelCount * 4];
			var textureIndices = file.TextureData[i].Data;

			for ( int j = 0; j < pixelCount; ++j )
			{
				var index = textureIndices[j];
				if ( index == 255 )
					continue;

				textureData[(j * 4) + 0] = palette[(index * 3) + 0];
				textureData[(j * 4) + 1] = palette[(index * 3) + 1];
				textureData[(j * 4) + 2] = palette[(index * 3) + 2];
				textureData[(j * 4) + 3] = 255;

				if ( index >= 224 )
				{
					maskData[(j * 4) + 0] = 255;
					maskData[(j * 4) + 3] = 255;
				}
			}

			decoded[i] = textureData;
			masks[i] = maskData;
			dims[i] = (w, h);
		} );

		for ( int i = 0; i < textureCount; ++i )
		{
			var (w, h) = dims[i];
			int numMips = (int)Math.Log2( Math.Max( 1, Math.Min( w, h ) ) ) + 1;
			Textures.Add( Texture.Create( w, h, ImageFormat.RGBA8888 )
				.WithData( decoded[i] )
				.WithMips( numMips )
				.Finish() );
			FullbrightTextures.Add( Texture.Create( w, h, ImageFormat.RGBA8888 )
				.WithData( masks[i] )
				.WithMips( numMips )
				.Finish() );
		}

		var worldModel = CreateModel( file, file.Models[0], WorldModelName );
		return worldModel;
	}

	private Material GetMaterial( Quake.BSP.File file, int texIndex, int page )
	{
		var key = (texIndex, page);
		if ( _materials.TryGetValue( key, out var material ) )
			return material;

		var name = Quake.BSP.File.GetString( file.Textures[texIndex].Name );
		material = Material.Create( $"{name}_{texIndex}_{page}", "shaders/quake_generic.shader" );
		material.Set( "Color", Textures[texIndex] );
		material.Set( "Fullbright", FullbrightTextures[texIndex] );

		var lightmaps = _lightmapPages[page];
		material.Set( "LightmapR", lightmaps[0] );
		material.Set( "LightmapG", lightmaps[1] );
		material.Set( "LightmapB", lightmaps[2] );
		material.Set( "LightstyleTex", _lightstyleTex );

		_materials[key] = material;
		return material;
	}

	private Material GetWaterMaterial( Quake.BSP.File file, int texIndex )
	{
		var key = (texIndex, -1);
		if ( _materials.TryGetValue( key, out var material ) )
			return material;

		var name = Quake.BSP.File.GetString( file.Textures[texIndex].Name );
		material = Material.Create( $"{name}_{texIndex}_water", "shaders/quake_water.shader" );
		material.Set( "Color", Textures[texIndex] );
		material.Set( "g_flAlpha", LiquidAlpha( name ) );

		_materials[key] = material;
		return material;
	}

	private float LiquidAlpha( string name )
	{
		if ( name.Contains( "lava", StringComparison.OrdinalIgnoreCase ) )
			return _lavaAlpha;
		if ( name.Contains( "slime", StringComparison.OrdinalIgnoreCase ) )
			return _slimeAlpha;
		if ( name.Contains( "tele", StringComparison.OrdinalIgnoreCase ) )
			return _teleAlpha;
		return _waterAlpha;
	}

	private void ParseLiquidAlphas( Quake.BSP.File file )
	{
		foreach ( var entity in file.Entities )
		{
			if ( entity.TypeName != "worldspawn" )
				continue;

			_waterAlpha = entity.GetValue( "wateralpha", 0.5f );
			_lavaAlpha = entity.GetValue( "lavaalpha", 1.0f );
			_slimeAlpha = entity.GetValue( "slimealpha", 0.5f );
			_teleAlpha = entity.GetValue( "telealpha", 0.5f );
			return;
		}
	}

	private void CreateSurfaces( Quake.BSP.File file, byte[] litData )
	{
		const int size = LightmapPageSize;

		var lightmapRoot = NewLightmapRoot( size );

		var lightmapData1 = new byte[size * size * 4];
		var lightmapData2 = new byte[size * size * 4];
		var lightmapData3 = new byte[size * size * 4];
		var page = 0;

		void FinishPage()
		{
			_lightmapPages.Add(
			[
				Texture.Create( size, size, ImageFormat.RGBA8888 ).WithData( lightmapData1 ).Finish(),
				Texture.Create( size, size, ImageFormat.RGBA8888 ).WithData( lightmapData2 ).Finish(),
				Texture.Create( size, size, ImageFormat.RGBA8888 ).WithData( lightmapData3 ).Finish(),
			] );
		}

		Surfaces = new FaceSurface[file.Faces.Length];
		var faceCount = file.Faces.Length;
		var lwidths = new int[faceCount];
		var lheights = new int[faceCount];

		Parallel.For( 0, faceCount, faceIndex =>
		{
			var bspFace = file.Faces[faceIndex];
			var bspTexInfo = file.TexInfos[bspFace.TextureInfo];

			var umin = float.MaxValue;
			var vmin = float.MaxValue;
			var umax = float.MinValue;
			var vmax = float.MinValue;

			for ( var edgeIndex = 0; edgeIndex < bspFace.EdgeCount; ++edgeIndex )
			{
				var bspSurfEdge = file.SurfEdges[(int)(bspFace.FirstEdge + edgeIndex)];
				var vertex = file.Vertices[bspSurfEdge > 0 ? file.Edges[bspSurfEdge].Vertex0 : file.Edges[-bspSurfEdge].Vertex1];

				var u = (vertex.x * bspTexInfo.S.x) + (vertex.y * bspTexInfo.S.y) + (vertex.z * bspTexInfo.S.z) + bspTexInfo.OffsetS;
				var v = (vertex.x * bspTexInfo.T.x) + (vertex.y * bspTexInfo.T.y) + (vertex.z * bspTexInfo.T.z) + bspTexInfo.OffsetT;

				umin = MathF.Min( u, umin );
				vmin = MathF.Min( v, vmin );
				umax = MathF.Max( u, umax );
				vmax = MathF.Max( v, vmax );
			}

			var minu = MathF.Floor( umin / 16.0f );
			var maxu = MathF.Ceiling( umax / 16.0f );
			var minv = MathF.Floor( vmin / 16.0f );
			var maxv = MathF.Ceiling( vmax / 16.0f );
			var lwidth = (int)(maxu - minu + 1);
			var lheight = (int)(maxv - minv + 1);

			lwidths[faceIndex] = lwidth;
			lheights[faceIndex] = lheight;
			Surfaces[faceIndex] = new FaceSurface
			{
				umin = minu * 16,
				vmin = minv * 16,
				size = new Vector2( lwidth, lheight ),
			};
		} );

		bool hasLit = litData != null && litData.Length > 0;

		for ( var faceIndex = 0; faceIndex < faceCount; ++faceIndex )
		{
			var lwidth = lwidths[faceIndex];
			var lheight = lheights[faceIndex];

			if ( lwidth > size || lheight > size )
			{
				Surfaces[faceIndex].page = page;
				Surfaces[faceIndex].offset = Vector2.Zero;
				continue;
			}

			var node = AllocateLightmapRect( lightmapRoot, lwidth, lheight );

			// Current page is full: finalise it and start a fresh one.
			if ( node is null )
			{
				FinishPage();
				page++;
				lightmapRoot = NewLightmapRoot( size );
				lightmapData1 = new byte[size * size * 4];
				lightmapData2 = new byte[size * size * 4];
				lightmapData3 = new byte[size * size * 4];
				node = AllocateLightmapRect( lightmapRoot, lwidth, lheight );
			}

			Surfaces[faceIndex].page = page;

			// Doesn't fit even in an empty page: keep the geometry, just unlit.
			if ( node is null )
			{
				Surfaces[faceIndex].offset = Vector2.Zero;
				continue;
			}

			Surfaces[faceIndex].offset = new Vector2( node.X, node.Y );

			var bspFace = file.Faces[faceIndex];
			if ( bspFace.LightmapOffset != -1 )
			{
				int[] styles = { bspFace.Style0, bspFace.Style1, bspFace.Style2, bspFace.Style3 };

				// Each lightstyle has its own (lwidth * lheight) block stored consecutively for this
				// face. styleBlock counts how many valid styles we've consumed so we index the right one.
				var faceLuxels = lwidth * lheight;
				var styleBlock = 0;

				for ( int s = 0; s < styles.Length; s++ )
				{
					if ( styles[s] == 255 )
						continue;

					for ( var x = 0; x < lwidth; ++x )
					{
						for ( var y = 0; y < lheight; ++y )
						{
							var dstPixel = ((node.Y + y) * size) + node.X + x;
							var luxel = (y * lwidth) + x;

							if ( hasLit )
							{
								var srcPixel = (bspFace.LightmapOffset * 3) + 8 + ((styleBlock * faceLuxels + luxel) * 3);
								if ( srcPixel + 2 >= litData.Length )
									continue;

								lightmapData1[(dstPixel * 4) + s] = litData[srcPixel + 0];
								lightmapData2[(dstPixel * 4) + s] = litData[srcPixel + 1];
								lightmapData3[(dstPixel * 4) + s] = litData[srcPixel + 2];
							}
							else
							{
								var srcPixel = bspFace.LightmapOffset + (styleBlock * faceLuxels) + luxel;
								if ( srcPixel >= file.LightmapData.Length )
									continue;

								byte light = file.LightmapData[srcPixel];

								lightmapData1[(dstPixel * 4) + s] = light;
								lightmapData2[(dstPixel * 4) + s] = light;
								lightmapData3[(dstPixel * 4) + s] = light;
							}
						}
					}

					styleBlock++;
				}
			}
		}

		FinishPage();
	}

	private static LightmapNode NewLightmapRoot( int size ) => new()
	{
		Width = size,
		Height = size,
		MaxW = size,
		MaxH = size,
	};

	private Model CreateModel( Quake.BSP.File file, Quake.BSP.Model bspModel, string modelName, Vector3 origin = default )
	{
		var meshes = new Dictionary<(int Tex, int Page), (Mesh, List<MapVertex>, List<int>)>();
		var collisionVertices = new List<Vector3>();
		var collisionIndices = new List<int>();

		var bounds = new BBox
		{
			Mins = float.MaxValue,
			Maxs = float.MinValue
		};

		for ( var faceIndex = 0; faceIndex < bspModel.FaceCount; ++faceIndex )
		{
			var bspFace = file.Faces[bspModel.FirstFace + faceIndex];
			var bspTexInfo = file.TexInfos[bspFace.TextureInfo];
			var tex = file.Textures[bspTexInfo.MipTex];

			var texIndex = (int)bspTexInfo.MipTex;
			var texName = Quake.BSP.File.GetString( tex.Name );
			var isSky = texName.StartsWith( "sky", StringComparison.OrdinalIgnoreCase );
			var isLiquid = texName.Length > 0 && texName[0] == '*';

			if ( isSky )
				continue;
			if ( !isLiquid && (bspTexInfo.Flags & 1) != 0 )
				continue;

			var surface = Surfaces[bspModel.FirstFace + faceIndex];
			var page = isLiquid ? -1 : surface.page;
			var key = (texIndex, page);
			if ( !meshes.TryGetValue( key, out var meshGroup ) )
			{
				var material = isLiquid ? GetWaterMaterial( file, texIndex ) : GetMaterial( file, texIndex, surface.page );
				meshGroup = (new Mesh( material ), [], []);
				meshes[key] = meshGroup;
			}

			var vertices = meshGroup.Item2;
			var indices = meshGroup.Item3;

			var bspPlane = file.Planes[bspFace.Plane];
			var indexOffset = vertices.Count;
			var collisionIndexOffset = collisionVertices.Count;

			var umin = surface.umin;
			var vmin = surface.vmin;
			var lx = surface.offset.x;
			var ly = surface.offset.y;
			var lwidth = surface.size.x;
			var lheight = surface.size.y;

			var faceVertices = new Vector3[bspFace.EdgeCount];

			for ( var edgeIndex = 0; edgeIndex < bspFace.EdgeCount; ++edgeIndex )
			{
				var bspSurfEdge = file.SurfEdges[(int)(bspFace.FirstEdge + edgeIndex)];
				var vertex = file.Vertices[bspSurfEdge > 0 ? file.Edges[bspSurfEdge].Vertex0 : file.Edges[-bspSurfEdge].Vertex1];

				var u = (vertex.x * bspTexInfo.S.x) + (vertex.y * bspTexInfo.S.y) + (vertex.z * bspTexInfo.S.z) + bspTexInfo.OffsetS;
				var v = (vertex.x * bspTexInfo.T.x) + (vertex.y * bspTexInfo.T.y) + (vertex.z * bspTexInfo.T.z) + bspTexInfo.OffsetT;

				var uv = new Vector2( u, v );
				var uv2 = uv;
				var position = vertex - origin;
				var normal = (bspPlane.Normal * (bspFace.PlaneSide == 0 ? 1 : -1)).Normal;
				var tangent = bspTexInfo.S;

				uv /= new Vector2( tex.Width, tex.Height );

				double ucoord = uv2.x;
				ucoord -= umin;
				ucoord += 8.0;
				ucoord /= lwidth * 16.0;

				double vcoord = uv2.y;
				vcoord -= vmin;
				vcoord += 8.0;
				vcoord /= lheight * 16.0;

				ucoord = ((ucoord * lwidth) + lx) / LightmapPageSize;
				vcoord = ((vcoord * lheight) + ly) / LightmapPageSize;

				uv2 = new Vector2( (float)ucoord, (float)vcoord );

				vertices.Add( new MapVertex
				{
					position = position,
					normal = normal,
					tangent = tangent,
					texcoord = uv,
					texcoord2 = uv2,
					LightStyle0 = bspFace.Style0,
					LightStyle1 = bspFace.Style1,
					LightStyle2 = bspFace.Style2,
					LightStyle3 = bspFace.Style3,
				} );

				if ( !isLiquid )
					collisionVertices.Add( position );

				bounds = bounds.AddPoint( position );

				faceVertices[edgeIndex] = position;
			}

			var faceIndices = Mesh.TriangulatePolygon( faceVertices );
			for ( var i = 0; i < faceIndices.Length; ++i )
			{
				var index = faceIndices[faceIndices.Length - 1 - i];
				indices.Add( indexOffset + index );
				if ( !isLiquid )
					collisionIndices.Add( collisionIndexOffset + index );
			}
		}

		var builder = Model.Builder.WithName( modelName );

		foreach ( var m in meshes.Values )
		{
			var mesh = m.Item1;
			var vertices = m.Item2;
			var indices = m.Item3;

			if ( vertices.Count == 0 || indices.Count == 0 )
				continue;

#pragma warning disable CS0618
			mesh.CreateVertexBuffer( vertices.Count, MapVertex.Layout, vertices );
#pragma warning restore CS0618
			mesh.CreateIndexBuffer( indices.Count, indices );
			mesh.Bounds = bounds;

			builder.AddMesh( mesh );
		}

		if ( collisionVertices.Count > 0 && collisionIndices.Count > 0 )
		{
			builder.AddCollisionMesh( collisionVertices.ToArray(), collisionIndices.ToArray() );
			builder.AddTraceMesh( collisionVertices, collisionIndices );
		}

		return builder.Create();
	}

	/// <summary>
	/// Stable, deterministic name the world model is registered under so the serialized scene's
	/// ModelRenderer can resolve it again via <see cref="Model.Load(string)"/>.
	/// </summary>
	private string WorldModelName => $"quakemap/{PakDir}/{FileName}".Replace( '\\', '/' );

	class LightmapNode
	{
		public int X;
		public int Y;
		public int Width;
		public int Height;
		public bool Filled;
		public LightmapNode[] Nodes;

		public int MaxW;
		public int MaxH;
	}

	private static LightmapNode AllocateLightmapRect( LightmapNode node, int width, int height )
	{
		if ( node.MaxW < width || node.MaxH < height )
			return null;

		if ( node.Nodes != null )
		{
			var ret = AllocateLightmapRect( node.Nodes[0], width, height )
				?? AllocateLightmapRect( node.Nodes[1], width, height );

			node.MaxW = Math.Max( node.Nodes[0].MaxW, node.Nodes[1].MaxW );
			node.MaxH = Math.Max( node.Nodes[0].MaxH, node.Nodes[1].MaxH );
			return ret;
		}

		if ( node.Filled ) return null;

		if ( node.Width < width || node.Height < height ) return null;

		if ( node.Width == width && node.Height == height )
		{
			node.Filled = true;
			node.MaxW = 0;
			node.MaxH = 0;
			return node;
		}

		var nodes = new LightmapNode[2];
		if ( (node.Width - width) > (node.Height - height) )
		{
			nodes[0] = new LightmapNode
			{
				X = node.X,
				Y = node.Y,
				Width = width,
				Height = node.Height,
				MaxW = width,
				MaxH = node.Height
			};

			nodes[1] = new LightmapNode
			{
				X = node.X + width,
				Y = node.Y,
				Width = node.Width - width,
				Height = node.Height,
				MaxW = node.Width - width,
				MaxH = node.Height
			};
		}
		else
		{
			nodes[0] = new LightmapNode
			{
				X = node.X,
				Y = node.Y,
				Width = node.Width,
				Height = height,
				MaxW = node.Width,
				MaxH = height
			};

			nodes[1] = new LightmapNode
			{
				X = node.X,
				Y = node.Y + height,
				Width = node.Width,
				Height = node.Height - height,
				MaxW = node.Width,
				MaxH = node.Height - height
			};
		}

		node.Nodes = nodes;
		var result = AllocateLightmapRect( node.Nodes[0], width, height );

		node.MaxW = Math.Max( node.Nodes[0].MaxW, node.Nodes[1].MaxW );
		node.MaxH = Math.Max( node.Nodes[0].MaxH, node.Nodes[1].MaxH );
		return result;
	}

	[StructLayout( LayoutKind.Sequential )]
	public struct MapVertex
	{
		public MapVertex( Vector3 position, Vector3 normal, Vector3 tangent, Vector2 texcoord, Vector2 texcoord2 )
		{
			this.position = position;
			this.normal = normal;
			this.tangent = tangent;
			this.texcoord = texcoord;
			this.texcoord2 = texcoord2;
		}

		public Vector3 position;
		public Vector3 normal;
		public Vector3 tangent;
		public Vector2 texcoord;
		public Vector2 texcoord2;

		public int LightStyle0;
		public int LightStyle1;
		public int LightStyle2;
		public int LightStyle3;

		public static readonly VertexAttribute[] Layout =
		{
			new ( VertexAttributeType.Position, VertexAttributeFormat.Float32, 3 ),
			new ( VertexAttributeType.Normal, VertexAttributeFormat.Float32, 3 ),
			new ( VertexAttributeType.Tangent, VertexAttributeFormat.Float32, 3 ),
			new ( VertexAttributeType.TexCoord, VertexAttributeFormat.Float32, 2, 0 ),
			new ( VertexAttributeType.TexCoord, VertexAttributeFormat.Float32, 2, 1 ),
			new ( VertexAttributeType.BlendIndices, VertexAttributeFormat.UInt32, 4, 10 ),
		};
	}

	public static readonly string[] DefaultLightstyles =
	[
		// 0 normal
		"m",
		// 1 FLICKER (first variety)
		"mmnmmommommnonmmonqnmmo",
		// 2 SLOW STRONG PULSE
		"abcdefghijklmnopqrstuvwxyzyxwvutsrqponmlkjihgfedcba",
		// 3 CANDLE (first variety)
		"mmmmmaaaaammmmmaaaaaabcdefgabcdefg",
		// 4 FAST STROBE
		"mamamamamama",
		// 5 GENTLE PULSE 1
		"jklmnopqrstuvwxyzyxwvutsrqponmlkj",
		// 6 FLICKER (second variety)
		"nmonqnmomnmomomno",
		// 7 CANDLE (second variety)
		"mmmaaaabcdefgmmmmaaaammmaamm",
		// 8 CANDLE (third variety)
		"mmmaaammmaaammmabcdefaaaammmmabcdefmmmaaaa",
		// 9 SLOW STROBE (fourth variety)
		"aaaaaaaazzzzzzzz",
		// 10 FLUORESCENT FLICKER
		"mmamammmmammamamaaamammma",
		// 11 SLOW PULSE NOT FADE TO BLACK
		"abcdefghijklmnopqrrqponmlkjihgfedcba",
	];
}
