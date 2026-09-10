namespace Sandbox.Services;

public struct OrganizationDto
{
	public string Ident { get; set; }
	public string Title { get; set; }
	public string Summary { get; set; }
	public string Description { get; set; }
	public string Thumb { get; set; }
	public string Twitter { get; set; }
	public string WebUrl { get; set; }
	public string Discord { get; set; }

	public DateTimeOffset Created { get; set; }
	public int PackageCount { get; set; }
	public int MemberCount { get; set; }
	public Player[] Members { get; set; }

	public string DevLink( string append = "/" )
	{
		return $"/{Ident}{append}";
	}
}
