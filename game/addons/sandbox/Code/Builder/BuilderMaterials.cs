namespace Sandbox;

/// <summary>
/// Loads building block materials: mounted .vmat paths, or workshop package idents.
/// </summary>
public static class BuilderMaterials
{
	static readonly Dictionary<string, Task<Material>> Downloads = new();

	/// <summary>The orange ghost a block wears while its workshop material downloads.</summary>
	public static Material Downloading => Material.Load( "materials/effects/duplicator_override_downloading.vmat" );

	/// <summary>
	/// Loading a material that doesn't exist gives a working-looking error material, so check its compiled file first.
	/// </summary>
	static bool IsMounted( string path ) => FileSystem.Mounted.FileExists( path.EndsWith( "_c", StringComparison.OrdinalIgnoreCase ) ? path : path + "_c" );

	public static bool IsLocal( string path ) => string.IsNullOrWhiteSpace( path )
		|| path.EndsWith( ".vmat", StringComparison.OrdinalIgnoreCase )
		|| path.EndsWith( ".vmat_c", StringComparison.OrdinalIgnoreCase );

	/// <summary>
	/// A mounted material, loaded now; the default for an empty or broken path. Null for a workshop ident.
	/// </summary>
	public static Material LoadLocal( string path )
	{
		if ( !IsLocal( path ) ) return null;
		if ( string.IsNullOrWhiteSpace( path ) ) path = BuilderCatalog.DefaultMaterial;

		var material = IsMounted( path ) ? Material.Load( path ) : null;
		if ( material is { IsError: false } ) return material;

		Log.Warning( $"Builder: couldn't load material '{path}', using the default" );
		return Material.Load( BuilderCatalog.DefaultMaterial );
	}

	/// <summary>
	/// Any material, fetching workshop ones. Requests for the same ident share one download. Null if it can't be loaded.
	/// </summary>
	public static Task<Material> LoadAsync( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) ) path = BuilderCatalog.DefaultMaterial;
		if ( IsLocal( path ) ) return LoadResource( path );

		if ( Downloads.TryGetValue( path, out var pending ) ) return pending;

		var download = Download( path );
		if ( !download.IsCompleted ) Downloads[path] = download;
		return download;
	}

	static async Task<Material> LoadResource( string path )
	{
		if ( !IsMounted( path ) )
		{
			Log.Warning( $"Builder: material '{path}' doesn't exist" );
			return null;
		}

		try
		{
			return await ResourceLibrary.LoadAsync<Material>( path );
		}
		catch ( Exception e )
		{
			Log.Warning( $"Builder: couldn't load material '{path}': {e.Message}" );
			return null;
		}
	}

	static async Task<Material> Download( string ident )
	{
		try
		{
			return await Cloud.Load<Material>( ident );
		}
		catch ( Exception e )
		{
			Log.Warning( $"Builder: couldn't load material '{ident}': {e.Message}" );
			return null;
		}
		finally
		{
			Downloads.Remove( ident );
		}
	}
}
