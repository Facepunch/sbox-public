using Sandbox.Services;
using System.Text.Json;

namespace Sandbox;

internal sealed class RemotePackage : Package
{
	public override IRevision Revision => Version;

	public override bool IsRemote => true;
	public PackageRevision Version { get; set; }

	JsonElement? cachedJson;

	public static RemotePackage FromDto( PackageDto p )
	{
		var package = new RemotePackage();
		package.UpdateFromDto( p );

		Cache( package, false );

		return package;
	}

	public static RemotePackage FromDto( PackageWrapMinimal p )
	{
		var package = new RemotePackage();
		package.UpdateFromDto( p );

		Cache( package, true );

		return package;
	}

	public void UpdateFromDto( PackageWrapMinimal p )
	{
		Org = Package.Organization.FromDto( p.Org );

		Ident = p.Ident;
		Title = p.Title;
		Summary = p.Summary;
		Thumb = p.Thumb;
		ThumbWide = p.ThumbWide ?? p.Thumb;
		ThumbTall = p.ThumbTall ?? p.Thumb;
		VideoThumb = p.VideoThumb;
		Updated = p.Updated;
		Created = p.Created;
		Tags = p.Tags;
		Favourited = p.Favourited;
		VotesUp = p.VotesUp;
		VotesDown = p.VotesDown;
		Public = p.Public;
		TypeName = p.TypeName;

		Interaction = new PackageInteraction
		{
			Favourite = p.Interaction.Favourite,
			FavouriteCreated = p.Interaction.FavouriteCreated,
			Rating = p.Interaction.Rating,
			RatingCreated = p.Interaction.RatingCreated,
			Used = p.Interaction.Used,
			FirstUsed = p.Interaction.FirstUsed,
			LastUsed = p.Interaction.LastUsed,
			Sessions = p.Interaction.Sessions,
			Seconds = p.Interaction.Seconds,
		};

		if ( p.UsageStats is not null )
		{
			Usage = new PackageUsageStats
			{
				UsersNow = p.UsageStats.UsersNow,

				Total = new PackageUsageStats.Group
				{
					Users = p.UsageStats.Total.Users,
					Seconds = p.UsageStats.Total.Seconds,
					Sessions = p.UsageStats.Total.Sessions,
				}
			};
		}
	}

	public void UpdateFromDto( PackageDto p )
	{
		Org = Package.Organization.FromDto( p.Org );

		Ident = p.Ident;
		Title = p.Title;
		Summary = p.Summary;
		Description = p.Description;
		Thumb = p.Thumb;
		ThumbWide = p.ThumbWide ?? p.Thumb;
		ThumbTall = p.ThumbTall ?? p.Thumb;
		VideoThumb = VideoThumb ?? p.Screenshots?.Where( x => x.IsVideo ).Select( x => x.Thumb ).FirstOrDefault();
		Updated = p.Updated;
		Created = p.Created;
		Tags = p.Tags;
		Favourited = p.Favourited;
		VotesUp = p.VotesUp;
		VotesDown = p.VotesDown;
		Source = p.Source;
		Public = p.Public;
		ApiVersion = p.ApiVersion;
		Screenshots = p.Screenshots?.Select( x => new Screenshot { Created = x.Created, Height = x.Height, IsVideo = x.IsVideo, Thumb = x.Thumb, Url = x.Url, Width = x.Width } ).ToArray() ?? Array.Empty<Screenshot>();
		TypeName = p.TypeName;
		PackageReferences = p.PackageReferences;
		EditorReferences = p.EditorReferences;
		ErrorRate = p.ErrorRate;

		if ( p.LatestNews is { } newsPost )
		{
			LatestNewsPost = Sandbox.Services.News.From( newsPost );
		}

		Reviews = new ReviewStats( p.ReviewStats );
		_data = p.Data;

		Interaction = new PackageInteraction
		{
			Favourite = p.Interaction.Favourite,
			FavouriteCreated = p.Interaction.FavouriteCreated,
			Rating = p.Interaction.Rating,
			RatingCreated = p.Interaction.RatingCreated,
			Used = p.Interaction.Used,
			FirstUsed = p.Interaction.FirstUsed,
			LastUsed = p.Interaction.LastUsed,
			Sessions = p.Interaction.Sessions,
			Seconds = p.Interaction.Seconds,
		};

		LoadingScreen = new LoadingScreenSetup { MediaUrl = p.LoadingScreen.MediaUrl };

		if ( p.Version is not null )
		{
			Version = new PackageRevision();
			Version.Meta = p.Version.Meta;
			Version.EngineVersion = p.Version.EngineVersion;
			Version.Created = p.Version.Created;
			Version.ManifestUrl = p.Version.ManifestUrl;
			Version.TotalSize = p.Version.TotalSize;
			Version.FileCount = p.Version.FileCount;
			Version.Changes = p.Version.Changes;
			Version.AssetVersionId = p.Version.Id;
		}

		if ( p.UsageStats is not null )
		{
			Usage = new PackageUsageStats
			{
				UsersNow = p.UsageStats.UsersNow,

				Total = new PackageUsageStats.Group
				{
					Users = p.UsageStats.Total.Users,
					Seconds = p.UsageStats.Total.Seconds,
					Sessions = p.UsageStats.Total.Sessions,
				}
			};
		}
	}

	internal override JsonElement? GetJson()
	{
		if ( Version == null ) return null;
		if ( string.IsNullOrEmpty( Version.Meta ) ) return null;
		if ( cachedJson.HasValue ) return cachedJson;

		var document = JsonDocument.Parse( Version.Meta, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip } );
		cachedJson = document.RootElement;
		return cachedJson;
	}


	Dictionary<string, object> _data;

	/// <summary>
	/// Get a data value. These are usually set on the backend, and are package type specific. These are
	/// generally values that are used to configure behaviour in the menu system.
	/// </summary>
	public override T GetValue<T>( string name, T defaultValue = default )
	{
		if ( _data is null ) return defaultValue;

		var o = _data.GetValueOrDefault( name, null );
		if ( o is null ) return defaultValue;

		if ( o is T t ) return t;

		if ( o is JsonElement je )
		{
			try
			{
				return je.Deserialize<T>( Json.options ) ?? defaultValue;
			}
			catch ( System.Exception )
			{
				return defaultValue;
			}
		}

		return defaultValue;
	}
}
