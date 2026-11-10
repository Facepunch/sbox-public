namespace Sandbox.Services;

/// <summary>
/// Requests public record summaries for up to 100 group keys in one package and table.
/// </summary>
public class StorageSummaryRequest
{
	/// <summary>
	/// Case-sensitive group keys to summarize. Duplicate keys appear once in the response.
	/// </summary>
	public string[] GroupKeys { get; set; }
}

/// <summary>
/// Public record summaries keyed by the requested group keys, including empty groups.
/// </summary>
public class StorageSummary
{
	/// <summary>
	/// Amount and creation time range for each requested group.
	/// </summary>
	public Dictionary<string, StorageGroupSummary> Groups { get; set; }
}

/// <summary>
/// Amount and creation time range of the currently public records in a group.
/// </summary>
public class StorageGroupSummary
{
	/// <summary>
	/// Number of public records currently in the group.
	/// </summary>
	public long Amount { get; set; }

	/// <summary>
	/// Earliest creation time, or null when the group has no public records.
	/// </summary>
	public DateTimeOffset? First { get; set; }

	/// <summary>
	/// Latest creation time, or null when the group has no public records.
	/// </summary>
	public DateTimeOffset? Last { get; set; }
}

/// <summary>
/// A new player-owned record. Existing keys are never overwritten by create.
/// </summary>
public class StorageCreate
{
	/// <summary>
	/// Caller-chosen key, unique within the player's package and table.
	/// </summary>
	public string Key { get; set; }

	/// <summary>
	/// Optional immutable group key, up to 128 characters.
	/// </summary>
	public string GroupKey { get; set; }

	/// <summary>
	/// Whether anyone may read the record. Defaults to private.
	/// </summary>
	public bool PublicRead { get; set; }

	/// <summary>
	/// String payload, commonly serialized JSON, up to 65536 UTF-8 bytes.
	/// </summary>
	public string Value { get; set; }
}

/// <summary>
/// A replacement payload and visibility, guarded by the current revision.
/// </summary>
public class StorageUpdate
{
	/// <summary>
	/// Replacement string payload, up to 65536 UTF-8 bytes.
	/// </summary>
	public string Value { get; set; }

	/// <summary>
	/// Whether the updated record is publicly readable.
	/// </summary>
	public bool PublicRead { get; set; }

	/// <summary>
	/// Revision returned by the previous read.
	/// </summary>
	public string Revision { get; set; }
}

/// <summary>
/// One group page, ordered by ascending record ID.
/// </summary>
public class StoragePage
{
	/// <summary>
	/// Records visible in this page.
	/// </summary>
	public StorageEntry[] Items { get; set; }

	/// <summary>
	/// Pass as afterId to continue the same query; null at the end.
	/// </summary>
	public long? NextId { get; set; }
}
