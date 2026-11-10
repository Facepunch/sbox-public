using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Sandbox;

namespace Sandbox.WhitelistGen;

/// <summary>Prints reviewed candidate entries in the format consumed by <see cref="AccessRules"/>.</summary>
internal static class Program
{
	enum RuleEffect { Allow, Deny, Block, Unblock }

	static int Main( string[] args )
	{
		if ( args.Length < 1 )
		{
			Console.Error.WriteLine( "Usage: WhitelistGen \"<Assembly>/<Pattern>\" [--dll <path>] [--all] [--effect allow|deny|block|unblock]" );
			Console.Error.WriteLine( "Outputs candidate rules only; review every entry before adding it to Rules.All." );
			Console.Error.WriteLine( "  --dll     load a specific assembly instead of the runtime directory" );
			Console.Error.WriteLine( "  --all     include non-public types and methods" );
			Console.Error.WriteLine( "  --effect  select the unified rule prefix (default: allow)" );
			return 1;
		}

		var pattern = args[0];
		string dllOverride = null;
		var includeNonPublic = false;
		var effect = RuleEffect.Allow;

		for ( var i = 1; i < args.Length; i++ )
		{
			switch ( args[i] )
			{
				case "--dll" when i + 1 < args.Length:
					dllOverride = args[++i];
					break;
				case "--all":
					includeNonPublic = true;
					break;
				case "--effect" when i + 1 < args.Length && Enum.TryParse<RuleEffect>( args[i + 1], true, out var parsed ) && Enum.IsDefined( parsed ):
					effect = parsed;
					i++;
					break;
				default:
					Console.Error.WriteLine( $"Unknown or incomplete option: {args[i]}" );
					return 1;
			}
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
			Console.Error.WriteLine( $"Assembly not found: {dllPath}" );
			return 1;
		}

		// Match the full "<Assembly>/<signature>" input pattern.
		var regex = new Regex( "^" + Regex.Escape( pattern ).Replace( "\\*", ".*" ) + "$", RegexOptions.Compiled );

		// A metadata-only compilation over the whole runtime dir so referenced types (base types, parameter
		// types) resolve; SymbolSignature then produces exactly what AccessWalker matches against.
		var searchDirs = new HashSet<string>( StringComparer.OrdinalIgnoreCase ) { runtimeDir };
		if ( dllOverride != null ) searchDirs.Add( Path.GetDirectoryName( Path.GetFullPath( dllPath ) ) );

		var target = MetadataReference.CreateFromFile( Path.GetFullPath( dllPath ) );
		var references = new List<MetadataReference> { target };
		foreach ( var dir in searchDirs )
		{
			foreach ( var file in Directory.EnumerateFiles( dir, "*.dll" ) )
			{
				if ( string.Equals( Path.GetFullPath( file ), Path.GetFullPath( dllPath ), StringComparison.OrdinalIgnoreCase ) )
					continue;

				try { references.Add( MetadataReference.CreateFromFile( file ) ); }
				catch { /* skip native / non-managed dlls */ }
			}
		}

		var options = new CSharpCompilationOptions( OutputKind.DynamicallyLinkedLibrary )
			.WithMetadataImportOptions( MetadataImportOptions.All );
		var compilation = CSharpCompilation.Create( "WhitelistGen", references: references, options: options );

		if ( compilation.GetAssemblyOrModuleSymbol( target ) is not IAssemblySymbol assembly )
		{
			Console.Error.WriteLine( $"Could not load '{dllPath}' as a managed assembly." );
			return 1;
		}

		var lines = new SortedSet<string>( StringComparer.Ordinal );

		foreach ( var type in AllTypes( assembly.GlobalNamespace ) )
		{
			if ( !includeNonPublic && !IsVisible( type.DeclaredAccessibility ) )
				continue;

			var prefix = type.ContainingAssembly?.Identity.Name ?? asmName;
			var typeSignature = SymbolSignature.Type( type );
			var typeMatch = $"{prefix}/{typeSignature}";
			if ( regex.IsMatch( typeMatch ) )
				lines.Add( FormatRule( effect, prefix, typeSignature ) );

			foreach ( var method in type.GetMembers().OfType<IMethodSymbol>() )
			{
				if ( !includeNonPublic && !IsVisible( method.DeclaredAccessibility ) )
					continue;

				var methodSignature = SymbolSignature.Method( method );
				var methodMatch = $"{prefix}/{methodSignature}";
				if ( regex.IsMatch( methodMatch ) )
					lines.Add( FormatRule( effect, prefix, methodSignature ) );
			}
		}

		foreach ( var line in lines )
			Console.WriteLine( $"\t\t\"{line}\"," );

		Console.Error.WriteLine( $"// {lines.Count} candidate {effect.ToString().ToLowerInvariant()} rules matched \"{pattern}\"" );
		return 0;
	}

	static string FormatRule( RuleEffect effect, string assembly, string signature ) => effect switch
	{
		RuleEffect.Allow => $"{assembly}/{signature}",
		RuleEffect.Deny => $"!{assembly}/{signature}",
		RuleEffect.Block => $"-{signature}",
		RuleEffect.Unblock => $"+{signature}",
		_ => throw new ArgumentOutOfRangeException( nameof( effect ) )
	};

	static IEnumerable<INamedTypeSymbol> AllTypes( INamespaceSymbol ns )
	{
		foreach ( var type in ns.GetTypeMembers() )
			foreach ( var expanded in Expand( type ) )
				yield return expanded;

		foreach ( var child in ns.GetNamespaceMembers() )
			foreach ( var type in AllTypes( child ) )
				yield return type;
	}

	static IEnumerable<INamedTypeSymbol> Expand( INamedTypeSymbol type )
	{
		yield return type;

		foreach ( var nested in type.GetTypeMembers() )
			foreach ( var expanded in Expand( nested ) )
				yield return expanded;
	}

	// Sandboxed code can only reference public (and, for types it derives from, protected) members,
	// so those are the only ones the whitelist ever needs to authorize.
	static bool IsVisible( Accessibility accessibility )
		=> accessibility is Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal;
}
