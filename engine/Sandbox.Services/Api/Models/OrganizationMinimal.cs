namespace Sandbox.Services;

public struct OrganizationMinimal
{
	public string Ident { get; set; }
	public string Title { get; set; }
	public string Thumb { get; set; }

	public string DevLink( string append = "/" )
	{
		return $"/{Ident}{append}";
	}
}
