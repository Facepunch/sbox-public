using Sandbox.Services;
using System.Collections.Immutable;
using System.Text.Json.Serialization;
using System.Threading;

namespace Sandbox;

/// <summary>
/// Represents an asset on <a href="https://asset.party/">Asset Party</a>.
/// </summary>
public partial class Package
{
	/// <summary>
	/// Whether this is a remote or a locally installed package.
	/// </summary>
	public virtual bool IsRemote => false;

	/// <summary>
	/// The owner of this package.
	/// </summary>
	public Organization Org { get; set; }

	/// <summary>
	/// Full unique identity of this package.
	/// </summary>
	public string FullIdent => FormatIdent( Org.Ident, Ident, local: !IsRemote );

	/// <summary>
	/// Unique identity of this package within its <see cref="Org">organization.</see>.
	/// </summary>
	public string Ident { get; set; }

	/// <summary>
	/// A "nice" name of this package, which will be shown to players in UI.
	/// </summary>
	public string Title { get; set; }

	/// <summary>
	/// A short summary of the package.
	/// </summary>
	public string Summary { get; set; }

	/// <summary>
	/// Full description of the package.
	/// </summary>
	public string Description { get; set; }

	/// <summary>
	/// Link to the thumbnail image of this package.
	/// </summary>
	public string Thumb { get; set; }

	/// <summary>
	/// Link to the thumbnail image of this package.
	/// </summary>
	public string ThumbWide { get; protected set; }

	/// <summary>
	/// Link to the thumbnail image of this package.
	/// </summary>
	public string ThumbTall { get; protected set; }

	/// <summary>
	/// Link to the thumbnail video of this package.
	/// </summary>
	public string VideoThumb { get; set; }

	/// <summary>
	/// Engine version this package was uploaded with.
	/// This is useful for when the base game undergoes large API changes.
	/// </summary>
	public int EngineVersion { get; set; }

	/// <summary>
	/// List of tags for this package.
	/// </summary>
	public virtual string[] Tags { get; set; }

	/// <summary>
	/// List of packages that this package depends on. These will be downloaded and installed when
	/// installing this package.
	/// </summary>
	public string[] PackageReferences { get; set; }

	/// <summary>
	/// List of packages that this package depended on during editing.
	/// </summary>
	public string[] EditorReferences { get; set; }

	/// <summary>
	/// What kind of package it is.
	/// </summary>
	[System.Obsolete( "Use TypeName to determine the type" )]
	public Type PackageType { get; set; }

	/// <summary>
	/// What kind of package it is.
	/// </summary>
	public string TypeName { get; set; }

	/// <summary>
	/// Whether this package is public or hidden.
	/// </summary>
	public bool Public { get; set; }

	/// <summary>
	/// Whether this package is archived or not.
	/// </summary>
	public bool Archived { get; set; }

	/// <summary>
	/// The total size of this package in MB. This only applies to packages from Asset Party, the total file size
	/// of local packages are not calculated.
	/// </summary>
	public float FileSize => (Revision?.TotalSize ?? 0) / MathF.Pow( 1024, 2 );

	/// <summary>
	/// Statistics for user interactions with this package
	/// </summary>
	public struct PackageUsageStats
	{
		public struct Group
		{
			/// <summary>
			/// Unique Users
			/// </summary>
			public long Users { get; set; }

			/// <summary>
			/// Total combined user-seconds
			/// </summary>
			public long Seconds { get; set; }

			/// <summary>
			/// Total combined user-seconds
			/// </summary>
			public long Sessions { get; set; }
		}

		/// <summary>
		/// Total lifetime usage stats
		/// </summary>
		public Group Total { get; set; }

		/// <summary>
		/// Usage for the last 3 days
		/// </summary>
		public Group Month { get; set; }

		/// <summary>
		/// Usage for the last week
		/// </summary>
		public Group Week { get; set; }

		/// <summary>
		/// Usage for the last 24 hours
		/// </summary>
		public Group Day { get; set; }

		/// <summary>
		/// How many users are using it right now
		/// </summary>
		public long UsersNow { get; set; }

		/// <summary>
		/// The trend is a number that represents whether it's been popular recently. Higher means more popular.
		/// </summary>
		[Obsolete]
		public double Trend { get; set; }
	}

	/// <summary>
	/// Statistics for user interactions with this package
	/// </summary>
	[JsonPropertyName( "UsageStats" )]
	public PackageUsageStats Usage { get; set; }


	/// <summary>
	/// Number of players who added this package to their favourites.
	/// </summary>
	public int Favourited { get; set; }

	/// <summary>
	/// Number of players who voted this package up.
	/// </summary>
	public int VotesUp { get; set; }

	/// <summary>
	/// Number of players who voted this package down.
	/// </summary>
	public int VotesDown { get; set; }

	/// <summary>
	/// Link to this package's sources, if set.
	/// </summary>
	public string Source { get; set; }

	/// <summary>
	/// For game extension compatibility. Game targeting extensions are only compatible with that game
	/// if the API Versions match.
	/// </summary>
	public int ApiVersion { get; set; }

	/// <summary>
	/// A list of screenshots
	/// </summary>
	public Screenshot[] Screenshots { get; set; }

	public class Screenshot
	{
		public DateTime Created { get; set; }
		public int Width { get; set; }
		public int Height { get; set; }
		public string Url { get; set; }
		public string Thumb { get; set; }

		/// <summary>
		/// True if this is a loading screen rather than a regular screenshot
		/// </summary>
		public bool IsVideo { get; set; }

		/// <summary>
		/// Return the URL of a thumbnail matching this exact size. For caching reasons it's going to be best if
		/// we can keep this to round number sizes (256, 512 etc) rather than trying to exact fit.
		/// </summary>
		public string GetThumbUrl( int width, int height ) => Thumb.Replace( "{width}", width.ToString() ).Replace( "{height}", height.ToString() );
	}

	/// <summary>
	/// True if this asset is in our favourite list.
	/// </summary>
	[JsonIgnore]
	public bool IsFavourite => AccountInformation.IsFavourite( FullIdent );

	/// <summary>
	/// True if we're a member of this package's organization.
	/// </summary>
	[JsonIgnore]
	public bool CanEdit => Org != null && AccountInformation.HasOrganization( Org.Ident );// || AccountInformation.HasOrganization( "facepunch" );

	/// <summary>
	/// A link to this asset on our backend
	/// </summary>
	[JsonIgnore]
	public string Url => $"https://sbox.game/{Org.Ident}/{Ident}";

	/// <summary>
	/// When the entry was last updated. If these are different between packages
	/// then something updated on the backend.
	/// </summary>
	public DateTimeOffset Updated { get; set; }

	/// <summary>
	/// When the package was originally created.
	/// </summary>
	public DateTimeOffset Created { get; set; }

	/// <summary>
	/// How many collections we're in (roughly)
	/// </summary>
	public int Collections { get; set; }

	/// <summary>
	/// How many packages we're referencing (roughly)
	/// </summary>
	public int Referencing { get; set; }

	/// <summary>
	/// How many packages we're referenced by (roughly)
	/// </summary>
	public int Referenced { get; set; }

	public readonly struct ReviewStats
	{
		/// <summary>
		/// Gets the total number of ratings, including positive, negative, and promise ratings.
		/// </summary>
		public readonly int Total => PositiveRatings + NegativeRatings + PromiseRatings;

		/// <summary>
		/// A normalized score from 0 to 1, where 1 means all ratings are positive.
		/// </summary>
		public readonly float Score;

		/// <summary>
		/// Gets the number of positive ratings associated with the item.
		/// </summary>
		public readonly int PositiveRatings;

		/// <summary>
		/// Gets the number of negative ratings associated with the item.
		/// </summary>
		public readonly int NegativeRatings;

		/// <summary>
		/// Represents the number of promise ratings associated with the current instance.
		/// </summary>
		public readonly int PromiseRatings;

		/// <summary>
		/// Gets a read-only dictionary containing the count of each positive review tag associated with the item.
		/// </summary>
		public readonly ImmutableDictionary<Review.PositiveTags, int> PositiveTags;

		/// <summary>
		/// Gets a read-only dictionary containing the negative review tags and their corresponding counts.
		/// </summary>
		public readonly ImmutableDictionary<Review.NegativeTags, int> NegativeTags;

		internal ReviewStats( PackageReviewStats reviews )
		{
			Score = reviews?.ToPercentage() ?? 0;
			PositiveRatings = reviews?.PositiveRatings ?? 0;
			NegativeRatings = reviews?.NegativeRatings ?? 0;
			PromiseRatings = reviews?.PromiseRatings ?? 0;
			PositiveTags = reviews?.PositiveTags.ToImmutableDictionary( x => (Review.PositiveTags)x.Key, x => x.Value ) ?? ImmutableDictionary<Review.PositiveTags, int>.Empty;
			NegativeTags = reviews?.NegativeTags.ToImmutableDictionary( x => (Review.NegativeTags)x.Key, x => x.Value ) ?? ImmutableDictionary<Review.NegativeTags, int>.Empty;
		}
	}

	/// <summary>
	/// Stats for the reviews. Gives the number of reviews, and the fraction of the total score.
	/// </summary>
	public ReviewStats Reviews { get; set; }

	/// <summary>
	/// What fraction of users got errors from this package in the last day
	/// </summary>
	public float ErrorRate { get; set; }

	/// <summary>
	/// The latest news post created by this package
	/// </summary>
	public Sandbox.Services.News LatestNewsPost { get; set; }

	/// <summary>
	/// Represents an organization on Asset Party. Organization owns packages.
	/// </summary>
	public class Organization
	{
		/// <summary>
		/// Unique string that identifies this organization.
		/// </summary>
		public string Ident { get; set; }

		/// <summary>
		/// Full or "nice" name of this organization.
		/// </summary>
		public string Title { get; set; }

		/// <summary>
		/// Link to Twitter of this organization, if set.
		/// </summary>
		public string SocialTwitter { get; set; }

		/// <summary>
		/// Link to the website of this organization, if set.
		/// </summary>
		public string SocialWeb { get; set; }

		/// <summary>
		/// Description of this organization.
		/// </summary>
		public string Description { get; set; }

		/// <summary>
		/// Link to the thumbnail image of this organization.
		/// </summary>
		public string Thumb { get; set; }

		/// <summary>
		/// When the organization was created.
		/// </summary>
		public DateTimeOffset Created { get; set; }

		internal static Organization FromDto( Services.OrganizationDto x )
		{
			var o = new Organization();
			o.Ident = x.Ident;
			o.Title = x.Title;
			o.SocialTwitter = x.Twitter;
			o.SocialWeb = x.WebUrl;
			o.Description = x.Description;
			o.Thumb = x.Thumb;
			return o;
		}
	}

	public enum Type : int
	{
		Map = 1,
		[Obsolete( "Use Game" )]
		Gamemode = 2,
		Game = 2,
		Tool = 3,
		Content = 4,
		Model = 5,
		Material = 6,
		Sound = 7,

		[Obsolete( "Use Asset" )]
		Soundscape = 8,

		[Obsolete( "Use Asset" )]
		Shader = 9,

		Addon = 10,

		[Obsolete( "Use Asset" )]
		Particle = 11,

		[Obsolete( "Use Asset" )]

		Texture = 12,
		Library = 20,
		Asset = 100,
		Collection = 1000
	}

	public interface IRevision
	{
		/// <summary>
		/// Unique index of this revision.
		/// </summary>
		public long VersionId { get; }

		/// <summary>
		/// Number of files in this revision.
		/// </summary>
		public long FileCount { get; }

		/// <summary>
		/// Total size of all the files in this revision, in bytes.
		/// </summary>
		public long TotalSize { get; }

		/// <summary>
		/// A summary of the changes in this revision.
		/// </summary>
		public string Summary { get; }

		/// <summary>
		/// When this revision was created.
		/// </summary>
		public DateTimeOffset Created { get; }

		/// <summary>
		/// Engine version of this revision.
		/// TODO: How exactly is this different from <see cref="Package.EngineVersion"/>?
		/// </summary>
		public int EngineVersion { get; }

		/// <summary>
		/// Manifest of the revision, describing what files are available. For this to be available
		/// you should call DownloadManifestAsync first.
		/// </summary>
		public ManifestSchema Manifest { get; }

		/// <summary>
		/// The manifest will not be immediately available until you've downloaded it.
		/// </summary>
		public Task DownloadManifestAsync( CancellationToken token = default );
	}

	/// <summary>
	/// Information about the current package revision/version.
	/// </summary>
	public virtual IRevision Revision => default;

	/// <summary>
	/// Returns true if the org and ident of the passed in ident matches this package
	/// </summary>
	internal bool IsNamed( string ident )
	{
		if ( !Package.TryParseIdent( ident, out var parsed ) )
			return false;

		if ( !string.Equals( Org.Ident, parsed.org, System.StringComparison.OrdinalIgnoreCase ) )
			return false;

		if ( !string.Equals( Ident, parsed.package, System.StringComparison.OrdinalIgnoreCase ) )
			return false;

		return true;
	}

	/// <summary>
	/// Describes the authenticated user's interactions with this package. This is only available
	/// clientside for specific users in order to show things like play history state, favourite
	/// status and whether they have rated the item or not.
	/// </summary>
	public PackageInteraction Interaction { get; set; }

	public struct PackageInteraction
	{
		public bool Favourite { get; set; }
		public DateTimeOffset? FavouriteCreated { get; set; }
		public int? Rating { get; set; }
		public DateTimeOffset? RatingCreated { get; set; }
		public bool Used { get; set; }
		public DateTimeOffset? FirstUsed { get; set; }
		public DateTimeOffset? LastUsed { get; set; }
		public long Sessions { get; set; }
		public long Seconds { get; set; }
	}

	internal virtual IEnumerable<string> EnumeratePackageReferences()
	{
		if ( PackageReferences is not null )
		{
			foreach ( var p in PackageReferences )
				yield return p;
		}
	}

	/// <summary>
	/// If this package is a game, it can provide media to show on the loading screen
	/// </summary>
	public LoadingScreenSetup LoadingScreen { get; set; }

	public struct LoadingScreenSetup
	{
		/// <summary>
		/// The URL to an image or video to use as a loading screen. The extension should reveal its type.
		/// </summary>
		public string MediaUrl { get; set; }
	}

	/// <summary>
	/// Get a data value. These are usually set on the backend, and are package type specific. These are
	/// generally values that are used to configure behaviour in the menu system.
	/// </summary>
	public virtual T GetValue<T>( string name, T defaultValue = default )
	{
		return defaultValue;
	}
}
