using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Facepunch.InteropGen.Parsers;

internal class GlobalParser : BaseParser
{
	// Cache compiled regex patterns for better performance
	private static readonly Regex _typeRegex = new(
		@"^(static)?[\s+]?(class|accessor) [\s+]?(.+)",
		RegexOptions.IgnoreCase | RegexOptions.Compiled
	);

	private static readonly Regex _structRegex = new(
		@"^(struct|enum|pointer) [\s+]?(.+)",
		RegexOptions.IgnoreCase | RegexOptions.Compiled
	);

	private static readonly Regex _nativeRegex = new(
		@"^(native|managed) [\s+]?(.+)",
		RegexOptions.IgnoreCase | RegexOptions.Compiled
	);

	private static readonly Regex _attributeRegex = new(
		@"^\[(.+)\]",
		RegexOptions.IgnoreCase | RegexOptions.Compiled
	);

	// Cache for parsing results to avoid repeated regex operations
	private static readonly ConcurrentDictionary<string, ParsedTypeDefinition> _typeParseCache = new();
	private static readonly ConcurrentDictionary<string, ParsedStructDefinition> _structParseCache = new();

	// Structs to hold parsed components efficiently
	private readonly struct ParsedTypeDefinition
	{
		public readonly bool IsStatic;
		public readonly string TypeKind;
		public readonly string Remaining;

		public ParsedTypeDefinition( bool isStatic, string typeKind, string remaining )
		{
			IsStatic = isStatic;
			TypeKind = typeKind;
			Remaining = remaining;
		}
	}

	private readonly struct ParsedStructDefinition
	{
		public readonly string StructKind;
		public readonly string Remaining;

		public ParsedStructDefinition( string structKind, string remaining )
		{
			StructKind = structKind;
			Remaining = remaining;
		}
	}

	public void ident( string str )
	{
		definition.Ident = str.Trim();
	}

	public void exceptions( string str )
	{
		definition.ExceptionHandlerName = str.Trim();
	}

	public void cs( string str )
	{
		definition.SaveFileCs = str.Trim();
	}

	public void csaot( string str )
	{
		definition.SaveFileCsAot = str.Trim();
	}

	public void cpp( string str )
	{
		definition.SaveFileCpp = str.Trim();
	}

	public void hpp( string str )
	{
		definition.SaveFileCppH = str.Trim();
	}

	public void @namespace( string str )
	{
		definition.ManagedNamespace = str.Trim();
	}

	public void @extern( string str )
	{
		definition.Externs.Add( str );
	}

	public void initfrom( string str )
	{
		definition.InitFrom = str.Trim();
	}

	public void stringtools( string str )
	{
		definition.StringTools = str.Trim();
	}

	public void include( string str )
	{
		if ( str.EndsWith( ".h" ) )
		{
			definition.Includes.Add( str );
			return;
		}

		if ( str.EndsWith( ".def" ) )
		{
			IncludeFile( str );
			return;
		}

		IncludeFolder( str );
	}

	public void includecpp( string str )
	{
		definition.CppIncludes.Add( str );
	}

	public void nativedll( string str )
	{
		// engine2.dll -> libengine2.so on Linux, libengine2.dylib on macOS
		string dir = Path.GetDirectoryName( str ) ?? "";
		string baseName = Path.GetFileNameWithoutExtension( str );

		if ( OperatingSystem.IsLinux() )
		{
			str = Path.Combine( dir, $"lib{baseName}.so" );
		}
		else if ( OperatingSystem.IsMacOS() )
		{
			str = Path.Combine( dir, $"lib{baseName}.dylib" );
		}

		definition.NativeDll = str.Trim();
	}

	public void inherit( string str )
	{
		string path = definition.Root + "/" + str;

		Definition d = Definition.FromFile( path );
		definition.IncludedDefinitions.Add( d );
		// Log.WriteLine($"Inherit \"{str}\"");

		Definition.Current = definition;
	}

	public void skipdefine( string str )
	{
		string path = definition.Root + "/" + str;

		Definition d = Definition.FromFile( path );
		definition.SkipDefines.Add( d );

		Definition.Current = definition;
		// Log.WriteLine( $"SkipDefine \"{str}\"" );
	}
	public void skipall( string str )
	{
		string path = definition.Root + "/" + str;

		Definition d = Definition.FromFile( path );
		definition.SkipAll.Add( d );

		Definition.Current = definition;
		// Log.WriteLine( $"SkipAll \"{str}\"" );
	}

	public void functionpointer( string strNativeTypeName )
	{
		definition.FunctionPointers.Add( strNativeTypeName.Trim( ';', ' ', '\t' ) );
	}

	public void @delegate( string strNativeTypeName )
	{
		definition.Delegates.Add( strNativeTypeName.Trim( ';', ' ', '\t' ) );
	}

	public void pch( string str )
	{
		definition.PrecompiledHeader = str;
	}

	private bool ParseTypeDefinition( bool isNative, string line )
	{
		// Check cache first
		if ( _typeParseCache.TryGetValue( line, out ParsedTypeDefinition cachedType ) )
		{
			return ProcessCachedTypeDefinition( isNative, cachedType );
		}

		Match match = _typeRegex.Match( line );
		if ( match.Success )
		{
			ParsedTypeDefinition parsed = new(
				isStatic: match.Groups[1].Success,
				typeKind: match.Groups[2].Value,
				remaining: match.Groups[3].Value
			);

			// Cache the result if cache isn't too large
			if ( _typeParseCache.Count < 500 )
			{
				_ = _typeParseCache.TryAdd( line, parsed );
			}

			return ProcessCachedTypeDefinition( isNative, parsed );
		}

		// Check struct cache
		if ( _structParseCache.TryGetValue( line, out ParsedStructDefinition cachedStruct ) )
		{
			return ProcessCachedStructDefinition( isNative, cachedStruct );
		}

		match = _structRegex.Match( line );
		if ( match.Success )
		{
			ParsedStructDefinition parsed = new(
				structKind: match.Groups[1].Value,
				remaining: match.Groups[2].Value
			);

			// Cache the result if cache isn't too large
			if ( _structParseCache.Count < 500 )
			{
				_ = _structParseCache.TryAdd( line, parsed );
			}

			return ProcessCachedStructDefinition( isNative, parsed );
		}

		return false;
	}

	private bool ProcessCachedTypeDefinition( bool isNative, ParsedTypeDefinition parsed )
	{
		Class c = Class.Parse( isNative, parsed.IsStatic, parsed.TypeKind, parsed.Remaining );
		if ( c != null )
		{
			c.TakeAttributes( Attributes );

			ClassParser parser = new( definition, c );
			subParser.Push( parser );

			// Use LINQ Any() for better performance with early termination
			if ( definition.Classes.Any( x => x.NativeNameWithNamespace == c.NativeNameWithNamespace ) )
			{
				throw new System.Exception( $"Class {c.NativeNameWithNamespace} defined more than once" );
			}

			if ( definition.Classes.Any( x => x.ManagedNameWithNamespace == c.ManagedNameWithNamespace ) )
			{
				throw new System.Exception( $"Class {c.ManagedNameWithNamespace} defined more than once" );
			}

			definition.Classes.Add( c );
			return true;
		}

		return false;
	}

	private bool ProcessCachedStructDefinition( bool isNative, ParsedStructDefinition parsed )
	{
		Struct strct = Struct.Parse( isNative, parsed.StructKind, parsed.Remaining );

		if ( definition.Structs.Any( x => x.NativeNameWithNamespace == strct.NativeNameWithNamespace ) )
		{
			throw new System.Exception( $"{strct.NativeNameWithNamespace} defined more than once" );
		}

		if ( definition.Structs.Any( x => x.ManagedNameWithNamespace == strct.ManagedNameWithNamespace ) )
		{
			throw new System.Exception( $"{strct.ManagedNameWithNamespace} defined more than once" );
		}

		strct.TakeAttributes( Attributes );
		definition.Structs.Add( strct );
		return true;
	}

	public override void ParseLine( string line )
	{
		string trimmedLine = line.Trim();

		// Type Definition - use cached regex
		Match nativeMatch = _nativeRegex.Match( trimmedLine );
		if ( nativeMatch.Success )
		{
			if ( ParseTypeDefinition( nativeMatch.Groups[1].Value == "native", nativeMatch.Groups[2].Value ) )
			{
				return;
			}
		}

		// Attribute Definition - use cached regex
		Match attributeMatch = _attributeRegex.Match( trimmedLine );
		if ( attributeMatch.Success )
		{
			Attributes.Add( attributeMatch.Groups[1].Value );
			return;
		}

		base.ParseLine( line );
	}
}
