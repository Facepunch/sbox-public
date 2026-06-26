namespace Sandbox.Services;

/// <summary>
/// Extracted metadata for a model package. Shared DTO (website + game engine).
/// </summary>
public class ModelMetaData : BaseMetaData
{
	/// <summary>Per-LOD stats, LOD0 (full detail) first.</summary>
	public List<LodInfo> Lods { get; set; } = new();

	public int Meshes { get; set; }
	public int Bones { get; set; }
	public int Animations { get; set; }
	public int Hitboxes { get; set; }
	public int BodyGroups { get; set; }
	public int MaterialGroups { get; set; }

	/// <summary>Compiler's total pool-size hint - a rough memory footprint for the model.</summary>
	public int MemorySize { get; set; }

	/// <summary>Whether the model ships a physics/collision mesh (embedded or referenced).</summary>
	public bool HasCollision { get; set; }

	/// <summary>Distinct material paths used by the model.</summary>
	public List<string> Materials { get; set; } = new();

	/// <summary>Model bounds, stored as a <c>"x,y,z"</c> string. Use <see cref="MetaVector"/> to convert.</summary>
	public string BoundsMin { get; set; }

	/// <inheritdoc cref="BoundsMin"/>
	public string BoundsMax { get; set; }

	public class LodInfo
	{
		public long Triangles { get; set; }
		public long DrawCalls { get; set; }
	}

	public override void GetAutoTags( HashSet<string> tags )
	{
		SetTag( tags, "haslods", Lods.Count > 1 );
		SetTag( tags, "hascollision", HasCollision );
		SetTag( tags, "hasbones", Bones > 0 );
		SetTag( tags, "hasanims", Animations > 0 );
		SetTag( tags, "hashitboxes", Hitboxes > 0 );
	}

	const int SizeCategory = 49;
	const int SizeTiny = 50, SizeSmall = 51, SizeMedium = 52, SizeLarge = 53, SizeHuge = 54, SizeMassive = 55;

	const int TriangleCategory = 227;
	const int TriLow = 228, TriMedium = 229, TriHigh = 230, TriVeryHigh = 231;

	static readonly int[] ManagedCategories =
	{
		SizeCategory, SizeTiny, SizeSmall, SizeMedium, SizeLarge, SizeHuge, SizeMassive,
		TriangleCategory, TriLow, TriMedium, TriHigh, TriVeryHigh,
	};

	public override void UpdateCategories( List<int> categories )
	{
		// Drop any buckets we own so a re-index never leaves a stale one behind.
		categories.RemoveAll( id => ManagedCategories.Contains( id ) );

		var triangles = Lods.FirstOrDefault()?.Triangles ?? 0;
		int? tri = triangles switch
		{
			<= 0 => null,
			< 1_000 => TriLow,
			< 10_000 => TriMedium,
			< 50_000 => TriHigh,
			_ => TriVeryHigh,
		};

		if ( tri is not null )
		{
			categories.Add( TriangleCategory );
			categories.Add( tri.Value );
		}

		// Physical size bucket from the largest bounding-box dimension (units).
		var min = BoundsMin.ToMetaVector();
		var max = BoundsMax.ToMetaVector();
		var size = MathF.Max( MathF.Max( max.X - min.X, max.Y - min.Y ), max.Z - min.Z );
		int? sizeBucket = size switch
		{
			<= 0f => null,
			< 16f => SizeTiny,
			< 64f => SizeSmall,
			< 256f => SizeMedium,
			< 1024f => SizeLarge,
			< 4096f => SizeHuge,
			_ => SizeMassive,
		};

		if ( sizeBucket is not null )
		{
			categories.Add( SizeCategory );
			categories.Add( sizeBucket.Value );
		}
	}
}
