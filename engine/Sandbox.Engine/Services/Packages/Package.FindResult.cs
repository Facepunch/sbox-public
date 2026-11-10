
namespace Sandbox;

public partial class Package
{
	/// <summary>
	/// A result from the call to FindAsync
	/// </summary>
	public class FindResult
	{
		/// <summary>
		/// The amount of time the query took
		/// </summary>
		public double Milliseconds { get; set; }

		/// <summary>
		/// A list of packages retrieved
		/// </summary>
		public Package[] Packages { get; set; }

		/// <summary>
		/// The total amount of packages
		/// </summary>
		public int TotalCount { get; set; }

		/// <summary>
		/// Facets particular to this search
		/// </summary>
		public Facet[] Facets { get; set; }

		/// <summary>
		/// A list of tags relevant to this search
		/// </summary>
		public TagEntry[] Tags { get; set; }

		/// <summary>
		/// A list of sort orders. There may be other sort orders, but we provide a list here that can
		/// be easily used to save rewriting the same code over and over.
		/// </summary>
		public SortOrder[] Orders { get; set; }

		/// <summary>
		/// Binary options
		/// </summary>
		public PackageProperty[] Properties { get; set; }

		internal static FindResult FromDto( Services.PackageFindResult l )
		{
			return new FindResult
			{
				Packages = l.Packages.Select( x => RemotePackage.FromDto( x ) ).ToArray(),
				TotalCount = (int)l.TotalCount,
				Tags = l.Tags?.Select( x => new TagEntry( x.Key, x.Value ) ).ToArray() ?? Array.Empty<TagEntry>(),
				Facets = l.Facets?.Select( Package.Facet.FromDto ).ToArray() ?? Array.Empty<Facet>(),
				Orders = l.Orders?.Select( x => new SortOrder( x.Name, x.Title, x.Icon ) ).ToArray() ?? Array.Empty<SortOrder>(),
				Properties = l.Properties?.Select( PackageProperty.FromDto ).ToArray() ?? Array.Empty<PackageProperty>(),
			};
		}
	}

	/// <summary>
	/// Represents a tag along with the count of items it contains
	/// </summary>
	public record struct TagEntry( string Name, int Count )
	{

	}

	/// <summary>
	/// Describes a sort order which can be used with the package/find api
	/// </summary>
	public record struct SortOrder( string Name, string Title, string Icon );

	/// <summary>
	/// A binary category used to divide into two categories. For example, Work In Progress.
	/// </summary>
	public record struct PackageProperty( string Name, string Icon, string Title, string Description, int Count, bool IsExclusive )
	{
		internal static PackageProperty FromDto( Sandbox.Services.PackagePropertyTag i )
		{
			return new PackageProperty( i.Name, i.Icon, i.Title, i.Description, i.Count, i.Exclusive );
		}
	}
}
