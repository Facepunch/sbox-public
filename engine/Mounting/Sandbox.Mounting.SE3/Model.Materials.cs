using System;

namespace SeriousEngine3;

[Flags]
enum ShaderFlags
{
	None = 0,
	AlphaTest = 1 << 0,
	DoubleSided = 1 << 1,
}

public readonly record struct UvMapping( float ScaleU, float ScaleV, float OffsetU, float OffsetV, float RotationDegrees )
{
	public static readonly UvMapping Identity = new( 1f, 1f, 0f, 0f, 0f );

	public bool IsIdentity => ScaleU == 1f && ScaleV == 1f && OffsetU == 0f && OffsetV == 0f && RotationDegrees == 0f;

	public Vector2 Apply( Vector2 uv )
	{
		if ( IsIdentity )
			return uv;

		float r = RotationDegrees * (MathF.PI / 180f);
		float c = MathF.Cos( r );
		float s = MathF.Sin( r );
		return new Vector2(
			ScaleU * (uv.x * c - uv.y * s) + OffsetU,
			ScaleV * (uv.x * s + uv.y * c) + OffsetV );
	}
}

public partial class ModelLoader
{
	readonly Dictionary<ShaderFlags, Material> baseMaterials = new();

	static readonly Texture FlatNormal = Texture.Create( 1, 1 ).WithData( new byte[] { 128, 128, 255, 128 } ).Finish();

	readonly record struct ShaderArgData( string Color, string Normal, ShaderFlags Flags, float AlphaRef, string ColorUv, UvMapping DiffuseMapping, UvMapping NormalMapping );

	readonly record struct SurfaceMaterial( Material Material, string ColorUv, UvMapping Mapping, UvMapping NormalMapping )
	{
		public static readonly SurfaceMaterial Skipped = new( null, null, UvMapping.Identity, UvMapping.Identity );

		public bool IsSkipped => Material is null;
	}

	Material GetBaseMaterial( ShaderFlags flags )
	{
		if ( baseMaterials.TryGetValue( flags, out var mat ) )
			return mat;

		mat = Material.Create( $"se3_base_{(int)flags}", "shaders/se3.shader" );
		mat.Set( "g_bFogEnabled", true );

		mat.Set( "g_tColor", Texture.White );
		mat.Set( "g_tNormal", FlatNormal );

		if ( flags.HasFlag( ShaderFlags.AlphaTest ) )
		{
			mat.SetFeature( "F_ALPHA_TEST", 1 );
			mat.Set( "g_flAlphaTestReference", 0.5f );
			mat.Set( "g_flAntiAliasedEdgeStrength", 1.0f );
		}

		if ( flags.HasFlag( ShaderFlags.DoubleSided ) )
			mat.SetFeature( "F_RENDER_BACKFACES", 1 );

		baseMaterials[flags] = mat;
		return mat;
	}

	static Dictionary<string, int> BuildModifierMap( CTSEMeta meta, out int defaultPresetPtr )
	{
		defaultPresetPtr = -1;
		var map = new Dictionary<string, int>( System.StringComparer.OrdinalIgnoreCase );

		foreach ( var listObj in meta.GetObjectsOfType( "CMeshModifierList" ) )
		{
			var modifiers = meta.Wrap( listObj ).GetOrNull( "1" );
			if ( modifiers == null )
				continue;

			for ( int i = 0; i < modifiers.Count; i++ )
			{
				var mod = modifiers[i];
				int presetPtr = mod.GetOrNull( "3" )?.AsPointer() ?? 0;
				if ( presetPtr <= 0 )
					continue;

				var surfaceName = mod.GetOrNull( "1" )?.AsIdentName();
				if ( string.IsNullOrEmpty( surfaceName ) )
				{
					if ( defaultPresetPtr < 0 )
						defaultPresetPtr = presetPtr;
					continue;
				}

				map.TryAdd( surfaceName, presetPtr );
			}
		}

		return map;
	}

	SurfaceMaterial ResolveSurfaceMaterial(
		CTSEMeta modelMeta,
		MetaValue surface,
		MetaValue lodSurfaces,
		int surfaceIndex,
		Dictionary<string, int> modifierMap,
		int defaultPresetPtr,
		Dictionary<string, SurfaceMaterial> cache,
		Material defaultMaterial )
	{
		bool sawOverlay = false;

		SurfaceMaterial? FromPreset( CTSEMeta meta, int ptr )
		{
			if ( ptr <= 0 )
				return null;

			var result = ResolveMaterialFromPointer( meta, ptr, cache, defaultMaterial );
			if ( result.IsSkipped )
			{
				sawOverlay = true;
				return null;
			}

			return result.Material != defaultMaterial ? result : null;
		}

		int directPtr = surface.GetOrNull( "16" )?.AsPointer() ?? 0;
		int basePtr = BaseSurfacePresetPtr( surface, lodSurfaces, surfaceIndex );

		var surfaceName = surface.GetOrNull( "23" )?.AsIdentName();
		int modifierPtr = !string.IsNullOrEmpty( surfaceName ) && modifierMap.TryGetValue( surfaceName, out int p ) ? p : 0;

		return FromPreset( surface.Meta, directPtr )
			?? FromPreset( surface.Meta, basePtr )
			?? FromPreset( modelMeta, modifierPtr )
			?? FromPreset( modelMeta, defaultPresetPtr )
			?? (sawOverlay ? SurfaceMaterial.Skipped : new SurfaceMaterial( defaultMaterial, null, UvMapping.Identity, UvMapping.Identity ));
	}

	static int BaseSurfacePresetPtr( MetaValue surface, MetaValue lodSurfaces, int surfaceIndex )
	{
		var member = surface.GetOrNull( "20" );
		int baseIndex = member?.Kind is ValueKind.SLONG or ValueKind.CSyncedSLONG ? member.AsInt() : -1;
		if ( baseIndex < 0 || baseIndex == surfaceIndex || baseIndex >= lodSurfaces.Count )
			return 0;

		return lodSurfaces[baseIndex].GetOrNull( "16" )?.AsPointer() ?? 0;
	}

	SurfaceMaterial ResolveMaterialFromPointer( CTSEMeta meta, int pointerId, Dictionary<string, SurfaceMaterial> cache, Material defaultMaterial )
	{
		var mtrPath = meta.GetExternalResourcePath( pointerId );
		if ( mtrPath != null )
		{
			if ( cache.TryGetValue( mtrPath, out var cached ) )
				return cached;

			var result = CreateMaterialFromMtr( mtrPath, defaultMaterial );
			cache[mtrPath] = result;
			return result;
		}

		var preset = meta.WrapById( (uint)pointerId );
		if ( preset != null && preset.Type.Name == "CShaderPreset" )
		{
			var key = $"#{meta.GetHashCode()}:{pointerId}";
			if ( cache.TryGetValue( key, out var cached ) )
				return cached;

			var result = CreateMaterialFromPreset( preset, defaultMaterial );
			cache[key] = result;
			return result;
		}

		return new SurfaceMaterial( defaultMaterial, null, UvMapping.Identity, UvMapping.Identity );
	}

	SurfaceMaterial CreateMaterialFromMtr( string mtrPath, Material defaultMaterial )
	{
		try
		{
			var mtrMeta = CTSEMeta.Read( Host.GetBytes( mtrPath ) );
			mtrMeta.Host = Host;

			var preset = mtrMeta.GetOrNull( "CShaderPreset" );
			return preset != null ? CreateMaterialFromPreset( preset, defaultMaterial ) : new SurfaceMaterial( defaultMaterial, null, UvMapping.Identity, UvMapping.Identity );
		}
		catch
		{
			return new SurfaceMaterial( defaultMaterial, null, UvMapping.Identity, UvMapping.Identity );
		}
	}

	SurfaceMaterial CreateMaterialFromPreset( MetaValue preset, Material defaultMaterial )
	{
		if ( TryReadShaderArgs( preset, out var data, out bool hasArgs ) )
			return new SurfaceMaterial( BuildMaterial( data ), data.ColorUv, data.DiffuseMapping, data.NormalMapping );

		return hasArgs ? SurfaceMaterial.Skipped : new SurfaceMaterial( defaultMaterial, null, UvMapping.Identity, UvMapping.Identity );
	}

	static bool TryReadShaderArgs( MetaValue preset, out ShaderArgData data, out bool hasArgs )
	{
		data = default;
		hasArgs = false;

		var configs = preset.GetOrNull( "1" );
		if ( configs == null )
			return false;

		for ( int i = 0; i < configs.Count; i++ )
		{
			int argsPtr = configs[i].GetOrNull( "2" )?.AsPointer() ?? 0;
			if ( argsPtr <= 0 )
				continue;

			var args = preset.Meta.WrapById( (uint)argsPtr );
			if ( args == null )
				continue;

			hasArgs = true;
			if ( TryReadArgs( args, out data ) )
				return true;
		}

		return false;
	}

	static bool TryReadArgs( MetaValue args, out ShaderArgData data )
	{
		data = default;

		var (colorId, normalId) = SlotsFor( args.Type.Name );
		var color = ResolveTexture( args, colorId );
		var normal = ResolveTexture( args, normalId );

		if ( color == null && normal == null )
			return false;

		var flags = ShaderFlags.None;
		if ( ReadBool( args, "5" ) ) flags |= ShaderFlags.AlphaTest;
		if ( ReadBool( args, "7" ) ) flags |= ShaderFlags.DoubleSided;

		bool isStandard = args.Type.Name == "CStandardShaderArgs";
		var colorUv = ReadColorChannel( args, colorId );

		var diffuseMapping = isStandard ? ReadStandardMapping( args, colorId, 2 ) : UvMapping.Identity;
		var normalMapping = isStandard ? ReadStandardMapping( args, normalId, 4 ) : UvMapping.Identity;

		data = new ShaderArgData( color, normal, flags, ReadFloat( args, "6", 0.5f ), colorUv, diffuseMapping, normalMapping );
		return true;
	}

	static string ReadColorChannel( MetaValue args, string colorId )
	{
		if ( colorId == null || !int.TryParse( colorId, out int baseId ) )
			return null;

		var name = args.GetOrNull( (baseId + 1).ToString() )?.GetOrNull( "1" )?.AsIdentName();
		return string.IsNullOrEmpty( name ) ? null : name;
	}

	static UvMapping ReadStandardMapping( MetaValue args, string slotId, int stretchOffset )
	{
		if ( slotId == null || !int.TryParse( slotId, out int baseId ) )
			return UvMapping.Identity;

		float stretchU = MapFloat( args, baseId + stretchOffset, 1f );
		float stretchV = MapFloat( args, baseId + stretchOffset + 1, 1f );
		float offsetU = MapFloat( args, baseId + stretchOffset + 2, 0f );
		float offsetV = MapFloat( args, baseId + stretchOffset + 3, 0f );
		float rotation = MapFloat( args, baseId + stretchOffset + 4, 0f );
		return new UvMapping( stretchU, stretchV, offsetU, offsetV, rotation );
	}

	static float MapFloat( MetaValue args, int id, float fallback )
		=> args.GetOrNull( id.ToString() )?.GetOrNull( "1" )?.AsFloat() ?? fallback;

	static (string Color, string Normal) SlotsFor( string argsType ) => argsType switch
	{
		"CVegetationShaderArgs" => ("103", "114"),
		"CMultiLayerShaderArgs" => ("300", null),
		_ => ("103", "201"),
	};

	static string ResolveTexture( MetaValue args, string id )
	{
		if ( id == null )
			return null;

		var pointer = args.GetOrNull( id )?.GetOrNull( "1" );
		if ( pointer == null || pointer.Kind is not (ValueKind.Pointer or ValueKind.Ptr or ValueKind.Handle) )
			return null;

		int objId = pointer.AsPointer();
		return objId > 0 ? args.Meta.GetExternalResourcePath( objId ) : null;
	}

	static bool ReadBool( MetaValue args, string id )
		=> (args.GetOrNull( id )?.GetOrNull( "1" )?.AsInt() ?? 0) != 0;

	static float ReadFloat( MetaValue args, string id, float fallback )
		=> args.GetOrNull( id )?.GetOrNull( "1" )?.AsFloat() ?? fallback;

	Material BuildMaterial( ShaderArgData data )
	{
		var mat = GetBaseMaterial( data.Flags ).CreateCopy();

		if ( data.Color != null )
			mat.Set( "g_tColor", LoadTexture( data.Color ) );
		if ( data.Normal != null )
			mat.Set( "g_tNormal", LoadTexture( data.Normal ) );
		if ( data.Flags.HasFlag( ShaderFlags.AlphaTest ) )
			mat.Set( "g_flAlphaTestReference", data.AlphaRef );

		return mat;
	}

	Texture LoadTexture( string path )
		=> Texture.Load( $"mount://{Host.Ident}/{path}.vtex", false );
}
