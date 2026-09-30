namespace Sandbox.Services;

/// <summary>Display details for a player's explicit hides.</summary>
public class HiddenContentDetails
{
	public List<Entry> Packages { get; set; } = [];
	public List<Entry> Organizations { get; set; } = [];

	public class Entry
	{
		public long Id { get; set; }
		public long OrganizationId { get; set; }
		public string Title { get; set; }
		public string OrganizationName { get; set; }
		public string Thumb { get; set; }
		public string Url { get; set; }
	}
}
