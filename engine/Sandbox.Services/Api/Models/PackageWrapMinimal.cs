using System.Text.Json.Serialization;

namespace Sandbox.Services;

public class PackageWrapMinimal
{
	public OrganizationDto Org { get; set; }

	[JsonIgnore]
	public long AssetId { get; set; }

	public string Ident { get; set; }
	public string FullIdent => $"{Org.Ident}.{Ident}";
	public string Title { get; set; }
	public string Summary { get; set; }
	public string Thumb { get; set; }
	public string ThumbWide { get; set; }
	public string ThumbTall { get; set; }
	public string VideoThumb { get; set; }
	public string TypeName { get; set; }
	public DateTimeOffset Updated { get; set; }
	public DateTimeOffset Created { get; set; }
	public bool Archived { get; set; } = false;
	public PackageUsageStats UsageStats { get; set; }
	public string[] Tags { get; set; }
	public int Favourited { get; set; }
	public int Collections { get; set; }
	public int Referencing { get; set; }
	public int Referenced { get; set; }
	public int VotesUp { get; set; }
	public int VotesDown { get; set; }
	public bool Public { get; set; }

	// Added afterwards, describes how a user interacted with this package
	public PackageInteraction Interaction { get; set; }
	public long Spawns { get; set; }
	public long PlayerSpawns { get; set; }

	public string DevLink( string append = "/" )
	{
		return $"{Org.Ident}/{Ident}{append}";
	}
}
