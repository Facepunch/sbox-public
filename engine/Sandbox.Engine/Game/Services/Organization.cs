using System.Collections.Concurrent;

namespace Sandbox.Services;

/// <summary>
/// An organization on Asset Party. Organizations own packages and have members.
/// </summary>
public sealed class Organization
{
	/// <summary>
	/// Unique string that identifies this organization.
	/// </summary>
	public string Ident { get; init; }

	/// <summary>
	/// Full or "nice" name of this organization.
	/// </summary>
	public string Title { get; init; }

	/// <summary>
	/// Short summary of this organization.
	/// </summary>
	public string Summary { get; init; }

	/// <summary>
	/// Full description of this organization.
	/// </summary>
	public string Description { get; init; }

	/// <summary>
	/// Link to the thumbnail image of this organization.
	/// </summary>
	public string Thumb { get; init; }

	/// <summary>
	/// Link to Twitter of this organization, if set.
	/// </summary>
	public string Twitter { get; init; }

	/// <summary>
	/// Link to the website of this organization, if set.
	/// </summary>
	public string WebUrl { get; init; }

	/// <summary>
	/// Link to the Discord of this organization, if set.
	/// </summary>
	public string Discord { get; init; }

	/// <summary>
	/// When the organization was created.
	/// </summary>
	public DateTimeOffset Created { get; init; }

	/// <summary>
	/// Number of packages owned by this organization.
	/// </summary>
	public int PackageCount { get; init; }

	/// <summary>
	/// Number of members of this organization.
	/// </summary>
	public int MemberCount { get; init; }

	/// <summary>
	/// Members of this organization.
	/// </summary>
	public Sandbox.Services.Players.Profile[] Members { get; init; }

	static ConcurrentDictionary<string, Organization> _cache = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// Fetch an organization by its ident. Cached in-memory for the session and on disk
	/// across launches — if the backend is unreachable we'll fall back to the disk copy.
	/// </summary>
	public static async Task<Organization> Get( string ident )
	{
		if ( string.IsNullOrEmpty( ident ) ) return default;

		if ( _cache.TryGetValue( ident, out var cached ) )
			return cached;

		var dto = await ServiceCache.TryFetchAsync<OrganizationDto>(
			$"org_{ident}",
			() => Sandbox.Backend.Package.GetOrganization( ident ) );

		// OrganizationDto is a struct — an empty Ident means neither backend nor disk had it.
		if ( string.IsNullOrEmpty( dto.Ident ) ) return default;

		// GetOrAdd makes concurrent Get() calls for the same ident return the same instance.
		return _cache.GetOrAdd( ident, _ => From( dto ) );
	}

	internal static Organization From( OrganizationDto x )
	{
		return new Organization
		{
			Ident = x.Ident,
			Title = x.Title,
			Summary = x.Summary,
			Description = x.Description,
			Thumb = x.Thumb,
			Twitter = x.Twitter,
			WebUrl = x.WebUrl,
			Discord = x.Discord,
			Created = x.Created,
			PackageCount = x.PackageCount,
			MemberCount = x.MemberCount,
			Members = x.Members?.Select( Sandbox.Services.Players.Profile.From ).ToArray() ?? Array.Empty<Sandbox.Services.Players.Profile>()
		};
	}
}
