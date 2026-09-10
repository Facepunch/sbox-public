using System.Text;

namespace Facepunch.Native;

/// <summary>
/// Writes a .slnx over a set of modules.
/// </summary>
public static class Solution
{
	public static void Write( string name, IEnumerable<Module> modules )
	{
		var ordered = modules.OrderBy( m => m.Name, StringComparer.OrdinalIgnoreCase ).ToList();
		var included = ordered.ToHashSet();

		var xml = new Xml();
		xml.Open( "<Solution>" );
		xml.Open( "<Configurations>" );
		xml.Line( @"<Platform Name=""x64"" />" );
		xml.Close( "</Configurations>" );

		foreach ( var module in ordered )
		{
			var path = Vcxproj.ProjectPath( module );
			var id = Vcxproj.ProjectGuid( module ).ToString( "D" ).ToUpperInvariant();
			var dependencies = module.Dependencies.Where( included.Contains ).OrderBy( m => m.Name, StringComparer.OrdinalIgnoreCase ).ToList();

			if ( dependencies.Count == 0 )
			{
				xml.Line( $@"<Project Path=""{path}"" Id=""{id}"" />" );
				continue;
			}

			xml.Open( $@"<Project Path=""{path}"" Id=""{id}"">" );
			foreach ( var dependency in dependencies )
				xml.Line( $@"<BuildDependency Project=""{Vcxproj.ProjectPath( dependency )}"" />" );
			xml.Close( "</Project>" );
		}

		xml.Close( "</Solution>" );

		var file = Path.Combine( Paths.SrcDir, $"{name}.slnx" );
		var content = xml.ToString();
		if ( File.Exists( file ) && File.ReadAllText( file ) == content ) return;

		File.WriteAllText( file, content, new UTF8Encoding( false ) );
	}

	/// <summary>The given modules plus everything they depend on, transitively.</summary>
	public static List<Module> WithDependencies( IEnumerable<Module> roots )
	{
		var result = new HashSet<Module>();
		var queue = new Queue<Module>( roots );

		while ( queue.Count > 0 )
		{
			var module = queue.Dequeue();
			if ( !result.Add( module ) ) continue;
			foreach ( var dependency in module.Dependencies ) queue.Enqueue( dependency );
		}

		return [.. result];
	}
}
