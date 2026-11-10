using System.Text.Json.Serialization;

namespace Sandbox.Services;

/// <summary>
/// A standard envelope for paged API responses. Carries the page of <see cref="Items"/> plus the
/// counts a client needs to render pagination. Build one with <see cref="Create"/>.
/// </summary>
public class BasePagedResponse<T>
{
	// The API serializer ignores default values, but pagination clients need these counts even when
	// they're zero (e.g. an empty result set), so force them to always serialize.

	/// <summary>The 1-based page number these items came from.</summary>
	[JsonIgnore( Condition = JsonIgnoreCondition.Never )]
	public int Page { get; set; }

	/// <summary>How many items are returned per page.</summary>
	[JsonIgnore( Condition = JsonIgnoreCondition.Never )]
	public int PerPage { get; set; }

	/// <summary>Total number of items across every page.</summary>
	[JsonIgnore( Condition = JsonIgnoreCondition.Never )]
	public int TotalItems { get; set; }

	/// <summary>Total number of pages at this page size.</summary>
	[JsonIgnore( Condition = JsonIgnoreCondition.Never )]
	public int TotalPages { get; set; }

	/// <summary>The items for this page.</summary>
	public T[] Items { get; set; } = [];

	public static BasePagedResponse<T> Create( T[] items, int page, int perPage, int totalItems )
	{
		return new BasePagedResponse<T>
		{
			Page = page,
			PerPage = perPage,
			TotalItems = totalItems,
			TotalPages = perPage > 0 ? (int)Math.Ceiling( totalItems / (double)perPage ) : 0,
			Items = items ?? [],
		};
	}
}
