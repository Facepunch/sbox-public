namespace Sandbox.Services;

public class PackageGroups
{
	public string Title { get; set; }
	public List<Grouping> Groupings { get; set; }
	public double Milliseconds { get; set; }

	public struct Grouping
	{
		public Guid Id { get; set; }
		public string Title { get; set; }
		public string Style { get; set; }
		public string Icon { get; set; }
		public string QueryString { get; set; }
		public List<PackageWrapMinimal> Packages { get; set; }
		public int Order { get; set; }
	}
}
