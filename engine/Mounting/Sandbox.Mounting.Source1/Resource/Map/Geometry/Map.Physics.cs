using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using static Sandbox.Mounting.Source1.BspGeometry;

namespace Sandbox.Mounting.Source1;

partial class MapLoader
{
	void BuildWorldPhysics( BspFile bsp, GameObject world, bool skybox = false, int modelIndex = 0, bool staticCollider = true )
	{
		var (hulls, meshes, virtualTerrain) = bsp.ParsePhysicsGeometry( modelIndex );
		if ( skybox )
			bsp.FilterSkyboxPhysics( hulls, meshes );
		var bodies = new Dictionary<(Surface Surface, bool PassAudio), PhysicsBodyBuilder>();
		var surfaces = new List<Surface>();
		var surfaceIndices = new Dictionary<Surface, byte>();
		foreach ( var hull in hulls )
			GetBody( hull.SurfaceName, hull.PassAudio ).AddHull( CollectionsMarshal.AsSpan( hull.Vertices ) );
		foreach ( var mesh in meshes )
		{
			var materials = mesh.SurfaceNames.Select( name => GetMaterial( Host.GetSurface( name, FullPath ) ) ).ToArray();
			GetBody( mesh.SurfaceName, mesh.PassAudio ).AddMesh( CollectionsMarshal.AsSpan( mesh.Vertices ), CollectionsMarshal.AsSpan( mesh.Indices ), materials );
		}

		bool hasDisplacements = modelIndex == 0 && virtualTerrain && AddDisplacementPhysics( bsp, name => GetBody( name, false ), GetMaterial, skybox );
		if ( hulls.Count == 0 && meshes.Count == 0 && !hasDisplacements )
			return;
		var mixed = hulls.Count( hull => hull.MixedSurfaces );
		if ( mixed > 0 )
			Log.Warning( $"{FullPath}: {mixed} convex collision hulls have multiple Source surfaces; native hull builders cannot store per-face surfaces, so those hulls retain their solid's surface. Geometry is unchanged." );
		foreach ( bool passAudio in new[]
		{
			false,
			true
		}

		)
		{
			var parts = bodies.Where( entry => entry.Key.PassAudio == passAudio ).Select( entry => entry.Value ).ToList();
			if ( parts.Count == 0 )
				continue;
			var target = world;
			if ( passAudio )
			{
				target = new GameObject( false, "audio transparent collision" )
				{
					Parent = world,
					IsStatic = staticCollider
				};
				target.Tags.Add( "world" );
				target.Tags.Add( "passaudio" );
			}

			var suffix = passAudio ? "_passaudio" : "";
			var physics = PhysicsGroupDescription.Create( $"source1/{Host.Ident}/{FullPath}/{(skybox ? "skybox_" : "")}model_{modelIndex}{suffix}.vphys", parts, surfaces );
			_worldPhysics.Add( physics );
			var collider = target.AddComponent<PhysicsCollider>( false );
			collider.Static = staticCollider;
			collider.Physics = physics;
			collider.Enabled = true;
			if ( passAudio )
				target.Enabled = true;
		}

		PhysicsBodyBuilder GetBody( string name, bool passAudio )
		{
			var surface = Host.GetSurface( name, FullPath );
			if ( !bodies.TryGetValue( (surface, passAudio), out var body ) )
			{
				body = new PhysicsBodyBuilder
				{
					Mass = 1000,
					Surface = surface
				};
				bodies.Add( (surface, passAudio), body );
			}

			return body;
		}

		byte GetMaterial( Surface surface )
		{
			if ( surfaceIndices.TryGetValue( surface, out var index ) )
				return index;
			if ( surfaces.Count >= 256 )
				throw new NotSupportedException( $"{FullPath}: world collision exceeds the native 256-surface material table." );
			index = (byte)surfaces.Count;
			surfaceIndices.Add( surface, index );
			surfaces.Add( surface );
			return index;
		}
	}

	bool AddDisplacementPhysics( BspFile bsp, Func<string, PhysicsBodyBuilder> getBody, Func<Surface, byte> getMaterial, bool skybox = false )
	{
		const int CONTENTS_SOLID = 0x1;
		const int CONTENTS_WINDOW = 0x2;
		const int CONTENTS_GRATE = 0x8;
		const int SURF_NOPHYSICS_COLL = 0x2;
		bool added = false;
		var materialSurfaces = new Dictionary<int, (string Primary, string Secondary)>();
		Span<Vector3> corners = stackalloc Vector3[4];
		ref var world = ref bsp.Models[0];
		for ( int i = world.FirstFace; i < world.FirstFace + world.FaceCount; i++ )
		{
			ref var face = ref bsp.Faces[i];
			if ( face.DispInfo < 0 || skybox && !bsp.IsActiveSkyboxFace( i ) )
				continue;
			ref var disp = ref bsp.DispInfos[face.DispInfo];
			if ( (disp.Flags & SURF_NOPHYSICS_COLL) != 0 || (disp.Contents & (CONTENTS_SOLID | CONTENTS_WINDOW | CONTENTS_GRATE)) == 0 )
				continue;
			int size = (1 << disp.Power) + 1;
			var indices = new List<int>( (size - 1) * (size - 1) * 6 );
			AddDisplacementIndices( indices, 0, size, DisplacementTriangleTags( bsp, disp ) );
			if ( indices.Count == 0 )
				continue;
			GetDisplacementCorners( bsp, face, corners );
			var vertices = new Vector3[size * size];
			for ( int y = 0; y < size; y++ )
			{
				float ty = y / (float)(size - 1);
				for ( int x = 0; x < size; x++ )
				{
					float tx = x / (float)(size - 1);
					int vertex = x + y * size;
					ref var offset = ref bsp.DispVerts[disp.DispVertStart + vertex];
					vertices[vertex] = DisplacementBasePosition( corners, tx, ty ) + offset.Vector * offset.Dist;
				}
			}

			int texData = face.TexInfo < 0 ? -1 : bsp.TexInfos[face.TexInfo].TexData;
			if ( !materialSurfaces.TryGetValue( texData, out var surfaceNames ) )
			{
				var materialPath = $"texture data {texData}";
				surfaceNames = ("default", "default");
				try
				{
					if ( texData < 0 )
						throw new InvalidDataException( "The displacement has no texture information." );
					materialPath = VmtFile.NormalizePath( bsp.GetTexDataString( texData ), ".vmt" );
					if ( Host.GetContentPath( materialPath, ContentRoot ) is null )
						materialPath = $"{GetBaseMaterialPath( materialPath[..^4] )}.vmt";
					var material = VmtFile.Load( materialPath, path => Host.OpenContentFile( path, ContentRoot ), null, diagnostic => Log.Warning( $"{diagnostic.Path}:{diagnostic.Line}: {diagnostic.Message}" ) );
					var parameters = new VmtParameters( material.Parameters, materialPath );
					var primary = parameters.String( "$surfaceprop", "default" );
					var secondary = parameters.String( "$surfaceprop2", primary );
					surfaceNames = (primary, string.IsNullOrWhiteSpace( secondary ) ? primary : secondary);
				}
				catch ( Exception e ) when ( e is FileNotFoundException or InvalidDataException or NotSupportedException )
				{
					Log.Warning( $"{FullPath}: displacement surface properties could not be read from '{materialPath}': {e.Message} Using the mount's default surface." );
				}

				materialSurfaces.Add( texData, surfaceNames );
			}

			var primaryIndex = getMaterial( Host.GetSurface( surfaceNames.Primary, FullPath ) );
			var secondaryIndex = getMaterial( Host.GetSurface( surfaceNames.Secondary, FullPath ) );
			var materials = new byte[indices.Count / 3];
			bool multiblend = (disp.Flags & 0x40000000) != 0;
			for ( int triangle = 0; triangle < materials.Length; triangle++ )
			{
				float alpha = 0;
				for ( int corner = 0; corner < 3; corner++ )
					alpha += bsp.DispVerts[disp.DispVertStart + indices[triangle * 3 + corner]].Alpha;
				materials[triangle] = !multiblend && alpha > 382.5f ? secondaryIndex : primaryIndex;
			}

			if ( multiblend )
				Log.Warning( $"{FullPath}: virtualterrain displacement {face.DispInfo} uses multiblend; its primary surface is used because multiblend data is not imported." );
			getBody( surfaceNames.Primary ).AddMesh( vertices, MemoryMarshal.Cast<int, uint>( CollectionsMarshal.AsSpan( indices ) ), materials );
			added = true;
		}

		return added;
	}
}
