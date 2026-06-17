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

		if ( string.IsNullOrWhiteSpace( filename ) )
			return Error;

		filename = filename?.Replace( ".vmdl_c", ".vmdl" );

		if ( Sandbox.Mounting.Directory.TryLoad( filename, ResourceType.Model, out object model ) && model is Model m )
			return m;

		if ( Game.Resources.TryGet<Model>( filename, out var resource ) )
			return resource;

		return FromNative( NativeGlue.Resources.GetModel( filename ), name: filename );
	}

	/// <summary>
	/// Load a model by file path.
	/// </summary>
	/// <param name="filename">The file path to load as a model.</param>
	/// <returns>The loaded model, or null</returns>
	public static async Task<Model> LoadAsync( string filename )
	{
		ThreadSafe.AssertIsMainThread();

		if ( string.IsNullOrWhiteSpace( filename ) )
			return Error;

		filename = filename?.Replace( ".vmdl_c", ".vmdl" );

		if ( await Sandbox.Mounting.Directory.TryLoadAsync( filename, ResourceType.Model ) is Model m )
			return m;

		if ( Game.Resources.TryGet<Model>( filename, out var resource ) )
			return resource;

		using var manifest = AsyncResourceLoader.Load( filename );
		if ( manifest is not null )
		{
			await manifest.WaitForLoad();
		}

		// TODO - make async
		return Load( filename );
	}
}
