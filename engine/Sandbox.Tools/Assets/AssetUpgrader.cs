using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace Editor;

static class AssetUpgrader
{
	readonly record struct UpgradeMethod( MethodInfo Method, AssetUpgraderAttribute Attribute );

	static readonly UpgradeMethod[] BuiltinUpgraders =
	[
		.. Discover( typeof( AssetUpgraderAttribute ).Assembly ),
		.. Discover( typeof( AssetUpgrader ).Assembly )
	];

	internal static void Upgrade( Asset asset )
	{
		if ( asset.IsCloud || asset.IsDeleted )
			return;

		UpgradeFile( BuiltinUpgraders, asset.AbsoluteSourcePath, asset );
	}

	internal static bool UpgradeFile( Assembly assembly, string path )
	{
		return UpgradeFile( Discover( assembly ), path );
	}

	static bool UpgradeFile( UpgradeMethod[] upgraders, string path, Asset asset = null )
	{
		if ( string.IsNullOrEmpty( path ) )
			return false;

		try
		{
			var methods = FindUpgraders( upgraders, Path.GetExtension( path ), asset );
			if ( methods.Length == 0 || !File.Exists( path ) )
				return false;

			string source;
			Encoding encoding;
			using ( var reader = new StreamReader( path, new UTF8Encoding( false ) ) )
			{
				source = reader.ReadToEnd();
				encoding = reader.CurrentEncoding;
			}

			if ( !TryUpgrade( methods, source, out var upgraded ) )
				return false;

			// Save once after all upgrades succeed. The file watcher recompiles and reloads the asset.
			File.WriteAllText( path, upgraded, encoding );
			return true;
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Couldn't upgrade asset '{path}'." );
			return false;
		}
	}

	internal static bool TryUpgrade( Assembly assembly, string extension, string source, out string upgraded )
	{
		return TryUpgrade( FindUpgraders( Discover( assembly ), extension ), source, out upgraded );
	}

	static UpgradeMethod[] Discover( Assembly assembly )
	{
		try
		{
			const BindingFlags flags = BindingFlags.Static | BindingFlags.Public |
				BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

			return assembly.GetTypes()
				.SelectMany( type => type.GetMethods( flags ) )
				.SelectMany( method => method.GetCustomAttributes<AssetUpgraderAttribute>()
					.Where( attribute => attribute.Target == AssetUpgradeTarget.Source )
					.Select( attribute => new UpgradeMethod( method, attribute ) ) )
				.ToArray();
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Couldn't discover asset upgraders in '{assembly.FullName}'." );
			return [];
		}
	}

	static MethodInfo[] FindUpgraders( UpgradeMethod[] registered, string extension, Asset asset = null )
	{
		extension = extension.TrimStart( '.' );
		var upgraders = registered
			.Where( x => string.Equals( x.Attribute.Extension, extension, StringComparison.OrdinalIgnoreCase ) )
			.OrderBy( x => x.Attribute.Order )
			.ThenBy( x => x.Method.DeclaringType.FullName, StringComparer.Ordinal )
			.ThenBy( x => x.Method.Name, StringComparer.Ordinal )
			.ToArray();

		if ( upgraders.Length == 0 )
			return [];

		// Direct source references are reusable while the source checksum matches its compiled version.
		if ( upgraders.All( x => !string.IsNullOrEmpty( x.Attribute.SourceReference ) ) &&
			asset is NativeAsset native && native.native.IsSourceUnchangedSinceCompile() )
		{
			var references = asset.GetReferences( false ).Select( x => x?.Path )
				.Concat( asset.GetUnrecognizedReferencePaths() );

			if ( CanSkip( upgraders.Select( x => x.Attribute ), references ) )
				return [];
		}

		// Keep the full sequence: an earlier upgrade can introduce a reference needed by a later one.
		return upgraders
			.Select( x => x.Method )
			.Distinct()
			.ToArray();
	}

	internal static bool CanSkip( IEnumerable<AssetUpgraderAttribute> upgraders, IEnumerable<string> references )
	{
		var names = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		foreach ( var reference in references )
		{
			if ( string.IsNullOrEmpty( reference ) )
				return false;

			names.Add( GetReferenceName( reference ) );
		}

		// Older compiled resources may not contain a reference table.
		if ( names.Count == 0 )
			return false;

		foreach ( var upgrader in upgraders )
		{
			if ( string.IsNullOrEmpty( upgrader.SourceReference ) )
				return false;

			if ( names.Contains( GetReferenceName( upgrader.SourceReference ) ) )
				return false;
		}

		return true;
	}

	static string GetReferenceName( string path )
	{
		var name = Path.GetFileName( path.Replace( '\\', '/' ) );
		return name.EndsWith( "_c", StringComparison.OrdinalIgnoreCase ) ? name[..^2] : name;
	}

	static bool TryUpgrade( MethodInfo[] methods, string source, out string upgraded )
	{
		upgraded = source;
		foreach ( var method in methods )
		{
			try
			{
				var parameters = method.GetParameters();
				if ( method.ReturnType != typeof( string ) || method.ContainsGenericParameters ||
					parameters.Length != 1 || parameters[0].ParameterType != typeof( string ) )
					throw new InvalidOperationException( "Asset upgraders must have signature static string (string source)." );

				upgraded = (string)method.Invoke( null, [upgraded] ) ?? upgraded;
			}
			catch ( Exception e )
			{
				Log.Warning( e, $"Asset upgrader {method.DeclaringType.FullName}.{method.Name} failed; source left unchanged." );
				upgraded = source;
				return false;
			}
		}

		return upgraded != source;
	}
}
