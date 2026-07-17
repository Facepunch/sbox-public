using Sandbox;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Editor.Mcp;

[McpToolset( "package", "Published packages on the sbox.game backend - search and fetch details" )]
public static partial class PackageTools
{
	/// <summary>
	/// Search the backend for published packages - models, gamemodes, maps, addons. Plain words search text,
	/// and the query supports filters like 'type:model', 'type:gamemode', 'tag:fun' and 'sort:popular' mixed
	/// in with the search words.
	/// </summary>
	/// <param name="query">Search query, e.g. 'barrel type:model sort:popular'.</param>
	/// <param name="limit">How many results to return.</param>
	/// <param name="offset">How many results to skip, for paging.</param>
	[McpTool.ReadOnly( "find_packages" )]
	public static async Task<PackageSearchResult> FindPackages( string query, [Sandbox.Range( 1, 100 )] int limit = 20, int offset = 0 )
	{
		var result = await Package.FindAsync( query, limit, Math.Max( offset, 0 ) );

		return new PackageSearchResult
		{
			TotalCount = result.TotalCount,
			Packages = result.Packages.Select( x => new PackageSummary
			{
				Ident = x.FullIdent,
				Title = x.Title,
				Type = x.TypeName,
				Org = x.Org?.Title,
				Summary = x.Summary,
				Tags = x.Tags?.ToArray(),
				VotesUp = x.VotesUp,
				Updated = x.Updated.ToString( "yyyy-MM-dd" )
			} ).ToArray()
		};
	}

	/// <summary>
	/// A page of package search results.
	/// </summary>
	public class PackageSearchResult
	{
		/// <summary>How many packages matched in total, beyond this page.</summary>
		public long TotalCount { get; set; }

		public PackageSummary[] Packages { get; set; }
	}

	/// <summary>
	/// One package as it appears in search results.
	/// </summary>
	public class PackageSummary
	{
		/// <summary>The ident get_package and install_package take.</summary>
		public string Ident { get; set; }

		public string Title { get; set; }

		public string Type { get; set; }

		public string Org { get; set; }

		public string Summary { get; set; }

		public string[] Tags { get; set; }

		public int VotesUp { get; set; }

		public string Updated { get; set; }
	}

	/// <summary>
	/// Fetch a single package from the backend by ident - full details including description, tags, votes,
	/// usage stats and timestamps. Use this after find_packages to dig into one result.
	/// </summary>
	/// <param name="ident">The package ident, like 'facepunch.sandbox' or from find_packages.</param>
	[McpTool.ReadOnly( "get_package" )]
	public static async Task<PackageDetails> GetPackage( string ident )
	{
		var package = await Package.FetchAsync( ident, partial: false )
			?? throw new Exception( $"No package found for '{ident}'" );

		return new PackageDetails
		{
			Ident = package.FullIdent,
			Title = package.Title,
			Type = package.TypeName,
			Org = package.Org is null ? null : new PackageOrg { Ident = package.Org.Ident, Title = package.Org.Title },
			Summary = package.Summary,
			Description = package.Description,
			Tags = package.Tags?.ToArray(),
			Public = package.Public,
			Archived = package.Archived,
			Favourited = package.Favourited,
			VotesUp = package.VotesUp,
			VotesDown = package.VotesDown,
			EngineVersion = $"{package.EngineVersion}",
			Created = package.Created.ToString( "yyyy-MM-dd" ),
			Updated = package.Updated.ToString( "yyyy-MM-dd" )
		};
	}

	/// <summary>
	/// Everything the backend knows about one package.
	/// </summary>
	public class PackageDetails
	{
		public string Ident { get; set; }
		public string Title { get; set; }
		public string Type { get; set; }
		public PackageOrg Org { get; set; }
		public string Summary { get; set; }
		public string Description { get; set; }
		public string[] Tags { get; set; }
		public bool Public { get; set; }
		public bool Archived { get; set; }
		public int Favourited { get; set; }
		public int VotesUp { get; set; }
		public int VotesDown { get; set; }
		public string EngineVersion { get; set; }
		public string Created { get; set; }
		public string Updated { get; set; }
	}

	/// <summary>
	/// The organisation a package belongs to.
	/// </summary>
	public class PackageOrg
	{
		public string Ident { get; set; }
		public string Title { get; set; }
	}

	/// <summary>
	/// Download and install a cloud package into the project so its assets can be used - after this
	/// the returned asset path works anywhere an asset path does, e.g. spawn_model for a model
	/// package. Already-installed packages return immediately.
	/// </summary>
	/// <param name="ident">The package ident, like 'facepunch.wooden_crate' or from find_packages.</param>
	[McpTool( "install_package" )]
	public static async Task<InstalledPackage> InstallPackage( string ident )
	{
		var asset = await AssetSystem.InstallAsync( ident )
			?? throw new Exception( $"Couldn't install '{ident}' - is it a valid asset package ident from find_packages?" );

		return new InstalledPackage
		{
			Ident = ident,
			AssetPath = asset.Path,
			AssetType = asset.AssetType?.FriendlyName
		};
	}

	/// <summary>
	/// What installing a package mounted.
	/// </summary>
	public class InstalledPackage
	{
		public string Ident { get; set; }

		/// <summary>The primary asset's path - works anywhere an asset path does.</summary>
		public string AssetPath { get; set; }

		public string AssetType { get; set; }
	}
}
