namespace Sandbox.Services;

public class PackageDto
{
	public OrganizationDto Org { get; set; }
	public string Ident { get; set; }
	public string Title { get; set; }
	public string Summary { get; set; }
	public string Description { get; set; }
	public string Thumb { get; set; }
	public string ThumbWide { get; set; }
	public string ThumbTall { get; set; }
	public DateTimeOffset Updated { get; set; }
	public DateTimeOffset Created { get; set; }
	public PackageUsageStats UsageStats { get; set; }
	public PackageReviewStats ReviewStats { get; set; }
	public string[] Tags { get; set; }
	public int Favourited { get; set; }
	public int VotesUp { get; set; }
	public int VotesDown { get; set; }
	public PackageVersion Version { get; set; }
	public string Source { get; set; }
	public bool Public { get; set; }
	public bool Archive { get; set; }
	public int ApiVersion { get; set; }
	public Screenshot[] Screenshots { get; set; }

	public string TypeName { get; set; }

	/// <summary>
	/// List of packages that this package needs to run
	/// </summary>
	public string[] PackageReferences { get; set; }

	/// <summary>
	/// List of packages that this package needs to edit
	/// </summary>
	public string[] EditorReferences { get; set; }

	/// <summary>
	/// A specific user's interaction state with this package
	/// </summary>
	public PackageInteraction Interaction { get; set; }

	/// <summary>
	/// For games only, information about the loadingscreen
	/// </summary>
	public LoadingScreenSetup LoadingScreen { get; set; }

	/// <summary>
	/// General config data. This is package type specific.
	/// </summary>
	public Dictionary<string, object> Data { get; set; }

	/// <summary>
	/// The latest news post
	/// </summary>
	public NewsPostDto LatestNews { get; set; }

	/// <summary>
	/// What fraction of users got errors in the last day
	/// </summary>
	public float ErrorRate { get; set; }
	public LicenseType AssetLicense { get; set; }
	public LicenseType SoftwareLicense { get; set; }

	public struct LoadingScreenSetup
	{
		public string MediaUrl { get; set; }

		// as a struct incase we want to add more info to the loading screen
		// like allowing them to add titles or something. Stop short at uploading a screenshot or video for now.
	}

	/// <summary>
	/// The amount of times this has been spawned
	/// </summary>
	public long Spawns { get; set; }

	/// <summary>
	/// The amount of players that have spawned this
	/// </summary>
	public long PlayerSpawns { get; set; }

	public string DevLink( string append = "/" )
	{
		return $"{Org.Ident}/{Ident}{append}";
	}
}
