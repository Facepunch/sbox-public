using System;
using System.IO;
using GltfImage = SharpGLTF.Schema2.Image;
using GltfModelRoot = SharpGLTF.Schema2.ModelRoot;
using GltfNode = SharpGLTF.Schema2.Node;
using GltfPrimitiveType = SharpGLTF.Schema2.PrimitiveType;

namespace Editor;

/// <summary>
/// Imports glTF/GLB files into s&amp;box as .vmdl models using the native CModelMesh/CModelDoc pipeline.
/// Follows the same pattern as FBX import - produces a .vmdl file that gets compiled by the asset system.
/// </summary>
public static class GltfImporter
{
	private const float MetersToInches = 39.3701f;

	/// <summary>
	/// Import a glTF/GLB file and create a .vmdl model asset.
	/// </summary>
	public static Asset ImportToVmdl( string gltfAbsolutePath, string targetVmdlPath = null )
	{
		if ( string.IsNullOrWhiteSpace( gltfAbsolutePath ) )
			return null;

		if ( !File.Exists( gltfAbsolutePath ) )
		{
			Log.Warning( $"GltfImporter: File not found: {gltfAbsolutePath}" );
			return null;
		}

		var modelFilename = targetVmdlPath ?? Path.ChangeExtension( gltfAbsolutePath, ".vmdl" );

		if ( File.Exists( modelFilename ) )
			return null;

		if ( !g_pToolFramework2.InitEngineTool( "modeldoc_editor" ) )
			return null;

		GltfModelRoot gltfModel;
		try
		{
			gltfModel = GltfModelRoot.Load( gltfAbsolutePath );
		}
		catch ( Exception ex )
		{
			Log.Warning( $"GltfImporter: Failed to parse glTF: {ex.Message}" );
			return null;
		}

		var textureDir = Path.GetDirectoryName( gltfAbsolutePath );
		var modelName = SanitizeFilename( Path.GetFileNameWithoutExtension( gltfAbsolutePath ) );
		ExtractTextures( gltfModel, textureDir, modelName );

		var materialPaths = CreateMaterials( gltfModel, textureDir, modelName );

		CModelMesh? nativeMesh;
		try
		{
			nativeMesh = ConvertSceneToMesh( gltfModel, materialPaths );
		}
		catch ( Exception ex )
		{
			Log.Warning( $"GltfImporter: Failed to convert meshes: {ex.Message}" );
			return null;
		}

		if ( !nativeMesh.HasValue )
		{
			Log.Warning( $"GltfImporter: No meshes found in {gltfAbsolutePath}" );
			return null;
		}

		var meshValue = nativeMesh.Value;

		bool success;
		try
		{
			unsafe
			{
				success = NativeEngine.ModelDoc.CreateModelFromMesh( modelFilename, meshValue );
			}
		}
		catch ( Exception ex )
		{
			Log.Warning( $"GltfImporter: Native CreateModelFromMesh failed: {ex.Message}" );
			success = false;
		}
		finally
		{
			meshValue.DeleteThis();
		}

		if ( !success )
		{
			Log.Warning( $"GltfImporter: Failed to create .vmdl from {gltfAbsolutePath}" );
			return null;
		}

		var asset = AssetSystem.RegisterFile( modelFilename );
		if ( asset is null )
			return null;

		asset.Compile( true );
		return asset;
	}

	/// <summary>
	/// Import a glTF/GLB file as a runtime Model for preview thumbnails.
	/// Walks the scene node tree and applies world transforms to each mesh.
	/// </summary>
	public static Model ImportToRuntimeModel( string gltfAbsolutePath )
	{
		if ( !File.Exists( gltfAbsolutePath ) )
			return null;

		GltfModelRoot gltfModel;
		try
		{
			gltfModel = GltfModelRoot.Load( gltfAbsolutePath );
		}
		catch ( Exception ex )
		{
			Log.Warning( $"GltfImporter: Failed to parse glTF for preview: {ex.Message}" );
			return null;
		}

		var builder = Model.Builder.WithName( Path.GetFileNameWithoutExtension( gltfAbsolutePath ) );
		var defaultMaterial = Material.Load( "materials/dev/primary_white.vmat" );

		var scene = gltfModel.DefaultScene;
		if ( scene is null && gltfModel.LogicalScenes.Count > 0 )
			scene = gltfModel.LogicalScenes[0];
		if ( scene is null )
			return null;

		foreach ( var node in scene.VisualChildren )
			BuildRuntimeNode( node, System.Numerics.Matrix4x4.Identity, builder, defaultMaterial );

		return builder.Create();
	}

	private static void BuildRuntimeNode( GltfNode node, System.Numerics.Matrix4x4 parentTransform, ModelBuilder builder, Material defaultMaterial )
	{
		var worldTransform = node.LocalMatrix * parentTransform;

		if ( node.Mesh is not null )
		{
			foreach ( var primitive in node.Mesh.Primitives )
			{
				if ( primitive.DrawPrimitiveType != GltfPrimitiveType.TRIANGLES &&
					primitive.DrawPrimitiveType != GltfPrimitiveType.TRIANGLE_STRIP &&
					primitive.DrawPrimitiveType != GltfPrimitiveType.TRIANGLE_FAN )
					continue;

				var posAccessor = primitive.GetVertexAccessor( "POSITION" );
				if ( posAccessor is null )
					continue;

				var positions = posAccessor.AsVector3Array();
				var normals = primitive.GetVertexAccessor( "NORMAL" )?.AsVector3Array();
				var texcoords = primitive.GetVertexAccessor( "TEXCOORD_0" )?.AsVector2Array();

				var vertexCount = positions.Count;
				var vertices = new Vertex[vertexCount];

				for ( int i = 0; i < vertexCount; i++ )
				{
					var pos = System.Numerics.Vector3.Transform( positions[i], worldTransform );
					vertices[i].Position = ConvertPosition( pos );

					if ( normals is not null && i < normals.Count )
					{
						var n = System.Numerics.Vector3.TransformNormal( normals[i], worldTransform );
						n = System.Numerics.Vector3.Normalize( n );
						vertices[i].Normal = ConvertDirection( n );
					}
					else
						vertices[i].Normal = Vector3.Up;

					if ( texcoords is not null && i < texcoords.Count )
					{
						var uv = texcoords[i];
						vertices[i].TexCoord0 = new Vector4( uv.X, uv.Y, 0, 0 );
					}

					vertices[i].Color = new Color32( 255, 255, 255, 255 );
				}

				var triangleIndices = primitive.GetTriangleIndices().ToList();
				var indices = new List<int>();
				foreach ( var (a, b, c) in triangleIndices )
				{
					indices.Add( a );
					indices.Add( b );
					indices.Add( c );
				}

				var bounds = new BBox();
				for ( int i = 0; i < vertexCount; i++ )
					bounds = bounds.AddPoint( vertices[i].Position );

				var mesh = new Mesh( defaultMaterial );
				mesh.CreateVertexBuffer<Vertex>( vertexCount, vertices );
				mesh.CreateIndexBuffer( indices.Count, indices );
				mesh.Bounds = bounds;

				builder.AddMesh( mesh );
			}
		}

		foreach ( var child in node.VisualChildren )
			BuildRuntimeNode( child, worldTransform, builder, defaultMaterial );
	}

	// ─── Native CModelMesh pipeline (for .vmdl creation) ───

	private static unsafe CModelMesh? ConvertSceneToMesh( GltfModelRoot gltfModel, Dictionary<int, string> materialPaths )
	{
		var allPositions = new List<Vector3>();
		var allNormals = new List<Vector3>();
		var allUVs = new List<Vector2>();
		var primData = new List<(int groupIndex, List<(int a, int b, int c)> tris)>();
		var faceGroupMaterials = new List<string>();

		var scene = gltfModel.DefaultScene;
		if ( scene is null && gltfModel.LogicalScenes.Count > 0 )
			scene = gltfModel.LogicalScenes[0];
		if ( scene is null )
			return null;

		foreach ( var node in scene.VisualChildren )
			CollectNodeMeshData( node, System.Numerics.Matrix4x4.Identity, allPositions, allNormals, allUVs, primData, faceGroupMaterials, materialPaths );

		if ( allPositions.Count == 0 )
			return null;

		var mesh = CModelMesh.Create();
		mesh.AddVertices( allPositions.Count );

		var posArray = allPositions.ToArray();
		fixed ( Vector3* pPositions = posArray )
			mesh.SetPositions( (IntPtr)pPositions, posArray.Length );

		foreach ( var mat in faceGroupMaterials )
			mesh.AddFaceGroup( mat );

		var fvNormals = new List<Vector3>();
		var fvUVs = new List<Vector2>();

		foreach ( var (groupIndex, tris) in primData )
		{
			foreach ( var (a, b, c) in tris )
			{
				fvNormals.Add( allNormals[a] );
				fvNormals.Add( allNormals[b] );
				fvNormals.Add( allNormals[c] );
				fvUVs.Add( allUVs[a] );
				fvUVs.Add( allUVs[b] );
				fvUVs.Add( allUVs[c] );

				var faceIndices = new int[] { a, b, c };
				fixed ( int* pIndices = faceIndices )
					mesh.AddFace( groupIndex, (IntPtr)pIndices, 3 );
			}
		}

		var normArray = fvNormals.ToArray();
		var uvArray = fvUVs.ToArray();

		fixed ( Vector3* pNormals = normArray )
			mesh.SetNormals( (IntPtr)pNormals, normArray.Length );

		fixed ( Vector2* pUVs = uvArray )
			mesh.SetTexCoords( (IntPtr)pUVs, uvArray.Length );

		return mesh;
	}

	private static void CollectNodeMeshData(
		GltfNode node,
		System.Numerics.Matrix4x4 parentTransform,
		List<Vector3> allPositions,
		List<Vector3> allNormals,
		List<Vector2> allUVs,
		List<(int groupIndex, List<(int a, int b, int c)> tris)> primData,
		List<string> faceGroupMaterials,
		Dictionary<int, string> materialPaths )
	{
		var worldTransform = node.LocalMatrix * parentTransform;

		if ( node.Mesh is not null )
		{
			foreach ( var primitive in node.Mesh.Primitives )
			{
				if ( primitive.DrawPrimitiveType != GltfPrimitiveType.TRIANGLES &&
					primitive.DrawPrimitiveType != GltfPrimitiveType.TRIANGLE_STRIP &&
					primitive.DrawPrimitiveType != GltfPrimitiveType.TRIANGLE_FAN )
					continue;

				var posAccessor = primitive.GetVertexAccessor( "POSITION" );
				if ( posAccessor is null )
					continue;

				var positions = posAccessor.AsVector3Array();
				var normals = primitive.GetVertexAccessor( "NORMAL" )?.AsVector3Array();
				var texcoords = primitive.GetVertexAccessor( "TEXCOORD_0" )?.AsVector2Array();

				var triangleIndices = primitive.GetTriangleIndices().ToList();
				if ( triangleIndices.Count == 0 )
					continue;

				int vertexOffset = allPositions.Count;

				for ( int i = 0; i < positions.Count; i++ )
				{
					var pos = System.Numerics.Vector3.Transform( positions[i], worldTransform );
					allPositions.Add( ConvertPosition( pos ) );

					if ( normals is not null && i < normals.Count )
					{
						var n = System.Numerics.Vector3.TransformNormal( normals[i], worldTransform );
						allNormals.Add( ConvertDirection( System.Numerics.Vector3.Normalize( n ) ) );
					}
					else
						allNormals.Add( Vector3.Up );

					allUVs.Add( texcoords is not null && i < texcoords.Count
						? new Vector2( texcoords[i].X, texcoords[i].Y )
						: Vector2.Zero );
				}

				var matIndex = primitive.Material?.LogicalIndex ?? -1;
				string matPath = materialPaths.TryGetValue( matIndex, out var mp )
					? mp
					: "materials/dev/primary_white.vmat";

				int groupIndex = faceGroupMaterials.IndexOf( matPath );
				if ( groupIndex < 0 )
				{
					groupIndex = faceGroupMaterials.Count;
					faceGroupMaterials.Add( matPath );
				}

				var offsetTris = triangleIndices
					.Select( t => (t.A + vertexOffset, t.B + vertexOffset, t.C + vertexOffset) )
					.ToList();
				primData.Add( (groupIndex, offsetTris) );
			}
		}

		foreach ( var child in node.VisualChildren )
			CollectNodeMeshData( child, worldTransform, allPositions, allNormals, allUVs, primData, faceGroupMaterials, materialPaths );
	}

	// ─── Textures & Materials ───

	private static void ExtractTextures( GltfModelRoot gltfModel, string outputDir, string modelName )
	{
		var texDir = Path.Combine( outputDir, "materials", modelName );
		Directory.CreateDirectory( texDir );

		foreach ( var image in gltfModel.LogicalImages )
		{
			var imageContent = image.Content;
			if ( imageContent.Content.Length == 0 )
				continue;

			var extension = GetImageExtension( imageContent.MimeType );
			var imageName = GetImageName( image );
			var imagePath = Path.Combine( texDir, imageName + extension );

			if ( !File.Exists( imagePath ) )
			{
				try
				{
					File.WriteAllBytes( imagePath, imageContent.Content.ToArray() );
				}
				catch ( Exception ex )
				{
					Log.Warning( $"GltfImporter: Failed to extract texture {imageName}: {ex.Message}" );
				}
			}
		}
	}

	private static Dictionary<int, string> CreateMaterials( GltfModelRoot gltfModel, string outputDir, string modelName )
	{
		var materialPaths = new Dictionary<int, string>();
		var matDir = Path.Combine( outputDir, "materials", modelName );
		Directory.CreateDirectory( matDir );

		foreach ( var gltfMat in gltfModel.LogicalMaterials )
		{
			var matName = !string.IsNullOrWhiteSpace( gltfMat.Name )
				? SanitizeFilename( gltfMat.Name )
				: $"material_{gltfMat.LogicalIndex}";

			var vmatAbsPath = Path.Combine( matDir, matName + ".vmat" );

			// Defaults
			string texColor = "[1.000000 1.000000 1.000000 1.000000]";
			string texNormal = "[0.501961 0.501961 1.000000 0.000000]";
			string texRough = "[0.500000 0.500000 0.500000 0.000000]";
			string texMetallic = "[0.000000 0.000000 0.000000 0.000000]";
			var features = new List<string>();

			// Base Color
			var baseColorChannel = gltfMat.FindChannel( "BaseColor" );
			if ( baseColorChannel.HasValue )
			{
				var ch = baseColorChannel.Value;
				if ( ch.Texture is not null )
					texColor = GetTextureRelativePath( ch.Texture.PrimaryImage, matDir );
				else
				{
					var c = ch.Color;
					texColor = $"[{c.X:F6} {c.Y:F6} {c.Z:F6} {c.W:F6}]";
				}
			}

			// Normal Map
			var normalChannel = gltfMat.FindChannel( "Normal" );
			if ( normalChannel.HasValue && normalChannel.Value.Texture is not null )
				texNormal = GetTextureRelativePath( normalChannel.Value.Texture.PrimaryImage, matDir );

			// Metallic-Roughness
			var mrChannel = gltfMat.FindChannel( "MetallicRoughness" );
			if ( mrChannel.HasValue )
			{
				var ch = mrChannel.Value;
				if ( ch.Texture is not null )
				{
					var mrPath = GetTextureRelativePath( ch.Texture.PrimaryImage, matDir );
					texRough = mrPath;
					texMetallic = mrPath;
					features.Add( "F_METALNESS_TEXTURE 1" );
					features.Add( "F_SPECULAR 1" );
				}
				else
				{
					try { texMetallic = FormatScalar( ch.GetFactor( "MetallicFactor" ) ); } catch { }
					try { texRough = FormatScalar( ch.GetFactor( "RoughnessFactor" ) ); } catch { }
				}
			}

			// Alpha Mode
			if ( gltfMat.Alpha == SharpGLTF.Schema2.AlphaMode.BLEND )
				features.Add( "F_TRANSLUCENT 1" );
			else if ( gltfMat.Alpha == SharpGLTF.Schema2.AlphaMode.MASK )
			{
				features.Add( "F_ALPHA_TEST 1" );
				features.Add( $"g_flAlphaTestReference {gltfMat.AlphaCutoff:F2}" );
			}

			// Double-Sided
			if ( gltfMat.DoubleSided )
				features.Add( "F_RENDER_BACKFACES 1" );

			// Build .vmat content
			var featureLines = features.Count > 0
				? string.Join( "\n", features.Select( f => $"\t{f}" ) ) + "\n\n"
				: "";

			var vmatContent =
				$"Layer0\n{{\n" +
				$"\tshader \"shaders/complex.shader_c\"\n\n" +
				featureLines +
				$"\tTextureColor \"{texColor}\"\n" +
				$"\tTextureNormal \"{texNormal}\"\n" +
				$"\tTextureRoughness \"{texRough}\"\n" +
				$"\tTextureMetalness \"{texMetallic}\"\n" +
				$"}}\n";

			try
			{
				if ( !File.Exists( vmatAbsPath ) )
					File.WriteAllText( vmatAbsPath, vmatContent );

				var matAsset = AssetSystem.RegisterFile( vmatAbsPath );
				if ( matAsset is not null )
				{
					materialPaths[gltfMat.LogicalIndex] = matAsset.Path;
					matAsset.Compile( true );
				}
				else
					materialPaths[gltfMat.LogicalIndex] = "materials/dev/primary_white.vmat";
			}
			catch ( Exception ex )
			{
				Log.Warning( $"GltfImporter: Failed to write material {matName}: {ex.Message}" );
				materialPaths[gltfMat.LogicalIndex] = "materials/dev/primary_white.vmat";
			}
		}

		return materialPaths;
	}

	// ─── Helpers ───

	private static string GetTextureRelativePath( GltfImage image, string matDir )
	{
		if ( image is null )
			return "";

		var imageName = GetImageName( image );
		var extension = GetImageExtension( image.Content.MimeType );
		var absolutePath = Path.Combine( matDir, imageName + extension );

		var asset = AssetSystem.FindByPath( absolutePath ) ?? AssetSystem.RegisterFile( absolutePath );
		if ( asset is not null )
			return asset.RelativePath;

		return ToAssetRelativePath( absolutePath );
	}

	private static string GetImageName( GltfImage image )
	{
		return !string.IsNullOrWhiteSpace( image.Name )
			? SanitizeFilename( image.Name )
			: $"texture_{image.LogicalIndex}";
	}

	private static string GetImageExtension( string mimeType )
	{
		return mimeType switch
		{
			"image/png" => ".png",
			"image/jpeg" => ".jpg",
			"image/webp" => ".webp",
			_ => ".png"
		};
	}

	private static string ToAssetRelativePath( string absolutePath )
	{
		var projectRoot = Project.Current?.GetRootPath();
		if ( string.IsNullOrWhiteSpace( projectRoot ) )
			return Path.GetFileName( absolutePath );

		var relative = Path.GetRelativePath( projectRoot, absolutePath );
		return relative.Replace( '\\', '/' );
	}

	private static Vector3 ConvertPosition( System.Numerics.Vector3 v )
	{
		return new Vector3( v.X, -v.Z, v.Y ) * MetersToInches;
	}

	private static Vector3 ConvertDirection( System.Numerics.Vector3 v )
	{
		return new Vector3( v.X, -v.Z, v.Y );
	}

	private static string FormatScalar( float v )
	{
		return $"[{v:F6} {v:F6} {v:F6} 0.000000]";
	}

	private static string SanitizeFilename( string name )
	{
		foreach ( var c in Path.GetInvalidFileNameChars() )
			name = name.Replace( c, '_' );

		return name.Replace( ' ', '_' ).ToLowerInvariant();
	}
}
