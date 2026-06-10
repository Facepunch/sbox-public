using Sandbox;
using Sandbox.Mounting;
using RasLib;
using System;
using System.Collections.Generic;

class MaxPayne2Model( string fileName ) : ResourceLoader<MaxPayne2Mount>
{
	public string FileName { get; set; } = fileName;

	const float Scale = 39.37f; // meters -> inches

	protected override object Load()
	{
		var data = Host.GetFileBytes( FileName );
		if ( data is null )
			return null;

		var kf2 = Kf2File.Parse( data );
		if ( kf2.Primitives.Count == 0 )
			return null;

		var dir = System.IO.Path.GetDirectoryName( FileName )?.Replace( '\\', '/' );
		var searchDirs = BuildSearchDirs( dir, kf2.TextureDirs );
		var builder = Model.Builder.WithName( Path );
		var textureCache = new Dictionary<string, Texture>( StringComparer.OrdinalIgnoreCase );

		var rig = LoadRig( dir );
		var animatedNodes = rig is null && kf2.Animations.Count > 0 && kf2.Nodes.Count > 0;

		Dictionary<string, int> boneIndexByNode = null;
		if ( rig is not null ) AddRigBones( builder, rig );
		else if ( animatedNodes ) boneIndexByNode = AddBones( kf2, builder );

		var collisionVerts = new List<Vector3>();
		var collisionIndices = new List<int>();
		var any = false;
		foreach ( var prim in kf2.Primitives )
		{
			if ( prim.Positions.Length == 0 || prim.Indices.Length == 0 )
				continue;

			var positions = ConvertPositions( prim );
			var indices = BuildIndices( kf2, prim );

			var mesh = rig is not null
				? BuildRigMesh( kf2, prim, positions, indices, searchDirs, textureCache, rig )
				: animatedNodes
					? BuildSkinnedMesh( kf2, prim, positions, indices, searchDirs, textureCache, boneIndexByNode )
					: BuildMesh( kf2, prim, positions, indices, searchDirs, textureCache );

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

		if ( rig is not null ) AddRigAnimations( builder, rig );
		else if ( animatedNodes ) AddAnimations( kf2, builder );

		return builder.Create();
	}

	class Rig
	{
		public string[] BoneNames;
		public string[] ParentNames;
		public Transform[] BindLocals;
		public Vector3[] BindScales;
		public Kf2File.SkinData Skin;
		public string AnimFolder;
	}

	Rig LoadRig( string dir )
	{
		var skdData = Host.GetFileBytes( System.IO.Path.ChangeExtension( FileName, ".skd" )?.Replace( '\\', '/' ) );
		if ( skdData is null )
			return null;

		Kf2File skd;
		try { skd = Kf2File.Parse( skdData ); }
		catch { return null; }

		if ( skd.Skin?.BoneNames is not { Length: > 0 } boneNames )
			return null;

		// skins/<char>/<lod>.kf2 -> skins/<char>.txt declares Skeleton = "male"|"female"
		var skeletonName = "male";
		var txt = Host.GetFileBytes( $"{dir}.txt" );
		if ( txt is not null )
		{
			var match = System.Text.RegularExpressions.Regex.Match(
				System.Text.Encoding.Latin1.GetString( txt ), "Skeleton\\s*=\\s*\"([^\"]+)\"" );
			if ( match.Success ) skeletonName = match.Groups[1].Value.ToLowerInvariant();
		}

		var skelDir = $"data/database/skeletons/{skeletonName}";
		var skelData = Host.GetFileBytes( $"{skelDir}/skeleton_{skeletonName}.kf2" );
		if ( skelData is null )
			return null;

		Kf2File skeleton;
		try { skeleton = Kf2File.Parse( skelData ); }
		catch { return null; }

		var poseLocals = new Dictionary<string, Mat43>( StringComparer.OrdinalIgnoreCase );
		var poseData = Host.GetFileBytes( $"{skelDir}/pose_{skeletonName}.kf2" );
		if ( poseData is not null )
		{
			try
			{
				foreach ( var anim in Kf2File.Parse( poseData ).Animations )
				{
					if ( anim.Keys.Count > 0 )
						poseLocals[anim.NodeName] = anim.Keys[0].Local;
				}
			}
			catch { }
		}

		var boneSet = new HashSet<string>( boneNames, StringComparer.OrdinalIgnoreCase );
		var rig = new Rig
		{
			BoneNames = boneNames,
			ParentNames = new string[boneNames.Length],
			BindLocals = new Transform[boneNames.Length],
			BindScales = new Vector3[boneNames.Length],
			Skin = skd.Skin,
			AnimFolder = $"{skelDir}/anim/",
		};

		for ( var i = 0; i < boneNames.Length; i++ )
		{
			var node = skeleton.FindNode( boneNames[i] );
			if ( node is null )
			{
				rig.BindLocals[i] = Transform.Zero;
				rig.BindScales[i] = Vector3.One;
				continue;
			}

			// skeleton casing, not skd casing: MaxPayne skd says 'ForeArm', skeleton 'Forearm' - children
			// declare skeleton-cased parents and the engine must match them to the bone names
			rig.BoneNames[i] = node.Name;
			var local = poseLocals.TryGetValue( node.Name, out var pose ) ? pose : node.Local;
			rig.BindLocals[i] = local.ToSbox( Scale );
			rig.BindScales[i] = local.AxisScales;
			rig.ParentNames[i] = node.HasParent && !string.IsNullOrEmpty( node.Parent ) && boneSet.Contains( node.Parent )
				? node.Parent : null;
		}

		return rig;
	}

	// GoldSrc mount standard: Bone takes MODEL-SPACE transforms, AddFrame takes parent-local
	static void AddRigBones( ModelBuilder builder, Rig rig )
	{
		var count = rig.BoneNames.Length;
		var indexByName = new Dictionary<string, int>( count, StringComparer.OrdinalIgnoreCase );
		for ( var i = 0; i < count; i++ ) indexByName[rig.BoneNames[i]] = i;

		var worlds = new Transform?[count];

		Transform WorldOf( int i )
		{
			if ( worlds[i] is { } cached ) return cached;
			var world = rig.ParentNames[i] is { } p && indexByName.TryGetValue( p, out var pi ) && pi != i
				? WorldOf( pi ).ToWorld( rig.BindLocals[i] )
				: rig.BindLocals[i];
			worlds[i] = world;
			return world;
		}

		var bones = new ModelBuilder.Bone[count];
		for ( var i = 0; i < count; i++ )
		{
			var world = WorldOf( i );
			bones[i] = new ModelBuilder.Bone( rig.BoneNames[i], rig.ParentNames[i], world.Position, world.Rotation );
		}

		builder.AddBones( bones );
	}

	Mesh BuildRigMesh( Kf2File kf2, Kf2File.Primitive prim, Vector3[] positions, int[] indices, List<string> searchDirs, Dictionary<string, Texture> cache, Rig rig )
	{
		var material = CreateMaterial( kf2, prim, searchDirs, cache );
		var mesh = new Mesh( material );

		var skin = rig.Skin;
		var vertices = new SkinnedVertex[positions.Length];
		Span<int> bones = stackalloc int[4];
		Span<float> weights = stackalloc float[4];
		Span<byte> wb = stackalloc byte[4];

		for ( var i = 0; i < vertices.Length; i++ )
		{
			var n = prim.Normals is not null ? prim.Normals[i] : Vector3.Up;
			var uv = prim.Uvs is not null ? prim.Uvs[i] : Vector2.Zero;

			bones.Clear();
			weights.Clear();
			var used = 0;

			var global = prim.VertexStart + i;
			if ( global < skin.Offsets.Length )
			{
				var offset = skin.Offsets[global];
				var boneCount = skin.Counts[global];
				for ( var b = 0; b < boneCount && offset + b < skin.Weights.Length; b++ )
				{
					var bone = skin.BoneIndices[offset + b];
					var weight = skin.Weights[offset + b];
					if ( weight <= 0f || bone < 0 || bone > 255 ) continue;

					if ( used < 4 ) { bones[used] = bone; weights[used] = weight; used++; }
					else
					{
						var min = 0;
						for ( var k = 1; k < 4; k++ ) if ( weights[k] < weights[min] ) min = k;
						if ( weight > weights[min] ) { bones[min] = bone; weights[min] = weight; }
					}
				}
			}

			Color32 blendIndices, blendWeights;
			if ( used == 0 )
			{
				blendIndices = new Color32( 0, 0, 0, 0 );
				blendWeights = new Color32( 255, 0, 0, 0 );
			}
			else
			{
				float total = 0;
				for ( var k = 0; k < used; k++ ) total += weights[k];

				wb.Clear();
				var sum = 0;
				for ( var k = 0; k < used; k++ )
				{
					wb[k] = (byte)Math.Clamp( (int)MathF.Round( weights[k] / total * 255f ), 0, 255 );
					sum += wb[k];
				}

				// engine expects bytes summing exactly 255 (MD5 mount precedent) - dump the diff on the largest
				if ( sum != 255 )
				{
					var max = 0;
					for ( var k = 1; k < used; k++ ) if ( wb[k] > wb[max] ) max = k;
					wb[max] = (byte)(wb[max] + (255 - sum));
				}

				blendIndices = new Color32( (byte)bones[0], (byte)bones[1], (byte)bones[2], (byte)bones[3] );
				blendWeights = new Color32( wb[0], wb[1], wb[2], wb[3] );
			}

			vertices[i] = new SkinnedVertex(
				positions[i],
				new Vector3( -n.x, -n.z, n.y ),
				uv, blendIndices, blendWeights );
		}

		mesh.CreateVertexBuffer( vertices.Length, vertices );
		mesh.CreateIndexBuffer( indices.Length, indices );
		mesh.Bounds = BBox.FromPoints( positions );
		return mesh;
	}

	void AddRigAnimations( ModelBuilder builder, Rig rig )
	{
		var indexByName = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase );
		for ( var i = 0; i < rig.BoneNames.Length; i++ ) indexByName[rig.BoneNames[i]] = i;

		foreach ( var path in Host.FindFiles( rig.AnimFolder, ".kf2" ) )
		{
			if ( path.EndsWith( "_mov.kf2", StringComparison.OrdinalIgnoreCase ) )
				continue;

			var data = Host.GetFileBytes( path );
			if ( data is null ) continue;

			Kf2File animFile;
			try { animFile = Kf2File.Parse( data ); }
			catch { continue; }

			if ( animFile.Animations.Count == 0 ) continue;

			BakeClip( builder, System.IO.Path.GetFileNameWithoutExtension( path ), rig, indexByName, animFile.Animations );
		}
	}

	// anim tracks are local to the track's OWN parentName, which can differ from the skeleton
	// (AI_Shoot anims key clavicles to Neck, thighs to Spine). Rebuild worlds per frame through
	// the anim's parent graph, then re-localize to the skeleton hierarchy for the engine.
	void BakeClip( ModelBuilder builder, string name, Rig rig, Dictionary<string, int> indexByName, List<Kf2File.NodeAnim> anims )
	{
		var fps = 30f;
		var totalFrames = 0;
		var looping = false;
		var boneCount = rig.BoneNames.Length;

		var skelParent = new int[boneCount];
		for ( var i = 0; i < boneCount; i++ )
			skelParent[i] = rig.ParentNames[i] is not null && indexByName.TryGetValue( rig.ParentNames[i], out var sp ) ? sp : -1;

		var keysPerBone = new List<(int Frame, Transform Local)>[boneCount];
		var animParent = new int[boneCount];
		Array.Copy( skelParent, animParent, boneCount );

		foreach ( var a in anims )
		{
			if ( a.Fps > 0 ) fps = a.Fps;
			if ( a.TotalFrames > totalFrames ) totalFrames = a.TotalFrames;
			looping |= a.Looping;

			if ( !indexByName.TryGetValue( a.NodeName, out var bone ) || a.Keys.Count == 0 )
				continue;

			var keys = new List<(int Frame, Transform Local)>( a.Keys.Count );
			foreach ( var (frame, local) in a.Keys )
			{
				// MP2 animates faces via matrix scale (blinks, lips) - carried as scale relative to bind
				var t = local.ToSbox( Scale );
				t.Scale = RelativeScale( local.AxisScales, rig.BindScales[bone] );
				keys.Add( (frame, t) );
				if ( frame + 1 > totalFrames ) totalFrames = frame + 1;
			}
			keys.Sort( ( x, y ) => x.Frame.CompareTo( y.Frame ) );
			keysPerBone[bone] = keys;

			// empty parentName = "skeleton parent" (Sub_* face tracks), NOT root
			animParent[bone] = string.IsNullOrEmpty( a.ParentName )
				? skelParent[bone]
				: indexByName.TryGetValue( a.ParentName, out var ap ) ? ap : skelParent[bone];
		}

		if ( totalFrames <= 0 )
			return;

		var animation = builder.AddAnimation( name, fps ).WithLooping( looping );
		var frameTransforms = new Transform[boneCount];
		var locals = new Transform[boneCount];
		var worlds = new Transform?[boneCount];

		Transform WorldAt( int i, int f, int depth )
		{
			if ( worlds[i] is { } cached ) return cached;

			var local = locals[i];
			var parent = keysPerBone[i] is not null ? animParent[i] : skelParent[i];

			var world = parent < 0 || depth > 128
				? new Transform( local.Position, local.Rotation )
				: WorldAt( parent, f, depth + 1 ).ToWorld( new Transform( local.Position, local.Rotation ) );
			worlds[i] = world;
			return world;
		}

		for ( var f = 0; f < totalFrames; f++ )
		{
			Array.Clear( worlds, 0, worlds.Length );

			for ( var i = 0; i < boneCount; i++ )
				locals[i] = keysPerBone[i] is not null ? SampleLocal( keysPerBone[i], rig.BindLocals[i], f ) : rig.BindLocals[i];

			for ( var i = 0; i < boneCount; i++ )
			{
				var world = WorldAt( i, f, 0 );
				if ( skelParent[i] < 0 )
				{
					frameTransforms[i] = world;
				}
				else
				{
					var parent = WorldAt( skelParent[i], f, 0 );
					var invRot = parent.Rotation.Inverse;
					frameTransforms[i] = new Transform( invRot * (world.Position - parent.Position), invRot * world.Rotation );
				}

				frameTransforms[i].Scale = locals[i].Scale;
			}

			animation.AddFrame( frameTransforms.AsSpan() );
		}
	}

	static Vector3 RelativeScale( Vector3 key, Vector3 bind )
	{
		static float S( float k, float b ) => b > 0.0001f ? k / b : 1f;
		return new Vector3( S( key.x, bind.x ), S( key.y, bind.y ), S( key.z, bind.z ) );
	}

	static Vector3[] ConvertPositions( Kf2File.Primitive prim )
	{
		var positions = new Vector3[prim.Positions.Length];
		for ( var i = 0; i < positions.Length; i++ )
		{
			var p = prim.Positions[i];
			positions[i] = new Vector3( -p.x, -p.z, p.y ) * Scale;
		}

		return positions;
	}

	// GoldSrc mount standard: Bone takes MODEL-SPACE transforms, AddFrame takes parent-local
	static Dictionary<string, int> AddBones( Kf2File kf2, ModelBuilder builder )
	{
		var byName = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase );
		var bones = new ModelBuilder.Bone[kf2.Nodes.Count];

		for ( var i = 0; i < kf2.Nodes.Count; i++ )
		{
			var node = kf2.Nodes[i];
			var parent = node.HasParent ? kf2.FindNode( node.Parent ) : null;
			var world = kf2.WorldOf( node ).ToSbox( Scale );
			bones[i] = new ModelBuilder.Bone( node.Name, parent?.Name, world.Position, world.Rotation );
			byName[node.Name] = i;
		}

		builder.AddBones( bones );
		return byName;
	}

	void AddAnimations( Kf2File kf2, ModelBuilder builder )
	{
		var fps = 30f;
		var totalFrames = 0;
		var looping = false;
		var animByNode = new Dictionary<string, Kf2File.NodeAnim>( StringComparer.OrdinalIgnoreCase );

		foreach ( var a in kf2.Animations )
		{
			animByNode[a.NodeName] = a;
			if ( a.Fps > 0 ) fps = a.Fps;
			if ( a.TotalFrames > totalFrames ) totalFrames = a.TotalFrames;
			foreach ( var k in a.Keys )
				if ( k.Frame + 1 > totalFrames ) totalFrames = k.Frame + 1;
			looping |= a.Looping;
		}

		if ( totalFrames <= 0 )
			return;

		var nodeCount = kf2.Nodes.Count;
		var bindLocals = new Transform[nodeCount];
		var keysPerNode = new List<(int Frame, Transform Local)>[nodeCount];

		for ( var i = 0; i < nodeCount; i++ )
		{
			var node = kf2.Nodes[i];
			bindLocals[i] = node.Local.ToSbox( Scale );

			if ( !animByNode.TryGetValue( node.Name, out var anim ) || anim.Keys.Count == 0 )
				continue;

			var bindScales = node.Local.AxisScales;
			var keys = new List<(int Frame, Transform Local)>( anim.Keys.Count );
			foreach ( var (frame, local) in anim.Keys )
			{
				var t = local.ToSbox( Scale );
				t.Scale = RelativeScale( local.AxisScales, bindScales );
				keys.Add( (frame, t) );
			}
			keys.Sort( ( x, y ) => x.Frame.CompareTo( y.Frame ) );
			keysPerNode[i] = keys;
		}

		var animation = builder.AddAnimation( "scene", fps ).WithLooping( looping );

		var frameTransforms = new Transform[nodeCount];
		for ( var f = 0; f < totalFrames; f++ )
		{
			for ( var i = 0; i < nodeCount; i++ )
				frameTransforms[i] = SampleLocal( keysPerNode[i], bindLocals[i], f );
			animation.AddFrame( frameTransforms.AsSpan() );
		}
	}

	static Transform SampleLocal( List<(int Frame, Transform Local)> keys, Transform bind, int f )
	{
		if ( keys is null || keys.Count == 0 )
			return bind;

		if ( f <= keys[0].Frame )
			return keys[0].Local;

		for ( var i = 1; i < keys.Count; i++ )
		{
			if ( f == keys[i].Frame )
				return keys[i].Local;

			if ( f < keys[i].Frame )
			{
				var a = keys[i - 1];
				var b = keys[i];
				var t = (f - a.Frame) / (float)(b.Frame - a.Frame);
				return new Transform(
					Vector3.Lerp( a.Local.Position, b.Local.Position, t ),
					Rotation.Slerp( a.Local.Rotation, b.Local.Rotation, t ),
					Vector3.Lerp( a.Local.Scale, b.Local.Scale, t ) );
			}
		}

		return keys[^1].Local;
	}

	static List<string> BuildSearchDirs( string dir, string textureDirs )
	{
		var dirs = new List<string> { dir ?? "" };
		foreach ( var entry in (textureDirs ?? "").Split( ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ) )
		{
			var resolved = ResolveRelative( dir, entry );
			if ( !dirs.Exists( d => string.Equals( d, resolved, StringComparison.OrdinalIgnoreCase ) ) )
				dirs.Add( resolved );
		}

		return dirs;
	}

	static string ResolveRelative( string baseDir, string rel )
	{
		var parts = new List<string>( (baseDir ?? "").Split( '/', StringSplitOptions.RemoveEmptyEntries ) );
		foreach ( var seg in rel.Replace( '\\', '/' ).Split( '/', StringSplitOptions.RemoveEmptyEntries ) )
		{
			if ( seg == "." ) continue;
			if ( seg == ".." )
			{
				if ( parts.Count > 0 ) parts.RemoveAt( parts.Count - 1 );
				continue;
			}
			parts.Add( seg );
		}

		return string.Join( '/', parts );
	}

	Mesh BuildMesh( Kf2File kf2, Kf2File.Primitive prim, Vector3[] positions, int[] indices, List<string> searchDirs, Dictionary<string, Texture> cache )
	{
		var material = CreateMaterial( kf2, prim, searchDirs, cache );

		var mesh = new Mesh( material );

		var vertices = new SimpleVertex[positions.Length];
		for ( var i = 0; i < vertices.Length; i++ )
		{
			var n = prim.Normals is not null ? prim.Normals[i] : Vector3.Up;
			var uv = prim.Uvs is not null ? prim.Uvs[i] : Vector2.Zero;
			vertices[i] = new SimpleVertex(
				positions[i],
				new Vector3( -n.x, -n.z, n.y ),
				Vector3.Zero,
				uv );
		}

		mesh.CreateVertexBuffer( vertices.Length, vertices );
		mesh.CreateIndexBuffer( indices.Length, indices );
		mesh.Bounds = BBox.FromPoints( positions );
		return mesh;
	}

	Mesh BuildSkinnedMesh( Kf2File kf2, Kf2File.Primitive prim, Vector3[] positions, int[] indices, List<string> searchDirs, Dictionary<string, Texture> cache, Dictionary<string, int> boneIndexByNode )
	{
		var material = CreateMaterial( kf2, prim, searchDirs, cache );

		var mesh = new Mesh( material );

		var bone = prim.NodeName is not null && boneIndexByNode.TryGetValue( prim.NodeName, out var bi ) ? bi : 0;
		var blendIndices = new Color32( (byte)bone, 255, 255, 255 );
		var blendWeights = new Color32( 255, 0, 0, 0 );

		var vertices = new SkinnedVertex[positions.Length];
		for ( var i = 0; i < vertices.Length; i++ )
		{
			var n = prim.Normals is not null ? prim.Normals[i] : Vector3.Up;
			var uv = prim.Uvs is not null ? prim.Uvs[i] : Vector2.Zero;
			vertices[i] = new SkinnedVertex(
				positions[i],
				new Vector3( -n.x, -n.z, n.y ),
				uv, blendIndices, blendWeights );
		}

		mesh.CreateVertexBuffer( vertices.Length, vertices );
		mesh.CreateIndexBuffer( indices.Length, indices );
		mesh.Bounds = BBox.FromPoints( positions );
		return mesh;
	}

	static int[] BuildIndices( Kf2File kf2, Kf2File.Primitive prim )
	{
		var twoSided = prim.MaterialName is not null
			&& kf2.Materials.TryGetValue( prim.MaterialName, out var matDef ) && matDef.TwoSided;

		var tris = prim.Indices.Length;
		var indices = new int[twoSided ? tris * 2 : tris];
		for ( var i = 0; i < tris; i += 3 )
		{
			indices[i] = prim.Indices[i];
			indices[i + 1] = prim.Indices[i + 2];
			indices[i + 2] = prim.Indices[i + 1];
		}

		if ( twoSided )
		{
			for ( var i = 0; i < tris; i++ )
				indices[tris + i] = prim.Indices[i];
		}

		return indices;
	}

	Material CreateMaterial( Kf2File kf2, Kf2File.Primitive prim, List<string> searchDirs, Dictionary<string, Texture> cache )
	{
		var material = Material.Create( "mp2_model", "shaders/mp2_model.shader" );
		material?.Set( "g_tColor", ResolveTexture( kf2, prim.MaterialName, searchDirs, cache ) ?? Texture.White );
		// opaque DXT1 decodes a=1 everywhere, so the cutout never fires on solid models
		material?.SetFeature( "F_ALPHA_TEST", 1 );
		return material;
	}

	Texture ResolveTexture( Kf2File kf2, string materialName, List<string> searchDirs, Dictionary<string, Texture> cache )
	{
		if ( string.IsNullOrEmpty( materialName ) || !kf2.Materials.TryGetValue( materialName, out var mat ) )
			return null;

		if ( string.IsNullOrEmpty( mat.DiffuseTexture ) )
			return null;

		if ( cache.TryGetValue( mat.DiffuseTexture, out var cached ) )
			return cached;

		var tex = LoadTexture( searchDirs, mat.DiffuseTexture );
		cache[mat.DiffuseTexture] = tex;
		return tex;
	}

	Texture LoadTexture( List<string> searchDirs, string fileName )
	{
		var name = System.IO.Path.GetFileNameWithoutExtension( fileName );

		foreach ( var dir in searchDirs )
		{
			foreach ( var ext in new[] { ".dds", ".tga", ".jpg", ".pcx" } )
			{
				var candidate = string.IsNullOrEmpty( dir ) ? name + ext : $"{dir}/{name}{ext}";
				var data = Host.GetFileBytes( candidate );
				if ( data is not null )
					return MaxPayneImage.Load( data );
			}
		}

		return null;
	}
}

file struct SkinnedVertex( Vector3 position, Vector3 normal, Vector2 texcoord, Color32 blendIndices, Color32 blendWeights )
{
	[VertexLayout.Position] public Vector3 Position = position;
	[VertexLayout.Normal] public Vector3 Normal = normal;
	[VertexLayout.TexCoord] public Vector2 Texcoord = texcoord;
	[VertexLayout.BlendIndices] public Color32 BlendIndices = blendIndices;
	[VertexLayout.BlendWeight] public Color32 BlendWeights = blendWeights;
}
