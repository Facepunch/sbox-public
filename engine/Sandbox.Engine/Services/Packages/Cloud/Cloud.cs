using System.Diagnostics;
using System.Reflection;

namespace Sandbox;

/// <summary>
/// For accessing assets from the cloud - from code
/// </summary>
[Expose]
public static partial class Cloud
{
	static CaseInsensitiveDictionary<string> cached = new();

	internal static void UpdateTypes( Assembly library )
	{
		var sw = Stopwatch.StartNew();

		var foundAttributes = library.GetCustomAttributes<AssetAttribute>();

		foreach ( var attr in foundAttributes )
		{
			Log.Trace( $"{attr.PackageIdent} / {attr.AssetPath}" );
			cached[attr.PackageIdent] = attr.AssetPath;
		}

		Log.Trace( $"UpdateTypes took {sw.Elapsed.TotalMilliseconds:0.00}ms" );
	}

	/// <summary>
	/// Returns the path of the asset referenced by this package
	/// </summary>
	[CloudAssetProvider]
	public static string Asset( [StringLiteralOnly] string ident )
	{
		if ( cached.TryGetValue( ident, out var foundPath ) )
		{
			Log.Trace( $"CloudAsset: Got Cached Path {ident} / {foundPath}" );
			return foundPath;
		}

		Log.Warning( $"CloudAsset: Unknown package ({ident})" );
		return null;
	}

	/// <summary>
	/// Loads a model from the cloud by its identifier. The asset is downloaded during code compilation, so it's treated like a local model since it's shipped along with your package.
	/// <br></br>
	/// If you wish to load a model at runtime, use <see cref="Load{T}(string)"/> instead.
	/// </summary>
	/// <param name="ident">The cloud ident/url of the model</param>
	[CloudAssetProvider]
	public static Model Model( [StringLiteralOnly] string ident )
		=> GetCloudAsset( ident, Sandbox.Model.Load, () => Sandbox.Model.Load( "models/dev/error.vmdl" ) );

	/// <summary>
	/// Loads a material from the cloud by its identifier. The asset is downloaded during code compilation, so it's treated like a local material since it's shipped along with your package.
	/// <br></br>
	/// If you wish to load a material at runtime, use <see cref="Load{T}(string)"/> instead.
	/// </summary>
	/// <param name="ident">The cloud ident/url of the material</param>
	[CloudAssetProvider]
	public static Material Material( [StringLiteralOnly] string ident )
		=> GetCloudAsset( ident, Sandbox.Material.Load, () => Sandbox.Material.Load( "materials/error.vmat" ) );

	[Obsolete]
	[CloudAssetProvider]
	public static ParticleSystem ParticleSystem( [StringLiteralOnly] string ident )
		=> GetCloudAsset( ident, Sandbox.ParticleSystem.Load, () => Sandbox.ParticleSystem.Load( "particles/error/error.vpcf" ) );

	/// <summary>
	/// Loads a sound event from the cloud by its identifier. The asset is downloaded during code compilation, so it's treated like a local particle system since it's shipped along with your package.
	/// <br></br>
	/// If you wish to load a sound event at runtime, use <see cref="Load{T}(string)"/> instead.
	/// </summary>
	/// <param name="ident">The cloud ident/url of the particle system</param>
	[CloudAssetProvider]
	public static SoundEvent SoundEvent( [StringLiteralOnly] string ident )
		=> GetCloudAsset( ident, Sandbox.GameResource.Load<SoundEvent>, () => Sandbox.GameResource.Load<SoundEvent>( "sounds/error.sound" ) );

	/// <summary>
	/// Loads a shader from the cloud by its identifier. The asset is downloaded during code compilation, so it's treated like a local shader since it's shipped along with your package.
	/// <br></br>
	/// If you wish to load a shader at runtime, use <see cref="Load{T}(string)"/> instead.
	/// </summary>
	/// <param name="ident">The cloud ident/url of the shader</param>
	[CloudAssetProvider]
	public static Shader Shader( [StringLiteralOnly] string ident )
		=> GetCloudAsset( ident, Sandbox.Shader.Load, () => Sandbox.Shader.Load( "error.shader" ) );

	/// <summary>
	/// Loads a resource asynchronously from the cloud by its identifier, downloading the package if the client doesn't have it locally.
	/// </summary>
	public static async Task<T> Load<T>( string ident ) where T : Resource
	{
		var (package, primaryAsset) = await GetPackageAndPrimaryAsset( ident );

		if ( string.IsNullOrEmpty( primaryAsset ) )
		{
			Log.Warning( $"Cloud: Package {ident} has no PrimaryAsset" );
			return default;
		}

		if ( package is null ) return default;
		if ( typeof( T ) == typeof( Model ) && package.TypeName != "model" ) return default;
		if ( typeof( T ) == typeof( Material ) && package.TypeName != "material" ) return default;
		if ( typeof( T ) == typeof( Shader ) && package.TypeName != "shader" ) return default;
		if ( typeof( T ) == typeof( SoundEvent ) && package.TypeName != "sound" ) return default;
		if ( typeof( T ) == typeof( PrefabFile ) && package.TypeName != "prefab" ) return default;

		var fs = await package.MountAsync();
		if ( fs is null )
		{
			Log.Warning( $"Cloud: Package {ident} couldn't be mounted" );
			return default;
		}

		return await ResourceLibrary.LoadAsync<T>( primaryAsset );
	}

	/// <summary>
	/// Loads a cloud resource by its identifier. The <paramref name="withCode"/> argument is ignored.
	/// </summary>
	[Obsolete( "Use Load<T>( ident ). Mounting a package only loads its content." )]
	public static Task<T> Load<T>( string ident, bool withCode ) where T : Resource => Load<T>( ident );

	/// <summary>
	/// Checks if a cloud package is installed.
	/// </summary>
	public static bool IsInstalled( string ident )
	{
		return PackageManager.Find( ident ) != null;
	}

	/// <summary>
	/// Loads a cloud package asynchronously from the cloud by its identifier
	/// </summary>
	public static async Task Load( string ident )
	{
		if ( IsInstalled( ident ) )
			return;

		var (package, primaryAsset) = await GetPackageAndPrimaryAsset( ident );

		if ( string.IsNullOrEmpty( primaryAsset ) )
		{
			Log.Warning( $"Cloud: Package {ident} has no PrimaryAsset" );
			return;
		}

		if ( package is null ) return;

		var fs = await package.MountAsync();
		if ( fs is null )
		{
			Log.Warning( $"Cloud: Package {ident} couldn't be mounted" );
			return;
		}
	}

	static T GetCloudAsset<T>( string ident, Func<string, T> getAsset, Func<T> fallback = null ) where T : class
	{
		if ( cached.TryGetValue( ident, out var foundPath ) )
		{
			Log.Trace( $"CloudAsset: Got Cached Path {ident} / {foundPath}" );
			return getAsset( foundPath );
		}

		Log.Warning( $"CloudAsset: Unknown package ({ident})" );
		return fallback?.Invoke() ?? default;
	}

	static async Task<(Package, string)> GetPackageAndPrimaryAsset( string ident )
	{
		var package = await Package.FetchAsync( ident, false );
		if ( package is null || (package.IsRemote && package.Revision is null) )
		{
			// Package was not found
			return (null, null);
		}
		return (package, package.PrimaryAsset ?? "");
	}

	/// <summary>
	/// Automatically addeded to a type as a result of using Cloud.Model etc inside.
	/// </summary>
	[AttributeUsage( AttributeTargets.Assembly, AllowMultiple = true )]
	public sealed class AssetAttribute : System.Attribute
	{
		public string PackageIdent { get; }
		public string AssetPath { get; }

		public AssetAttribute( string packageIdent, string assetPath )
		{
			PackageIdent = packageIdent;
			AssetPath = assetPath;
		}
	}

	/// <summary>
	/// Tells codegen to generate a [assembly: Cloud.Asset] for this method
	/// </summary>
	[AttributeUsage( AttributeTargets.Method, AllowMultiple = false )]
	internal sealed class CloudAssetProviderAttribute : System.Attribute
	{
	}
}
