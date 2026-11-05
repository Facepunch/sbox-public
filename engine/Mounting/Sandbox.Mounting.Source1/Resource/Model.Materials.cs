using System;
using System.IO;

namespace Sandbox.Mounting.Source1;

internal sealed partial class SourceModelBuilder
{
	private sealed class MaterialSlots( SourceModelBuilder loader, MdlFile source, VtxFile triangles, StaticPropLighting lighting = null )
	{
		private readonly Dictionary<string, Material> resources = new( StringComparer.OrdinalIgnoreCase );
		private readonly Dictionary<(int Slot, int Lod), Material> defaults = [];
		private readonly Dictionary<Material, (int Slot, int Lod)> defaultOwners = new( ReferenceEqualityComparer.Instance );
		private readonly List<(int Slot, int Lod)> slots = [];
		private readonly Dictionary<(int Slot, int Lod, string Shader), bool> shaders = [];
		private readonly List<(Material Material, int Slot, (int BodyPart, int Model, int Lod, int Mesh) Mesh)> litSlots = [];
		private readonly HashSet<Material> litMaterials = new( ReferenceEqualityComparer.Instance );

		public Material GetForMesh( int slot, int lod, (int BodyPart, int Model, int Lod, int Mesh) mesh )
		{
			var original = Get( slot, lod );
			if ( lighting is null ) return original;
			var material = lighting.CreateMaterial( loader.Host, original, slot, mesh, 0 );
			if ( litMaterials.Add( material ) ) litSlots.Add( (material, slot, mesh) );
			return material;
		}

		public bool UsesBumpMapping()
		{
			for ( var i = 0; i < source.MaterialCount; i++ )
				if ( LoadName( source.GetMaterialName( i ) ).GetVector4( "g_flSourceHasNormal" ).x > 0.5f )
					return true;
			foreach ( var mesh in triangles.LightingMeshes )
				for ( var skin = 0; skin < Math.Max( 1, source.SkinFamilyCount ); skin++ )
					if ( Load( (source.GetMesh( mesh.BodyPart, mesh.Model, mesh.Mesh ).Material, mesh.Lod), skin )
						.GetVector4( "g_flSourceHasNormal" ).x > 0.5f )
						return true;
			return false;
		}

		public Material Get( int slot, int lod )
		{
			var key = (slot, lod);
			if ( defaults.TryGetValue( key, out var cached ) )
				return cached;
			var material = Load( key, 0 );

			if ( defaultOwners.TryGetValue( material, out var owner ) )
			{
				if ( SameSkins( owner, key ) )
				{
					defaults.Add( key, material );
					return material;
				}
				material = CreateMaterial( key, 0,
					$"source1/{loader.Host.Ident}/{System.IO.Path.ChangeExtension( loader.FullPath, null )}_material_{slot}_lod{lod}.vmat" );
			}
			defaultOwners.Add( material, key );
			defaults.Add( key, material );
			slots.Add( key );
			return material;
		}

		public bool UsesShader( int slot, int lod, string shader )
		{
			var key = (slot, lod, shader);
			if ( shaders.TryGetValue( key, out var cached ) )
				return cached;
			var matches = string.Equals( Get( slot, lod ).ShaderName, shader, StringComparison.OrdinalIgnoreCase );
			for ( var skin = 1; !matches && skin < source.SkinFamilyCount; skin++ )
			{
				var material = Load( (slot, lod), skin );
				matches = string.Equals( material.ShaderName, shader, StringComparison.OrdinalIgnoreCase );
			}
			shaders.Add( key, matches );
			return matches;
		}

		public void AddGroups( ModelBuilder builder )
		{
			if ( source.SkinFamilyCount <= 1 || slots.Count == 0 )
				return;
			for ( var skin = 0; skin < source.SkinFamilyCount; skin++ )
			{
				var group = builder.AddMaterialGroup( $"Skin {skin}" );
				if ( lighting is not null )
				{
					foreach ( var slot in litSlots )
						group.AddMaterial( skin == 0 ? slot.Material
							: lighting.CreateMaterial( loader.Host, Load( (slot.Slot, slot.Mesh.Lod), skin ), slot.Slot, slot.Mesh, skin ) );
					continue;
				}
				foreach ( var slot in slots )
					group.AddMaterial( skin == 0 ? defaults[slot] : Load( slot, skin ) );
			}
		}

		private bool SameSkins( (int Slot, int Lod) first, (int Slot, int Lod) second )
		{
			for ( var skin = 1; skin < source.SkinFamilyCount; skin++ )
			{
				if ( !GetMaterialName( first, skin ).Equals( GetMaterialName( second, skin ), StringComparison.OrdinalIgnoreCase ) )
					return false;
			}
			return true;
		}

		private string GetMaterialName( (int Slot, int Lod) slot, int skin )
		{
			var material = source.GetSkinMaterial( slot.Slot, skin );
			return triangles.GetMaterialReplacement( slot.Lod, material ) ?? source.GetMaterialName( material );
		}

		private Material Load( (int Slot, int Lod) slot, int skin )
		{
			var name = GetMaterialName( slot, skin );
			return LoadName( name );
		}

		private Material CreateMaterial( (int Slot, int Lod) slot, int skin, string name )
		{
			var materialName = GetMaterialName( slot, skin );
			var material = LoadName( materialName );
			return loader.Host.CreateMaterialVariant( material, name );
		}

		private Material LoadName( string name )
		{
			if ( resources.TryGetValue( name, out var material ) )
				return material;

			var path = FindMaterial( name );
			if ( path == null )
			{
				Log.Trace( $"{loader.FullPath}: material '{name}' was not found in its MDL search paths or the materials root of mount '{loader.Host.Ident}'; using the error material." );
				path = "materials/error.vmat";
			}
			material = path == "materials/error.vmat" ? Material.Load( path ) : loader.Host.LoadContentMaterial( path, lighting?.ContentRoot ?? loader.ContentRoot );
			if ( material == null || !material.IsValid || material.IsError )
				throw new InvalidDataException( $"{loader.FullPath}: material '{path}' could not be loaded." );
			resources.Add( name, material );
			return material;
		}

		private string FindMaterial( string name )
		{
			for ( var path = 0; path < source.MaterialPathCount; path++ )
			{
				var resource = Find( source.GetMaterialFullPath( name, path ) );
				if ( resource != null )
					return resource;
			}

			return Find( name );

			string Find( string path )
			{
				path = VmtFile.NormalizePath( path, ".vmt" );
				var resource = loader.Host.GetContentPath( path, lighting?.ContentRoot ?? loader.ContentRoot );
				return resource is null ? null : path;
			}
		}
	}
}
