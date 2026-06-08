namespace Sandbox.Services;

/// <summary>
/// Extracted metadata for a material package. Shared DTO (website + game engine).
/// </summary>
public class MaterialMetaData : BaseMetaData
{
	public string ShaderName { get; set; }

	public bool IsTranslucent { get; set; }
	public bool IsAlphaTest { get; set; }
	public bool IsSky { get; set; }
	public bool IsOverlay { get; set; }
	public bool DoNotCastShadows { get; set; }
	public bool RenderBackfaces { get; set; }
	public bool IsTransmissive { get; set; }
	public bool IsDecal { get; set; }

	/// <summary>World-aligned mapping size, if the shader defines it (0 otherwise).</summary>
	public float WorldMappingWidth { get; set; }
	public float WorldMappingHeight { get; set; }

	/// <summary>Resolution of the material's representative (main) texture, 0 if none.</summary>
	public int RepresentativeWidth { get; set; }
	public int RepresentativeHeight { get; set; }

	/// <summary>Physics surface property name, if set.</summary>
	public string PhysicsSurface { get; set; }

	/// <summary>True if the package ships a trim/hotspot sheet (a .rect file).</summary>
	public bool HasTrimSheet { get; set; }

	/// <summary>The textures the material references and their dimensions.</summary>
	public List<TextureInfo> Textures { get; set; } = new();

	public class TextureInfo
	{
		public string Param { get; set; }
		public string Path { get; set; }
		public int Width { get; set; }
		public int Height { get; set; }

		/// <summary>GPU/compression format, e.g. "BC7", "DXT5", "RGBA8888".</summary>
		public string Format { get; set; }

		/// <summary>Compiled (.vtex_c) size on disk, in bytes.</summary>
		public long Bytes { get; set; }

		public int Mips { get; set; }
		public bool IsHdr { get; set; }
	}

	public override void GetAutoTags( HashSet<string> tags )
	{
		SetTag( tags, "translucent", IsTranslucent );
		SetTag( tags, "alphatest", IsAlphaTest );
		SetTag( tags, "sky", IsSky );
		SetTag( tags, "worldmapped", WorldMappingWidth > 0 && WorldMappingHeight > 0 );
		SetTag( tags, "trimsheet", HasTrimSheet );
		SetTag( tags, "nocastshadows", DoNotCastShadows );
		SetTag( tags, "doublesided", RenderBackfaces );
		SetTag( tags, "transmissive", IsTransmissive );
		SetTag( tags, "decal", IsDecal );

		SetTag( tags, "hdr", Textures.Any( t => t.IsHdr ) );
		SetTag( tags, "uncompressed", Textures.Any( t => IsUncompressed( t.Format ) ) );

		// Flag textures big enough to want a mip chain but shipping a single level. Tiny
		// (<=4px) textures legitimately have one mip, so don't count those.
		SetTag( tags, "nomips", Textures.Any( t => t.Mips <= 1 && Math.Max( t.Width, t.Height ) > 4 ) );
	}

	// Block-compressed format names all contain one of these; anything else is uncompressed
	// (RGBA8888, R8, RGBA16161616F, etc).
	static bool IsUncompressed( string format ) =>
		!string.IsNullOrEmpty( format ) &&
		!format.Contains( "DXT", StringComparison.OrdinalIgnoreCase ) &&
		!format.Contains( "BC", StringComparison.OrdinalIgnoreCase ) &&
		!format.Contains( "ATI", StringComparison.OrdinalIgnoreCase ) &&
		!format.Contains( "ETC", StringComparison.OrdinalIgnoreCase );

	// "Texture Resolution" category tree, bucketed by the representative texture resolution.
	const int ResolutionCategory = 232;
	const int Res256 = 233, Res1k = 234, Res2k = 235, Res4k = 236, Res8k = 237;

	static readonly int[] ManagedCategories = { ResolutionCategory, Res256, Res1k, Res2k, Res4k, Res8k };

	public override void UpdateCategories( List<int> categories )
	{
		// Drop our buckets first so a re-index never leaves a stale one behind.
		categories.RemoveAll( ManagedCategories.Contains );

		// Bucket by the representative texture resolution, rounded up to the tier.
		var max = Math.Max( RepresentativeWidth, RepresentativeHeight );

		var leaf = max switch
		{
			<= 0 => 0,
			<= 256 => Res256,
			<= 1024 => Res1k,
			<= 2048 => Res2k,
			<= 4096 => Res4k,
			_ => Res8k,
		};

		if ( leaf != 0 )
		{
			categories.Add( ResolutionCategory );
			categories.Add( leaf );
		}
	}
}
