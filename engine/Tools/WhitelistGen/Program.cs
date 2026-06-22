using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Mono.Cecil;
using Sandbox;

namespace Sandbox.WhitelistGen;

/// <summary>
/// Expands an access-control whitelist wildcard pattern into the full, explicit list of every type
/// and method it would match - in the exact "touch name" format the scanner compares against (via
/// <see cref="AccessSignature"/>). Paste the output into a Rules file and comment out the entries you
/// don't want to allow, so the whitelist stays explicit (no wildcards) and new members can't sneak in.
/// </summary>
internal static class Program
{
	static int Main( string[] args )
	{
		if ( args.Length < 1 )
		{
			Console.Error.WriteLine( "Usage: WhitelistGen \"<Assembly>/<Pattern>\" [--dll <path>] [--all]" );
			Console.Error.WriteLine( "  e.g. WhitelistGen \"System.Private.CoreLib/System.Memory*\"" );
			Console.Error.WriteLine( "  --dll  load a specific assembly instead of resolving from the runtime dir" );
			Console.Error.WriteLine( "  --all  include non-public types/methods (default: public + protected only)" );
			return 1;
		}

		var pattern = args[0];
		string dllOverride = null;
		var includeNonPublic = false;

		for ( var i = 1; i < args.Length; i++ )
		{
			if ( args[i] == "--dll" && i + 1 < args.Length ) dllOverride = args[++i];
			else if ( args[i] == "--all" ) includeNonPublic = true;
		}

		var slash = pattern.IndexOf( '/' );
		if ( slash <= 0 )
		{
			Console.Error.WriteLine( "Pattern must be '<Assembly>/<Pattern>', e.g. System.Private.CoreLib/System.Memory*" );
			return 1;
		}

		var asmName = pattern.Substring( 0, slash );
		var runtimeDir = RuntimeEnvironment.GetRuntimeDirectory();
		var dllPath = dllOverride ?? Path.Combine( runtimeDir, asmName + ".dll" );

		if ( !File.Exists( dllPath ) )
		{
			Console.Error.WriteLine( $"Couldn't find '{dllPath}'. Pass --dll <path> to point at the assembly." );
			return 1;
		}

		// Same wildcard -> regex translation as AccessRules.AddRule.
		var regex = new Regex( "^" + Regex.Escape( pattern ).Replace( "\\*", ".*" ) + "$", RegexOptions.Compiled );

		var resolver = new DefaultAssemblyResolver();
		resolver.AddSearchDirectory( runtimeDir );
		if ( dllOverride != null ) resolver.AddSearchDirectory( Path.GetDirectoryName( Path.GetFullPath( dllPath ) ) );

		using var assembly = AssemblyDefinition.ReadAssembly( dllPath, new ReaderParameters { AssemblyResolver = resolver } );

		var lines = new SortedSet<string>( StringComparer.Ordinal );

		foreach ( var type in AllTypes( assembly.MainModule.Types ) )
		{
			if ( !includeNonPublic && !IsTypeVisible( type ) )
				continue;

			var typeLine = AccessSignature.Type( type );
			if ( regex.IsMatch( typeLine ) )
				lines.Add( typeLine );

			foreach ( var method in type.Methods )
			{
				if ( !includeNonPublic && !IsMethodVisible( method ) )
					continue;

				var methodLine = AccessSignature.Method( method );
				if ( regex.IsMatch( methodLine ) )
					lines.Add( methodLine );
			}
		}

		foreach ( var line in lines )
			Console.WriteLine( $"\t\t\t\"{line}\"," );

		Console.Error.WriteLine( $"// {lines.Count} entries matched \"{pattern}\"" );
		return 0;
	}

	static IEnumerable<TypeDefinition> AllTypes( IEnumerable<TypeDefinition> types )
	{
		foreach ( var type in types )
		{
			yield return type;

			if ( type.HasNestedTypes )
			{
				foreach ( var nested in AllTypes( type.NestedTypes ) )
					yield return nested;
			}
		}
	}

	// Sandboxed code can only reference public (and, for types it derives from, protected) members,
	// so those are the only ones the whitelist ever needs to authorize.
	static bool IsTypeVisible( TypeDefinition t )
		=> t.IsPublic || t.IsNestedPublic || t.IsNestedFamily || t.IsNestedFamilyOrAssembly;

	static bool IsMethodVisible( MethodDefinition m )
		=> m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly;
}
