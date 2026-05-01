using Refit;

namespace Sandbox.Services;

public partial class ServiceApi
{
	public interface IPackageApi
	{
		[Get( "/package/get/2/{packageIdent}" )]
		Task<PackageDto> Get( string packageIdent );

		[Post( "/package/favourite/2/{packageIdent}" )]
		Task<PackageFavouriteResult> SetFavourite( string packageIdent, [Query] bool state );

		[Post( "/package/rate/1/{packageIdent}" )]
		Task<PackageRateResult> SetRating( string packageIdent, [Query] int rating );

		[Post( "/package/upload/file" )]
		Task<FileUploadResult> UploadFile( [Body] FileUploadRequest file );

		[Post( "/package/upload/video" )]
		Task<FileUploadResult> UploadVideo( [Body] VideoUploadRequest file );

		[Get( "/package/list/1" )]
		Task<PackageGroups> GetList( [Query] string id = null );

		[Get( "/package/find/2" )]
		Task<PackageFindResult> Find( [Query] string q, int take = 100, int skip = 0 );

		[Post( "/package/manifest" )]
		Task<PublishManifestResult> PublishManifest( [Body] PublishManifest manifest );

		[Post( "/package/update/1/{packageIdent}" )]
		Task<PackageDto> Update( string packageIdent, [Query] string key, [Query] string value );

		[Get( "/package/reviews/{packageIdent}" )]
		Task<PackageReviewList> GetReviews( string packageIdent, [Query] int skip, [Query] int take, [Query] int score = 0, [Query] int positives = 0, [Query] int negatives = 0 );

		[Get( "/package/reviews/{packageIdent}/{steamid}" )]
		Task<PackageReviewDto> GetReview( string packageIdent, long steamid );

		[Post( "/package/reviews/{packageIdent}" )]
		Task PostReview( string packageIdent, string text, int rating, int positives, int negatives );
	}
}

public class PackageFindResult
{
	public List<PackageWrapMinimal> Packages { get; set; }
	public long TotalCount { get; set; }
	public List<PackageFacet> Facets { get; set; }
	public Dictionary<string, int> Tags { get; set; }
	public List<SortOrder> Orders { get; set; }
	public List<PackagePropertyTag> Properties { get; set; }
}

public struct PackageFacet
{
	public string Name { get; set; }
	public string Title { get; set; }
	public List<Entry> Entries { get; set; }

	public record struct Entry( string Name, string Title, string Icon, int Count, List<Entry> Children );
}

public struct PackagePropertyTag
{
	public string Icon { get; set; }
	public string Name { get; set; }
	public string Title { get; set; }
	public string Description { get; set; }
	public int Count { get; set; }
	public bool Exclusive { get; set; }
}


public struct FileUploadRequest
{
	/// <summary>
	/// The package ident
	/// </summary>
	public string Package { get; set; }

	/// <summary>
	/// A base64 encoded version of the file, for files under a certain size (say 20mb)
	/// </summary>
	public string Contents { get; set; }

	/// <summary>
	/// The name of the blob on our upload storage container
	/// </summary>
	public string Blob { get; set; }

	/// <summary>
	/// The game path of the file
	/// </summary>
	public string Path { get; set; }

	/// <summary>
	/// CRC64.ToString( "x" )
	/// </summary>
	public string Crc { get; set; }

	/// <summary>
	/// The size of the file
	/// </summary>
	public int Size { get; set; }
}

public struct VideoUploadRequest
{
	/// <summary>
	/// Package ident
	/// </summary>
	public string Package { get; set; }

	/// <summary>
	/// Optional video tag. If a video with this tag is found we'll replace it instead of creating a new one.
	/// </summary>
	public string Tag { get; set; }

	/// <summary>
	/// A base64 embed of the file - or a blob name 
	/// </summary>
	public string File { get; set; }

	/// <summary>
	/// Is this a thumb - will replace any existing thumbs
	/// </summary>
	public bool Thumb { get; set; }

	/// <summary>
	/// True for this video to start hidden
	/// </summary>
	public bool Hidden { get; set; }
}


public struct FileUploadResult
{
	public string Status { get; set; }
}

public struct VideoUploadResult
{
	public string Status { get; set; }
}


public struct PublishManifest
{
	public ManifestConfig Config { get; set; }
	public ManifestFile[] Assets { get; set; }
	public string Title { get; set; }
	public string Description { get; set; }
	public bool Publish { get; set; }
	public int EngineApi { get; set; }
	public string Meta { get; set; }
}

public record struct ManifestFile( string Name, int Size, string Hash );

public struct ManifestConfig
{
	public string Title { get; set; }
	public string Type { get; set; }
	public string Org { get; set; }
	public string Ident { get; set; }
	public int Schema { get; set; }
	public List<string> PackageReferences { get; set; }
	public List<string> EditorReferences { get; set; }
}

public struct PublishManifestResult
{
	public string Status { get; set; }
	public string[] Files { get; set; }
	public long VersionId { get; set; }
}
