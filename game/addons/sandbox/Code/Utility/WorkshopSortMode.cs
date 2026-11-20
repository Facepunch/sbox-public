/// <summary>
/// A friendly sort mode for Storage/Workshop queries, wrapping <see cref="Storage.SortOrder"/>.
/// </summary>
public enum WorkshopSortMode
{
	Popular,
	Newest,
	Trending
}

public static class WorkshopSortModeExtensions
{
	/// <summary>
	/// Converts to the underlying <see cref="Storage.SortOrder"/> value.
	/// </summary>
	public static Storage.SortOrder ToSortOrder( this WorkshopSortMode mode )
	{
		return mode switch
		{
			WorkshopSortMode.Popular => Storage.SortOrder.RankedByVote,
			WorkshopSortMode.Newest => Storage.SortOrder.RankedByPublicationDate,
			WorkshopSortMode.Trending => Storage.SortOrder.RankedByTrend,
			_ => Storage.SortOrder.RankedByVote
		};
	}
}
