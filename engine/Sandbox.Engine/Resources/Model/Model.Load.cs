using Sandbox.Mounting;

namespace Sandbox;

public partial class Model
{
	/// <summary>
	/// Load a model by file path.
	/// </summary>
	/// <param name="filename">The file path to load as a model.</param>
	/// <returns>The loaded model, or null</returns>
	public static Model Load( string filename )
	{
		ThreadSafe.AssertIsMainThread();

		filename = SanitizeFilename( filename );
		if ( filename is null )
			return Error;

		return LoadInternal( filename );
	}

	/// <summary>
	/// Load a model by file path.
	/// </summary>
	/// <param name="filename">The file path to load as a model.</param>
	/// <returns>The loaded model, or null</returns>
	public static async Task<Model> LoadAsync( string filename )
	{
		ThreadSafe.AssertIsMainThread();

		filename = SanitizeFilename( filename );
		if ( filename is null )
			return Error;

		// Check cache first
		if ( await Sandbox.Mounting.Directory.TryLoadAsync( filename, ResourceType.Model ) is Model m )
			return m;

		using var manifest = AsyncResourceLoader.Load( filename );
		if ( manifest is not null )
		{
			await manifest.WaitForLoad();
		}

		// TODO - make async
		return LoadInternal( filename );
	}

	/// <summary>
	/// Helper to sanitize filenames
	/// </summary>
	private static string SanitizeFilename( string filename )
	{
		if ( string.IsNullOrWhiteSpace( filename ) )
			return null;

		if ( filename.EndsWith( ".vmdl_c", StringComparison.OrdinalIgnoreCase ) )
			return filename[..^2];

		return filename;
	}

	/// <summary>
	/// Internal load logic assuming sanitized filename and main thread context.
	/// </summary>
	private static Model LoadInternal( string filename )
	{
		if ( Sandbox.Mounting.Directory.TryLoad( filename, ResourceType.Model, out object model ) && model is Model m )
			return m;

		return FromNative( NativeGlue.Resources.GetModel( filename ), name: filename );
	}
}
