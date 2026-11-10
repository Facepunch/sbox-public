using Sandbox.Services;

namespace Sandbox;

public partial class Package
{
	/// <summary>
	/// Represents the actual response from the api
	/// </summary>
	public class ListResult
	{
		/// <summary>
		/// The name of this group
		/// </summary>
		public string Title { get; set; }

		/// <summary>
		/// Unique to this fetch. Impressions and launches from this list carry it, so they can be traced
		/// back to exactly what the player was shown.
		/// </summary>
		public Guid RequestId { get; set; }

		/// <summary>
		/// The groups of packages
		/// </summary>
		public Grouping[] Groupings { get; set; }

		public class Grouping
		{
			/// <summary>
			/// Stable id of this shelf on the backend
			/// </summary>
			public Guid Id { get; set; }

			/// <summary>
			/// The title of this group
			/// </summary>
			public string Title { get; set; }

			/// <summary>
			/// The description of this group
			/// </summary>
			public string Description { get; set; }

			/// <summary>
			/// The icon of this group
			/// </summary>
			public string Icon { get; set; }

			/// <summary>
			/// The style of this group
			/// </summary>
			public string Style { get; set; }

			/// <summary>
			/// Link to get a full list of this category
			/// </summary>
			public string QueryString { get; set; }

			/// <summary>
			/// The packages in this group
			/// </summary>
			public Package[] Packages { get; set; }
		}


		internal static ListResult From( PackageGroups groups )
		{
			var result = new ListResult();
			result.Title = groups.Title;
			result.RequestId = Guid.NewGuid();
			result.Groupings = groups.Groupings?.Select( x => new Grouping
			{
				Id = x.Id,
				Title = x.Title,
				Description = x.Description,
				Icon = x.Icon,
				Style = x.Style,
				QueryString = x.QueryString,
				Packages = x.Packages.Select( y => RemotePackage.FromDto( y ) ).ToArray()
			} ).ToArray();

			return result;
		}
	}

}
