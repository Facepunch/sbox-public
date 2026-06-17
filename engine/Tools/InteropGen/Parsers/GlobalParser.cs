using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Facepunch.InteropGen.Parsers;

/// <summary>
/// Top-level .def parser: handles the file directives (see <see cref="Directives"/>) and the
/// native/managed class, struct, enum and pointer declarations, pushing a <see cref="ClassParser"/>
/// for each class body.
/// </summary>
internal class GlobalParser : BaseParser
{
	private static readonly Regex TypeRegex = new( @"^(static)?[\s+]?(class|accessor) [\s+]?(.+)", RegexOptions.IgnoreCase );
	private static readonly Regex StructRegex = new( @"^(struct|enum|pointer) [\s+]?(.+)", RegexOptions.IgnoreCase );
	private static readonly Regex NativeRegex = new( @"^(native|managed) [\s+]?(.+)", RegexOptions.IgnoreCase );

	/// <summary>
	/// The top-level .def directives: keyword → what it does with its argument.
	/// </summary>
	private static readonly Dictionary<string, Action<GlobalParser, string>> Directives = new()
	{
		["ident"] = ( p, a ) => p.definition.Ident = a.Trim(),
		["exceptions"] = ( p, a ) => p.definition.ExceptionHandlerName = a.Trim(),
		["cs"] = ( p, a ) => p.definition.SaveFileCs = a.Trim(),
		["cpp"] = ( p, a ) => p.definition.SaveFileCpp = a.Trim(),
		["hpp"] = ( p, a ) => p.definition.SaveFileCppH = a.Trim(),
		["namespace"] = ( p, a ) => p.definition.ManagedNamespace = a.Trim(),
		["include"] = ( p, a ) => p.Include( a ),
		["nativedll"] = ( p, a ) => p.NativeDll( a ),
		["inherit"] = ( p, a ) => p.LoadInto( p.definition.IncludedDefinitions, a ),
		["skipall"] = ( p, a ) => p.LoadInto( p.definition.SkipAll, a ),
		["delegate"] = ( p, a ) => p.definition.Delegates.Add( a.Trim( ';', ' ', '\t' ) ),
		["pch"] = ( p, a ) => p.definition.PrecompiledHeader = a,
	};

	protected override bool TryHandleDirective( string keyword, string arg )
	{
		if ( Directives.TryGetValue( keyword, out Action<GlobalParser, string> handler ) )
		{
			handler( this, arg );
			return true;
		}

		return false;
	}

	private void Include( string str )
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

	private void NativeDll( string str )
	{
		string dir = Path.GetDirectoryName( str ) ?? "";
		string baseName = Path.GetFileNameWithoutExtension( str );

		// normalize to forward slashes
		definition.NativeDll = string.IsNullOrEmpty( dir ) ? baseName : $"{dir}/{baseName}".Replace( '\\', '/' );
	}

	/// <summary>
	/// inherit/skipall both load another .def and add it to one of our lists.
	/// </summary>
	private void LoadInto( List<Definition> list, string str )
	{
		Definition d = InteropPipeline.Build( definition.Root + "/" + str );
		list.Add( d );
	}

	private bool ParseTypeDefinition( bool isNative, string line )
	{
		Match match = TypeRegex.Match( line );
		if ( match.Success )
		{
			Class c = Class.Parse( isNative, match.Groups[1].Success, match.Groups[2].Value, match.Groups[3].Value );
			if ( c == null )
			{
				return false;
			}

			c.TakeAttributes( Attributes, definition );
			subParser.Push( new ClassParser( definition, c ) );

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

		match = StructRegex.Match( line );
		if ( match.Success )
		{
			Struct strct = Struct.Parse( isNative, match.Groups[1].Value, match.Groups[2].Value );

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

		return false;
	}

	public override void ParseLine( string line )
	{
		string trimmedLine = line.Trim();

		Match nativeMatch = NativeRegex.Match( trimmedLine );
		if ( nativeMatch.Success )
		{
			if ( ParseTypeDefinition( nativeMatch.Groups[1].Value == "native", nativeMatch.Groups[2].Value ) )
			{
				return;
			}
		}

		Match attributeMatch = AttributeRegex.Match( trimmedLine );
		if ( attributeMatch.Success )
		{
			Attributes.Add( attributeMatch.Groups[1].Value );
			return;
		}

		base.ParseLine( line );
	}
}
