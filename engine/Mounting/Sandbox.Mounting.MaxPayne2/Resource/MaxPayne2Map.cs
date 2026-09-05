using Sandbox;
using Sandbox.Mounting;
using RasLib;
using System;
using System.Collections.Generic;

partial class MaxPayne2Map( string fileName ) : SceneLoader<MaxPayne2Mount>
{
	public string FileName { get; set; } = fileName;

	const float Scale = 39.37f; // meters -> inches

	Ldb2File _ldb;
	readonly Dictionary<int, Texture> _diffuseCache = [];
	readonly Dictionary<int, Texture> _lightmapCache = [];

	protected override void BuildScene()
	{
		var data = Host.GetFileBytes( FileName );
		if ( data is null )
			return;

		_ldb = Ldb2File.Parse( data );

		var world = new GameObject( true, "worldspawn" );

		foreach ( var room in _ldb.Rooms )
		{
			var model = BuildMeshListModel( room.Name, room.Meshes, room.Transform );
			if ( model is null )
				continue;

			SpawnModel( world, room.Name, model, Transform.Zero );
		}

		SpawnDynamicMeshes( world );
		SpawnLights( world );
	}

	void SpawnDynamicMeshes( GameObject world )
	{
		var modelCache = new Dictionary<List<Ldb2File.SubMesh>, Model>();

		for ( var i = 0; i < _ldb.DynamicMeshes.Count; i++ )
		{
			var dyn = _ldb.DynamicMeshes[i];
			if ( dyn.Meshes is null || dyn.Meshes.Count == 0 )
				continue;

			if ( dyn.FsmId < 0 || dyn.FsmId >= _ldb.Fsms.Count )
				continue;

			if ( !modelCache.TryGetValue( dyn.Meshes, out var model ) )
			{
				model = BuildMeshListModel( $"dynamic_{i}", dyn.Meshes, Mat43.Identity );
				modelCache[dyn.Meshes] = model;
			}

			if ( model is null )
				continue;

			// geometry is centered on itself; pivot = center offset from the FSM origin
			// (door hinge) in FSM-LOCAL axes, so it must rotate with the basis
			var placement = _ldb.Fsms[dyn.FsmId].Transform;
			placement.Translation = placement.Point( dyn.AabbPivot );

			SpawnModel( world, $"dynamic_{i}", model, placement.ToSbox( Scale ) );
		}
	}

	void SpawnLights( GameObject world )
	{
		foreach ( var light in _ldb.Lights )
		{
			var go = new GameObject( true, "light" );
			go.SetParent( world );
			go.WorldTransform = light.Transform.ToSbox( Scale );

			var point = go.AddComponent<PointLight>();
			point.LightColor = new Color( light.R / 255f, light.G / 255f, light.B / 255f, 1f ) * (light.A / 255f);
			point.Radius = light.Falloff * Scale;
		}

		// flares mark the visible lamp props; their illumination is baked into lightmaps,
		// so give each a real light to shine on characters and props
		foreach ( var flare in _ldb.Flares )
		{
			var go = new GameObject( true, "flare_light" );
			go.SetParent( world );
			go.WorldTransform = flare.ToSbox( Scale );

			var point = go.AddComponent<PointLight>();
			point.LightColor = new Color( 1f, 0.92f, 0.78f, 1f );
			point.Radius = 350f;
			point.Shadows = false;
		}

		float ambR = 0, ambG = 0, ambB = 0, ambWeight = 0;

		foreach ( var room in _ldb.Rooms )
		{
			foreach ( var vol in room.VolumeLights )
			{
				var center = room.Transform.Point( (vol.Min + vol.Max) * 0.5f );
				var size = (vol.Max - vol.Min) * Scale;

				var go = new GameObject( true, $"ambient_{room.Name}" );
				go.SetParent( world );
				go.WorldPosition = new Vector3( -center.x, -center.z, center.y ) * Scale;

				// x2 compensates for point falloff standing in for a uniform ambient grid
				var point = go.AddComponent<PointLight>();
				point.LightColor = new Color( MathF.Min( vol.R * 2f, 1f ), MathF.Min( vol.G * 2f, 1f ), MathF.Min( vol.B * 2f, 1f ), 1f );
				point.Radius = MathF.Max( size.Length, 256f );
				point.Shadows = false;

				var weight = MathF.Max( size.Length, 1f );
				ambR += vol.R * weight; ambG += vol.G * weight; ambB += vol.B * weight;
				ambWeight += weight;
			}
		}

		// distance-independent floor so characters stay visible everywhere in the level
		if ( ambWeight > 0 )
		{
			var go = new GameObject( true, "mp2_ambient" );
			go.SetParent( world );
			go.AddComponent<AmbientLight>().Color = new Color(
				MathF.Min( ambR / ambWeight, 1f ),
				MathF.Min( ambG / ambWeight, 1f ),
				MathF.Min( ambB / ambWeight, 1f ), 1f );
		}
	}

	static void SpawnModel( GameObject world, string name, Model model, Transform transform )
	{
		var go = new GameObject( true, name );
		go.SetParent( world );
		go.WorldTransform = transform;
		go.AddComponent<ModelRenderer>().Model = model;

		var collider = go.AddComponent<ModelCollider>();
		collider.Model = model;
		collider.Static = true;
	}

	Model BuildMeshListModel( string name, List<Ldb2File.SubMesh> meshes, Mat43 transform )
	{
		var builder = Model.Builder.WithName( $"{Path}#{name}" );
		var collisionVerts = new List<Vector3>();
		var collisionIndices = new List<int>();
		var any = false;

		foreach ( var sub in meshes )
		{
			if ( sub.Positions.Length == 0 || sub.Indices.Length == 0 )
				continue;

			var mesh = BuildSubMesh( sub, transform, out var positions, out var indices );
			if ( mesh is null )
				continue;

			var baseIndex = collisionVerts.Count;
			collisionVerts.AddRange( positions );
			for ( var i = 0; i < indices.Length; i++ )
				collisionIndices.Add( baseIndex + indices[i] );

			builder.AddMesh( mesh );
			any = true;
		}

		if ( !any )
			return null;

		builder.AddCollisionMesh( collisionVerts, collisionIndices );
		builder.AddTraceMesh( collisionVerts, collisionIndices );
		return builder.Create();
	}

	Mesh BuildSubMesh( Ldb2File.SubMesh sub, Mat43 roomToWorld, out Vector3[] positions, out int[] indices )
	{
		var count = sub.Positions.Length;
		positions = new Vector3[count];
		var normals = new Vector3[count];
		for ( var i = 0; i < count; i++ )
		{
			var p = roomToWorld.Point( sub.Positions[i] );
			var n = roomToWorld.Direction( sub.Normals[i] );
			positions[i] = new Vector3( -p.x, -p.z, p.y ) * Scale;
			normals[i] = new Vector3( -n.x, -n.z, n.y ).Normal;
		}

		var matDef = sub.MaterialId >= 0 && sub.MaterialId < _ldb.Materials.Count ? _ldb.Materials[sub.MaterialId] : null;
		var dualSided = matDef?.DualSided ?? false;

		// coplanar layers: MP2 orders them by sortPriority with decals not writing z;
		// nudge along the normal so they separate in a plain z-buffered renderer
		var nudge = (matDef is { WritesZBuffer: false } ? 0.6f : 0f) + (matDef?.SortPriority ?? 0) * 0.3f;
		if ( nudge > 0f )
		{
			for ( var i = 0; i < count; i++ )
				positions[i] += normals[i] * nudge;
		}

		var tris = sub.Indices.Length;
		indices = new int[dualSided ? tris * 2 : tris];
		for ( var i = 0; i < tris; i += 3 )
		{
			indices[i] = sub.Indices[i];
			indices[i + 1] = sub.Indices[i + 2];
			indices[i + 2] = sub.Indices[i + 1];
		}

		if ( dualSided )
		{
			for ( var i = 0; i < tris; i++ )
				indices[tris + i] = sub.Indices[i];
		}

		var lightmap = sub.LightmapUvs is not null && matDef is not null ? ResolveLightmap( matDef.LightmapId ) : null;

		var usesAlpha = matDef?.UsesAlpha ?? false;

		Mesh mesh;
		if ( lightmap is not null )
		{
			var material = Material.Create( "mp2_world", "shaders/mp2_world.shader" );
			material?.Set( "g_tColor", ResolveDiffuse( sub.MaterialId ) ?? Texture.White );
			material?.Set( "g_tLightmap", lightmap );
			if ( usesAlpha ) material?.SetFeature( "F_ALPHA_TEST", 1 );

			mesh = new Mesh( material );
			var vertices = new MapVertex[count];
			for ( var i = 0; i < count; i++ )
				vertices[i] = new MapVertex( positions[i], normals[i], sub.Uvs[i], sub.LightmapUvs[i] );
			mesh.CreateVertexBuffer( count, vertices );
		}
		else
		{
			var material = Material.Create( "mp2_model", "shaders/mp2_model.shader" );
			material?.Set( "g_tColor", ResolveDiffuse( sub.MaterialId ) ?? Texture.White );
			if ( usesAlpha ) material?.SetFeature( "F_ALPHA_TEST", 1 );

			mesh = new Mesh( material );
			var vertices = new SimpleVertex[count];
			for ( var i = 0; i < count; i++ )
				vertices[i] = new SimpleVertex( positions[i], normals[i], Vector3.Zero, sub.Uvs[i] );
			mesh.CreateVertexBuffer( count, vertices );
		}

		mesh.CreateIndexBuffer( indices.Length, indices );
		mesh.Bounds = BBox.FromPoints( positions );
		return mesh;
	}

	Texture ResolveLightmap( int lightmapId )
	{
		if ( lightmapId < 0 || lightmapId >= _ldb.Lightmaps.Count )
			return null;

		if ( _lightmapCache.TryGetValue( lightmapId, out var cached ) )
			return cached;

		var tex = MaxPayneImage.Load( _ldb.Lightmaps[lightmapId].Data );
		_lightmapCache[lightmapId] = tex;
		return tex;
	}

	Texture ResolveDiffuse( int materialId )
	{
		if ( materialId < 0 || materialId >= _ldb.Materials.Count )
			return null;

		var mat = _ldb.Materials[materialId];
		var index = mat.VisibleFrame >= 0 ? mat.FrameStart + mat.VisibleFrame : mat.FrameStart;
		if ( index < 0 || index >= _ldb.Diffuse.Count )
			return null;

		if ( _diffuseCache.TryGetValue( index, out var cached ) )
			return cached;

		var tex = MaxPayneImage.Load( _ldb.Diffuse[index].Data );
		_diffuseCache[index] = tex;
		return tex;
	}
}

file struct MapVertex( Vector3 position, Vector3 normal, Vector2 texcoord, Vector2 lightmapUv )
{
	[VertexLayout.Position]
	public Vector3 Position = position;

	[VertexLayout.Normal]
	public Vector3 Normal = normal;

	[VertexLayout.TexCoord]
	public Vector2 Texcoord = texcoord;

	[VertexLayout.TexCoord( 1 )]
	public Vector2 LightmapUv = lightmapUv;
}
