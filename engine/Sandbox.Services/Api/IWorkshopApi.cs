using Refit;

namespace Sandbox.Services;

public partial class ServiceApi
{
	public interface IWorkshopApi
	{
		/// <summary>
		/// The Steam Workshop item an ident is linked to. 404 when there isn't one.
		/// </summary>
		[Get( "/workshop/lookup/1/{ident}" )]
		Task<WorkshopLookupResult> Lookup( string ident );
	}
}

public struct WorkshopLookupResult
{
	/// <summary>
	/// The Steam Workshop item id
	/// </summary>
	public ulong WorkshopId { get; set; }
}
