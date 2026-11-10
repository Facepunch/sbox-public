using Refit;

namespace Sandbox.Services;

public partial class ServiceApi
{
	public interface IVersionApi
	{
		[Get( "/package/versions/2/{packageIdent}" )]
		Task<PackageVersion[]> GetList( string packageIdent );
	}
}
