
namespace SeriousEngine3;

public partial class ModelLoader( string name ) : ResourceLoader<GameMount>
{
	protected override object Load()
	{
		var meta = CTSEMeta.Read( Host.GetBytes( name ) );
		meta.Host = Host;

		var builder = Model.Builder.WithName( Path );
		var lod0Positions = new List<Vector3>();
		var lod0Indices = new List<int>();

		var renderMesh = meta.GetOrNull( "CRenderMesh" );

		if ( renderMesh != null )
		{
			var skeleton = meta.GetOrNull( "CSkeleton" );
			var bones = skeleton != null ? ReadBones( skeleton ) : [];

			var boneIndexByName = new Dictionary<string, byte>();
			for ( int i = 0; i < bones.Count; i++ )
				boneIndexByName.Add( bones[i].Name, (byte)i );

			bool isSkinned = bones.Count > 0;

			AppendRenderMesh( builder, meta, renderMesh, isSkinned, boneIndexByName, lod0Positions, lod0Indices, SE3Transform.Identity );

			if ( lod0Positions.Count > 0 && lod0Indices.Count >= 3 )
				builder.AddTraceMesh( lod0Positions.ToArray(), lod0Indices.ToArray() );

			AddBones( builder, bones );

			var transforms = BuildBoneTransforms( bones );

			var mechanism = meta.GetOrNull( "CMechanismTemplateHolder" );
			bool hasPrimitiveCollision = AddMechanismCollision( builder, mechanism, bones, transforms, isSkinned );

			if ( !hasPrimitiveCollision )
			{
				var collisionMesh = meta.GetOrNull( "CCollisionMesh" );
				if ( collisionMesh != null )
					AddCollision( builder, collisionMesh, lod0Positions );
			}
		}
		else
		{
			ComposeChildren( builder, meta, lod0Positions, lod0Indices, SE3Transform.Identity );

			if ( lod0Indices.Count >= 3 )
				builder.AddTraceMesh( lod0Positions.ToArray(), lod0Indices.ToArray() );
		}

		return builder.Create();
	}

	void ComposeChildren( ModelBuilder builder, CTSEMeta meta, List<Vector3> lod0Positions, List<int> lod0Indices, SE3Transform parent )
	{
		var holder = meta.GetOrNull( "CModelConfigChildrenHolder" );
		var children = holder?.GetOrNull( "1" );
		if ( children == null ) return;

		var noBones = new Dictionary<string, byte>();

		for ( int i = 0; i < children.Count; i++ )
		{
			var child = children[i];
			int modelPtr = child.GetOrNull( "4" )?.GetOrNull( "3" )?.AsPointer() ?? 0;
			if ( modelPtr <= 0 ) continue;

			var childPath = meta.GetExternalResourcePath( modelPtr );
			if ( string.IsNullOrEmpty( childPath ) ) continue;

			CTSEMeta childMeta;
			try
			{
				childMeta = CTSEMeta.Read( Host.GetBytes( childPath ) );
				childMeta.Host = Host;
			}
			catch
			{
				continue;
			}

			var transform = parent.Combine( ReadChildTransform( child ) );
			var childRender = childMeta.GetOrNull( "CRenderMesh" );

			if ( childRender != null )
				AppendRenderMesh( builder, childMeta, childRender, false, noBones, lod0Positions, lod0Indices, transform );
			else
				ComposeChildren( builder, childMeta, lod0Positions, lod0Indices, transform );
		}
	}

	static SE3Transform ReadChildTransform( MetaValue child )
	{
		try
		{
			var placement = child.GetOrNull( "3" );
			var rotation = placement?.GetOrNull( "1" )?.AsQuaternion() ?? Rotation.Identity;
			var translation = placement?.GetOrNull( "2" )?.AsVector3() ?? Vector3.Zero;
			var scale = child.GetOrNull( "4" )?.GetOrNull( "1" )?.AsVector3() ?? Vector3.One;
			return new SE3Transform( rotation, translation, scale );
		}
		catch
		{
			return SE3Transform.Identity;
		}
	}

	void AppendRenderMesh( ModelBuilder builder, CTSEMeta meta, MetaValue renderMesh, bool isSkinned, Dictionary<string, byte> boneIndexByName, List<Vector3> lod0Positions, List<int> lod0Indices, SE3Transform transform )
	{
		var modifierMap = BuildModifierMap( meta, out int defaultPresetPtr );
		var materialCache = new Dictionary<string, SurfaceMaterial>();
		var defaultMaterial = GetBaseMaterial( ShaderFlags.None );

		var lods = renderMesh["5"]; // CStaticArray<CRenderMeshLOD>
		for ( int lodIndex = 0; lodIndex < lods.Count; lodIndex++ )
		{
			var lodSurfaces = lods[lodIndex]["9"]; // CStaticArray<CRenderMeshSurface>

			var holders = renderMesh["12"]; // index buffer holders
			var indexGfx = holders[lodIndex]["1"].Deref();
			byte[] indexBytes = indexGfx?["2"].AsByteArray();

			var vertHolders = renderMesh["13"]; // vertex buffer holders
			var vertGfx = vertHolders[lodIndex]["1"].Deref();
			byte[] vertBytes = vertGfx?["2"].AsByteArray();

			if ( indexBytes == null || vertBytes == null ) continue;

			int indexCursor = 0;

			for ( int si = 0; si < lodSurfaces.Count; si++ )
			{
				var surface = lodSurfaces[si];
				int triangleCount = surface["2"].AsInt();
				int vertexCount = surface["3"].AsInt();
				int indexCount = triangleCount * 3;

				if ( triangleCount == 0 || vertexCount == 0 )
				{
					indexCursor += indexCount * 2;
					continue;
				}

				var surfaceMaterial = ResolveSurfaceMaterial( meta, surface, lodSurfaces, si, modifierMap, defaultPresetPtr, materialCache, defaultMaterial );
				var indices = ReadSurfaceIndices( indexBytes, ref indexCursor, indexCount );

				if ( surfaceMaterial.IsSkipped )
					continue;

				if ( isSkinned )
				{
					var vertices = ReadSkinnedSurfaceVertices( surface, vertBytes, vertexCount, boneIndexByName, surfaceMaterial.ColorUv, surfaceMaterial.Mapping, surfaceMaterial.NormalMapping, transform );
					var bounds = BBox.FromPoints( vertices.Select( x => x.position ) );
					var mesh = new Mesh( surfaceMaterial.Material ) { Bounds = bounds };
					mesh.CreateVertexBuffer( vertices.Count, vertices );
					mesh.CreateIndexBuffer( indices.Count, indices );
					builder.AddMesh( mesh, lodIndex );

					if ( lodIndex == 0 )
					{
						int baseForTrace = lod0Positions.Count;
						lod0Positions.AddRange( vertices.Select( x => x.position ) );
						lod0Indices.AddRange( indices.Select( x => x + baseForTrace ) );
					}
				}
				else
				{
					var vertices = ReadStaticSurfaceVertices( surface, vertBytes, vertexCount, surfaceMaterial.ColorUv, surfaceMaterial.Mapping, surfaceMaterial.NormalMapping, transform );
					var bounds = BBox.FromPoints( vertices.Select( x => x.position ) );
					var mesh = new Mesh( surfaceMaterial.Material ) { Bounds = bounds };
					mesh.CreateVertexBuffer( vertices.Count, vertices );
					mesh.CreateIndexBuffer( indices.Count, indices );
					builder.AddMesh( mesh, lodIndex );

					if ( lodIndex == 0 )
					{
						int baseForTrace = lod0Positions.Count;
						lod0Positions.AddRange( vertices.Select( x => x.position ) );
						lod0Indices.AddRange( indices.Select( x => x + baseForTrace ) );
					}
				}
			}
		}
	}
}

