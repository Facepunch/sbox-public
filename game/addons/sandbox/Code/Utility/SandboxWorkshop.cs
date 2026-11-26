using Sandbox.Modals;

namespace Sandbox;

/// <summary>
/// Preview format and Workshop metadata used when publishing and browsing Sandbox saves and dupes.
/// </summary>
internal static class SandboxWorkshop
{
	internal static Bitmap CreatePreview() => new( 1280, 720 );

	internal static Dictionary<string, string> DupeFilters( string category = null ) =>
		ItemFilters( "dupe", "1", category );

	internal static Dictionary<string, string> SaveFilters( string map, string category = null )
	{
		var filters = ItemFilters( "save", SaveSystem.SaveVersion.ToString(), category );
		filters["map"] = map.ToLowerInvariant();
		return filters;
	}

	static Dictionary<string, string> ItemFilters( string type, string protocol, string category )
	{
		var filters = new Dictionary<string, string> { ["type"] = type, ["protocol"] = protocol };
		if ( category is not null ) filters["Category"] = category;
		return filters;
	}

	internal static WorkshopPublishOptions PublishOptions<TCategory>( string title, Dictionary<string, string> filters, string categoryLabel = "Category" )
		where TCategory : struct, Enum
	{
		var options = new WorkshopPublishOptions { Title = title, KeyValues = filters };
		options.AddCategory<TCategory>( "Category", categoryLabel );
		return options;
	}
}
