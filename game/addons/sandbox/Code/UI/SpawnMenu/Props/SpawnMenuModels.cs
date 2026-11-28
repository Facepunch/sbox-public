namespace Sandbox;

/// <summary>
/// Identifies first-person models that shouldn't be offered as spawnable props.
/// </summary>
internal static class SpawnMenuModels
{
	// Workshop search exposes category membership through a category query, not on each package.
	internal static HashSet<string> ViewModels { get; private set; }
	static Task _request;
	static RealTimeUntil _refreshAt;

	internal static Task LoadViewModels()
	{
		if ( _request is null || (_request.IsCompleted && _refreshAt) )
			_request = RefreshViewModels();

		return _request;
	}

	static async Task RefreshViewModels()
	{
		// Keep the last complete set if the backend is unavailable or a page is incomplete.
		_refreshAt = 10;
		var models = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		var offset = 0;
		while ( true )
		{
			var result = await Package.FindAsync( "type:model category:viewmodel sort:newest", 200, offset );
			if ( result.Facets is null )
				return;

			foreach ( var model in result.Packages )
				models.Add( model.FullIdent );

			offset += result.Packages.Length;
			if ( models.Count >= result.TotalCount )
				break;

			if ( result.Packages.Length == 0 || offset >= result.TotalCount )
				return;
		}

		if ( ViewModels is null || !ViewModels.SetEquals( models ) )
			ViewModels = models;

		_refreshAt = 300;
	}

	internal static void RefreshIfLoaded()
	{
		if ( _request is not null )
			_ = LoadViewModels();
	}

	internal static bool IsVisible( SpawnlistItem item )
	{
		var (type, path, _) = SpawnlistItem.ParseIdent( item.Ident );
		if ( !item.IsContent || type is not ("mount" or "prop") || string.IsNullOrEmpty( path ) )
			return true;

		if ( type == "mount" || path.EndsWith( ".vmdl", StringComparison.OrdinalIgnoreCase ) ||
			path.EndsWith( ".vmdl_c", StringComparison.OrdinalIgnoreCase ) )
			return !IsViewModelPath( path );

		if ( !Package.TryParseIdentParts( path, out var ident ) )
			return true;

		_ = LoadViewModels();
		return ViewModels is not null && !ViewModels.Contains( Package.FormatIdent( ident.org, ident.package ) );
	}

	internal static bool IsViewModelPath( string path )
	{
		// Local and mounted models have no Workshop category. Use the first-person weapon paths.
		path = "/" + path.Replace( '\\', '/' );
		return path.Contains( "/viewmodels/", StringComparison.OrdinalIgnoreCase )
			|| path.Contains( "/viewmodel/", StringComparison.OrdinalIgnoreCase )
			|| path.Contains( "/models/v_", StringComparison.OrdinalIgnoreCase )
			|| (path.Contains( "/weapons/", StringComparison.OrdinalIgnoreCase ) &&
				(path.Contains( "/first_person/", StringComparison.OrdinalIgnoreCase ) ||
				 System.IO.Path.GetFileName( path ).StartsWith( "v_", StringComparison.OrdinalIgnoreCase )));
	}
}
