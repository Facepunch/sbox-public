using System;

namespace Sandbox;

/// <summary>
/// What the Steam Workshop says about a package's item - for its page in the menu, in place of what the backend says.
/// From <see cref="MenuUtility.FetchWorkshopItemAsync"/>.
/// </summary>
public sealed class WorkshopItemDetails
{
	/// <summary>
	/// Its Workshop id
	/// </summary>
	public ulong Id { get; init; }

	public string Title { get; init; }

	/// <summary>
	/// Its description on its page, in Steam's BBCode
	/// </summary>
	public string Description { get; init; }

	/// <summary>
	/// Its main preview image - square, as Steam shows it. Null if it hasn't got one.
	/// </summary>
	public string Thumb { get; init; }

	/// <summary>
	/// The other images on its page, in the order they're shown there. Its videos are <see cref="YouTubeVideos"/>.
	/// </summary>
	public IReadOnlyList<string> Images { get; init; } = Array.Empty<string>();

	/// <summary>
	/// The YouTube videos on its page - their YouTube ids, in the order they're shown there
	/// </summary>
	public IReadOnlyList<string> YouTubeVideos { get; init; } = Array.Empty<string>();

	/// <summary>
	/// The Steam id of whoever published it
	/// </summary>
	public long OwnerId { get; init; }

	/// <summary>
	/// Their Steam name, if we know it
	/// </summary>
	public string OwnerName { get; init; }

	public DateTimeOffset Created { get; init; }
	public DateTimeOffset Updated { get; init; }

	public int VotesUp { get; init; }
	public int VotesDown { get; init; }

	/// <summary>
	/// How many people are subscribed to it
	/// </summary>
	public long Subscriptions { get; init; }

	/// <summary>
	/// How big its files are, in bytes
	/// </summary>
	public long Size { get; init; }

	/// <summary>
	/// Its page on the Steam Workshop
	/// </summary>
	public string Url { get; init; }

	/// <summary>
	/// Its tags on the Workshop - its type, the ones picked when it was published, and for a game how it's played
	/// </summary>
	public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

	/// <summary>
	/// Its key values on the Workshop, like a game's player counts
	/// </summary>
	public IReadOnlyDictionary<string, string> KeyValues { get; init; } = new Dictionary<string, string>();

	internal static WorkshopItemDetails From( Workshop.Item item ) => new()
	{
		Id = item.Id,
		Title = item.Title,
		Description = item.Description,
		Thumb = string.IsNullOrEmpty( item.Thumb ) ? null : item.Thumb,
		Images = item.Previews.Where( x => x.Type == Workshop.PreviewType.Image && !string.IsNullOrEmpty( x.Url ) ).Select( x => x.Url ).ToArray(),
		YouTubeVideos = item.Previews.Where( x => x.Type == Workshop.PreviewType.YouTubeVideo && !string.IsNullOrEmpty( x.Url ) ).Select( x => x.Url ).ToArray(),
		OwnerId = item.Owner is { } owner ? owner.Id.Value : 0,
		OwnerName = item.Owner?.Name,
		Created = item.Created,
		Updated = item.Updated,
		VotesUp = item.VotesUp,
		VotesDown = item.VotesDown,
		Subscriptions = item.Subscriptions,
		Size = item.Size,
		Url = item.Url,
		Tags = item.Tags,
		KeyValues = item.KeyValues
	};
}
