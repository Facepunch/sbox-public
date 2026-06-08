using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace Sandbox;

public partial class Package
{
	/// <summary>
	/// Server-extracted metadata for the current version's compiled asset (model stats, material
	/// flags, clothing info, ...). Null if the version hasn't been processed or its type isn't
	/// recognised. Pattern-match the concrete type, e.g. <c>if ( package.Metadata is Package.ModelMetaData m )</c>.
	/// </summary>
	public AssetMetaData Metadata { get; internal set; }

	/// <summary>
	/// Base class for the metadata a package version's compiled asset exposes. The concrete type
	/// depends on the package type - see <see cref="ModelMetaData"/>, <see cref="MaterialMetaData"/>
	/// and <see cref="ClothingMetaData"/>.
	/// </summary>
	public abstract class AssetMetaData
	{
		internal static AssetMetaData FromDto( Sandbox.Services.BaseMetaData meta )
		{
			return meta switch
			{
				Sandbox.Services.ModelMetaData m => ModelMetaData.FromDto( m ),
				Sandbox.Services.MaterialMetaData m => MaterialMetaData.FromDto( m ),
				Sandbox.Services.ClothingMetaData m => ClothingMetaData.FromDto( m ),
				_ => null,
			};
		}
	}

	/// <summary>
	/// Metadata extracted from a model package version.
	/// </summary>
	public sealed class ModelMetaData : AssetMetaData
	{
		/// <summary>Per-LOD stats, LOD0 (full detail) first.</summary>
		public ImmutableArray<LodInfo> Lods { get; init; } = ImmutableArray<LodInfo>.Empty;

		public int Meshes { get; init; }
		public int Bones { get; init; }
		public int Animations { get; init; }
		public int Hitboxes { get; init; }
		public int BodyGroups { get; init; }
		public int MaterialGroups { get; init; }

		/// <summary>Compiler's total pool-size hint - a rough memory footprint for the model.</summary>
		public int MemorySize { get; init; }

		/// <summary>Whether the model ships a physics/collision mesh (embedded or referenced).</summary>
		public bool HasCollision { get; init; }

		/// <summary>Distinct material paths used by the model.</summary>
		public ImmutableArray<string> Materials { get; init; } = ImmutableArray<string>.Empty;

		public Vector3 BoundsMin { get; init; }
		public Vector3 BoundsMax { get; init; }

		/// <summary>Triangle and draw-call counts for a single level of detail.</summary>
		public readonly record struct LodInfo( long Triangles, long DrawCalls );

		internal static ModelMetaData FromDto( Sandbox.Services.ModelMetaData m )
		{
			return new ModelMetaData
			{
				Meshes = m.Meshes,
				Bones = m.Bones,
				Animations = m.Animations,
				Hitboxes = m.Hitboxes,
				BodyGroups = m.BodyGroups,
				MaterialGroups = m.MaterialGroups,
				MemorySize = m.MemorySize,
				HasCollision = m.HasCollision,
				BoundsMin = Vector3.Parse( m.BoundsMin ),
				BoundsMax = Vector3.Parse( m.BoundsMax ),
				Materials = ToImmutableStrings( m.Materials ),
				Lods = LodsFromDto( m.Lods ),
			};
		}

		static ImmutableArray<LodInfo> LodsFromDto( List<Sandbox.Services.ModelMetaData.LodInfo> src )
		{
			if ( src is null || src.Count == 0 )
				return ImmutableArray<LodInfo>.Empty;

			var array = new LodInfo[src.Count];

			for ( int i = 0; i < array.Length; i++ )
				array[i] = new LodInfo( src[i].Triangles, src[i].DrawCalls );

			return ImmutableCollectionsMarshal.AsImmutableArray( array );
		}
	}

	/// <summary>
	/// Metadata extracted from a material package version.
	/// </summary>
	public sealed class MaterialMetaData : AssetMetaData
	{
		public string ShaderName { get; init; }

		public bool IsTranslucent { get; init; }
		public bool IsAlphaTest { get; init; }
		public bool IsSky { get; init; }
		public bool IsOverlay { get; init; }
		public bool DoNotCastShadows { get; init; }
		public bool RenderBackfaces { get; init; }
		public bool IsTransmissive { get; init; }
		public bool IsDecal { get; init; }

		/// <summary>World-aligned mapping size, if the shader defines it (0 otherwise).</summary>
		public float WorldMappingWidth { get; init; }
		public float WorldMappingHeight { get; init; }

		/// <summary>Resolution of the material's representative (main) texture, 0 if none.</summary>
		public int RepresentativeWidth { get; init; }
		public int RepresentativeHeight { get; init; }

		/// <summary>Physics surface property name, if set.</summary>
		public string PhysicsSurface { get; init; }

		/// <summary>True if the package ships a trim/hotspot sheet (a .rect file).</summary>
		public bool HasTrimSheet { get; init; }

		/// <summary>The textures the material references and their dimensions.</summary>
		public ImmutableArray<TextureInfo> Textures { get; init; } = ImmutableArray<TextureInfo>.Empty;

		/// <summary>A texture referenced by a material, with its compiled dimensions and format.</summary>
		public readonly record struct TextureInfo( string Param, string Path, int Width, int Height, string Format, long Bytes, int Mips, bool IsHdr );

		internal static MaterialMetaData FromDto( Sandbox.Services.MaterialMetaData m )
		{
			return new MaterialMetaData
			{
				ShaderName = m.ShaderName,
				IsTranslucent = m.IsTranslucent,
				IsAlphaTest = m.IsAlphaTest,
				IsSky = m.IsSky,
				IsOverlay = m.IsOverlay,
				DoNotCastShadows = m.DoNotCastShadows,
				RenderBackfaces = m.RenderBackfaces,
				IsTransmissive = m.IsTransmissive,
				IsDecal = m.IsDecal,
				WorldMappingWidth = m.WorldMappingWidth,
				WorldMappingHeight = m.WorldMappingHeight,
				RepresentativeWidth = m.RepresentativeWidth,
				RepresentativeHeight = m.RepresentativeHeight,
				PhysicsSurface = m.PhysicsSurface,
				HasTrimSheet = m.HasTrimSheet,
				Textures = TexturesFromDto( m.Textures ),
			};
		}

		static ImmutableArray<TextureInfo> TexturesFromDto( List<Sandbox.Services.MaterialMetaData.TextureInfo> src )
		{
			if ( src is null || src.Count == 0 )
				return ImmutableArray<TextureInfo>.Empty;

			var array = new TextureInfo[src.Count];

			for ( int i = 0; i < array.Length; i++ )
			{
				var x = src[i];
				array[i] = new TextureInfo( x.Param, x.Path, x.Width, x.Height, x.Format, x.Bytes, x.Mips, x.IsHdr );
			}

			return ImmutableCollectionsMarshal.AsImmutableArray( array );
		}
	}

	/// <summary>
	/// Metadata extracted from a clothing package version.
	/// </summary>
	public sealed class ClothingMetaData : AssetMetaData
	{
		/// <summary>Display name shown in the dresser/store.</summary>
		public string Title { get; init; }

		/// <summary>Flavour text shown under the title.</summary>
		public string Subtitle { get; init; }

		/// <summary>Slot category, e.g. Hat, Tops, Footwear.</summary>
		public Clothing.ClothingCategory Category { get; init; }

		/// <summary>Finer grouping within the category, e.g. "Helmets".</summary>
		public string SubCategory { get; init; }

		/// <summary>Space-separated creator tags.</summary>
		public string Tags { get; init; }

		/// <summary>True if this item supplies its own human skin (body) instead of just an attachment.</summary>
		public bool HasHumanSkin { get; init; }

		/// <summary>Body groups toggled on when wearing the human-skin variant.</summary>
		public ulong HumanSkinBodyGroups { get; init; }

		/// <summary>Tags applied to the human-skin body.</summary>
		public string HumanSkinTags { get; init; }

		/// <summary>Material used for the eyes when this item replaces them.</summary>
		public string EyesMaterial { get; init; }

		/// <summary>Eyes material applied to the supplied human skin, if any.</summary>
		public string HumanEyesMaterial { get; init; }

		/// <summary>Body slots this item occupies on the outer layer.</summary>
		public Clothing.Slots SlotsOver { get; init; }

		/// <summary>Body slots this item occupies on the inner layer.</summary>
		public Clothing.Slots SlotsUnder { get; init; }

		/// <summary>Body regions hidden while this item is worn.</summary>
		public Clothing.BodyGroups HideBody { get; init; }

		/// <summary>True if the player can recolour this item.</summary>
		public bool AllowTintSelect { get; init; }

		/// <summary>Heel height offset (footwear only; 0 otherwise).</summary>
		public float HeelHeight { get; init; }

		/// <summary>The Steam item definition id this clothing maps to, if any.</summary>
		public int? SteamItemDefinitionId { get; init; }

		/// <summary>Models swapped in under specific conditions, keyed by condition.</summary>
		public IReadOnlyDictionary<string, string> ConditionalModels { get; init; } = ImmutableDictionary<string, string>.Empty;

		/// <summary>True if this clothing has been approved as a Steam workshop item.</summary>
		public bool WorkshopItemApproved { get; init; }

		internal static ClothingMetaData FromDto( Sandbox.Services.ClothingMetaData m )
		{
			return new ClothingMetaData
			{
				Title = m.Title,
				Subtitle = m.Subtitle,
				Category = (Clothing.ClothingCategory)(int)m.Category,
				SubCategory = m.SubCategory,
				Tags = m.Tags,
				HasHumanSkin = m.HasHumanSkin,
				HumanSkinBodyGroups = m.HumanSkinBodyGroups,
				HumanSkinTags = m.HumanSkinTags,
				EyesMaterial = m.EyesMaterial,
				HumanEyesMaterial = m.HumanEyesMaterial,
				SlotsOver = (Clothing.Slots)(int)m.SlotsOver,
				SlotsUnder = (Clothing.Slots)(int)m.SlotsUnder,
				HideBody = (Clothing.BodyGroups)(int)m.HideBody,
				AllowTintSelect = m.AllowTintSelect,
				HeelHeight = m.HeelHeight,
				SteamItemDefinitionId = m.SteamItemDefinitionId,
				ConditionalModels = m.ConditionalModels is { Count: > 0 } cm
					? cm.ToImmutableDictionary()
					: ImmutableDictionary<string, string>.Empty,
				WorkshopItemApproved = m.WorkshopItemApproved,
			};
		}
	}

	static ImmutableArray<string> ToImmutableStrings( List<string> src )
	{
		if ( src is null || src.Count == 0 )
			return ImmutableArray<string>.Empty;

		var array = new string[src.Count];

		for ( int i = 0; i < array.Length; i++ )
			array[i] = src[i];

		return ImmutableCollectionsMarshal.AsImmutableArray( array );
	}
}
