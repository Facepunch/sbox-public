namespace Editor.Assets;

[AssetPreview( "vphys" )]
class PreviewPhysics( Asset asset ) : AssetPreview( asset )
{
	public override float PreviewWidgetCycleSpeed => 0.2f;
	public override bool IsAnimatedPreview => true;

	BBox _bounds;
	Material _material;

	public override async Task InitializeAsset()
	{
		await Task.Yield();

		var physics = PhysicsGroupDescription.Load( Asset.Path );
		if ( physics is null )
			return;

		_material = Material.Load( "materials/dev/debug_physics_wireframe.vmat" );

		using ( Scene.Push() )
		{
			PrimaryObject = new GameObject( true, "preview physics" );
			PrimaryObject.WorldTransform = Transform.Zero;
			_bounds = new BBox();

			foreach ( var part in physics.Parts )
			{
				foreach ( var mesh in part.Meshes )
				{
					var vertices = mesh.GetVertices();
					var indices = mesh.GetIndices();
					AddPart( part, vertices, indices );
				}

				foreach ( var hull in part.Hulls )
					AddLinePart( part, [.. hull.GetLines()] );

				foreach ( var sphere in part.Spheres )
				{
					sphere.GetOutline( out var verts, out var inds );
					AddPart( part, verts, inds, MeshPrimitiveType.Lines );
				}

				foreach ( var capsule in part.Capsules )
				{
					capsule.GetOutline( out var verts, out var inds );
					AddPart( part, verts, inds, MeshPrimitiveType.Lines );
				}
			}

			SceneSize = _bounds.Size;
			SceneCenter = _bounds.Center;
		}
	}

	void AddLinePart( PhysicsGroupDescription.BodyPart part, Line[] lines )
	{
		if ( lines.Length == 0 ) return;

		var verts = new Vector3[lines.Length * 2];
		var inds = new int[lines.Length * 2];

		for ( int i = 0; i < lines.Length; i++ )
		{
			verts[i * 2] = lines[i].Start;
			verts[i * 2 + 1] = lines[i].End;
			inds[i * 2] = i * 2;
			inds[i * 2 + 1] = i * 2 + 1;
		}

		AddPart( part, verts, inds, MeshPrimitiveType.Lines );
	}

	void AddPart( PhysicsGroupDescription.BodyPart part, Vector3[] vertices, int[] indices, MeshPrimitiveType primitiveType = MeshPrimitiveType.Triangles )
	{
		if ( vertices.Length == 0 || indices.Length == 0 ) return;

		var vb = new VertexBuffer();
		vb.Init( true );

		foreach ( var v in vertices )
		{
			vb.Add( new Vertex( v, Vector3.Up, Vector3.Zero, Vector4.Zero ) );
			_bounds = _bounds.AddPoint( v );
		}

		for ( int i = 0; i < indices.Length; i++ )
			vb.AddRawIndex( indices[i] );

		var rm = new Mesh( _material, primitiveType );
		rm.CreateBuffers( vb );

		var go = new GameObject( true, "physics" );
		go.Parent = PrimaryObject;
		go.LocalTransform = part.Transform;
		go.AddComponent<ModelRenderer>().Model = new ModelBuilder().AddMesh( rm ).Create();
	}
}
