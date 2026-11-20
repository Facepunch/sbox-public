using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Sandbox.ShaderCompiler.Slang;

namespace Sandbox.ShaderCompiler;

/// <summary>What a record field holds, per component.</summary>
public enum RecordFieldKind
{
	/// <summary>32-bit floats.</summary>
	Float,
	/// <summary>32-bit signed integers.</summary>
	Int,
	/// <summary>32-bit unsigned integers.</summary>
	UInt,
	/// <summary>Another record, defined before it (<see cref="RecordField.Struct"/>).</summary>
	Record,
}

/// <summary>
/// A record field: its name, kind, component count (1 to 4), array length (0 for none), byte offset in the record and
/// what its definition's <c>///</c> comment says.
/// </summary>
public sealed record RecordField( string Name, RecordFieldKind Kind, int Components, int ArrayLength, int Offset, string Summary )
{
	/// <summary>The record a <see cref="RecordFieldKind.Record"/> field holds (its <see cref="Components"/> are its words); null for others.</summary>
	public string Struct { get; init; }

	/// <summary>Bytes of one element: four per component.</summary>
	public int Stride => Components * 4;

	/// <summary>Bytes of the field, every element.</summary>
	public int Size => Stride * Math.Max( ArrayLength, 1 );
}

/// <summary>
/// A record Borealis writes into a GPU buffer and its shaders read: a struct of the record definitions
/// (<c>sbox/records.slangh</c>), its fields in order, each four-byte aligned with no padding (the layout
/// <c>ByteAddressBuffer.Load&lt;T&gt;</c> reads, as for material records).
/// </summary>
public sealed record RecordLayout( string Name, int Size, IReadOnlyList<RecordField> Fields, string Summary )
{
	/// <summary>A field by name, or null.</summary>
	public RecordField Field( string name ) => Fields.FirstOrDefault( f => f.Name == name );

	/// <summary>Lay out <paramref name="fields"/> (offsets ignored) in order.</summary>
	public static RecordLayout Create( string name, IEnumerable<RecordField> fields, string summary = null )
	{
		var laidOut = new List<RecordField>();
		var offset = 0;
		foreach ( var field in fields )
		{
			laidOut.Add( field with { Offset = offset } );
			offset += field.Size;
		}

		return new RecordLayout( name, offset, laidOut, summary );
	}
}

/// <summary>
/// The record definitions: every struct of <c>sbox/records.slangh</c>, reflected by Slang. The one place a record's layout
/// is written; <see cref="RecordWriter"/> makes the C# structs the engine writes them with and the HLSL header of offsets
/// its shaders read them at. See docs/reference/borealis-records.md.
/// </summary>
public sealed record RecordDefinitions( IReadOnlyList<RecordLayout> Records, string Hash )
{
	/// <summary>The definitions' file, relative to the core shaders.</summary>
	public const string File = "sbox/records.slangh";

	/// <summary>A record by name, or null.</summary>
	public RecordLayout Record( string name ) => Records.FirstOrDefault( r => r.Name == name );

	/// <summary>
	/// The hash of a definitions file's text (line endings aside) that the generated files carry, so what reads them can
	/// tell they were made from the definitions it has.
	/// </summary>
	public static string HashOf( string text )
	{
		var normal = text.Replace( "\r\n", "\n", StringComparison.Ordinal );
		return Convert.ToHexString( System.Security.Cryptography.SHA256.HashData( Encoding.UTF8.GetBytes( normal ) ) )[..16];
	}
}

/// <summary>The structs of a record definitions file, from Slang's reflection.</summary>
internal static unsafe partial class RecordReflection
{
	/// <summary>
	/// Each struct declared in <paramref name="text"/>, in order, with the <c>///</c> comments before it and its fields.
	/// Fields are <c>float</c>, <c>int</c>, <c>uint</c>, <c>float2</c> to <c>float4</c>, a record declared before, or fixed
	/// arrays of those.
	/// </summary>
	public static List<RecordLayout> Read( IntPtr reflection, string text, string path )
	{
		var records = new List<RecordLayout>();
		foreach ( Match match in Struct().Matches( text ) )
		{
			var type = match.Groups["name"].Value;
			var bytes = Encoding.UTF8.GetBytes( type + "\0" );
			IntPtr structType;
			fixed ( byte* n = bytes ) structType = SlangApi.FindTypeByName( reflection, n );
			if ( structType == IntPtr.Zero || SlangApi.TypeGetKind( structType ) != SlangApi.TypeKindStruct )
				throw new ShaderCompileException( path, $"The record {type} isn't a struct Slang can reflect" );

			var comments = FieldComments( text, match.Index + match.Length );
			var fields = new List<RecordField>();
			var count = SlangApi.TypeGetFieldCount( structType );
			for ( uint i = 0; i < count; i++ )
			{
				var field = SlangApi.TypeGetFieldByIndex( structType, i );
				var name = SlangApi.String( SlangApi.VariableGetName( field ) );
				var fieldType = SlangApi.VariableGetType( field );
				var arrayLength = 0;
				if ( SlangApi.TypeGetKind( fieldType ) == SlangApi.TypeKindArray )
				{
					arrayLength = (int)SlangApi.TypeGetElementCount( fieldType );
					fieldType = SlangApi.TypeGetElementType( fieldType );
					if ( arrayLength <= 0 ) throw new ShaderCompileException( path, $"{type}.{name}: a record's arrays have a fixed length" );
				}

				var inner = SlangApi.TypeGetKind( fieldType ) == SlangApi.TypeKindStruct ? records.Find( r => r.Name == SlangApi.String( SlangApi.TypeGetName( fieldType ) ) ) : null;
				var (kind, components) = inner is not null ? (RecordFieldKind.Record, inner.Size / 4)
					: Classify( fieldType ) ?? throw new ShaderCompileException( path, $"{type}.{name}: record fields are float, int, uint, float2 to float4, a record declared before, or fixed arrays of those" );
				fields.Add( new RecordField( name, kind, components, arrayLength, 0, comments.GetValueOrDefault( name ) ) { Struct = inner?.Name } );
			}

			if ( fields.Count == 0 ) throw new ShaderCompileException( path, $"The record {type} has no fields" );

			// An array's elements are its name and index in C#: none may be another field's name
			var names = fields.Select( f => f.Name ).ToHashSet();
			foreach ( var array in fields.Where( f => f.ArrayLength > 0 ) )
			{
				for ( int e = 0; e < array.ArrayLength; e++ )
				{
					if ( names.Contains( $"{array.Name}{e}" ) ) throw new ShaderCompileException( path, $"{type}.{array.Name}{e}: element {e} of {array.Name} has that name in C#; rename the field" );
				}
			}

			records.Add( RecordLayout.Create( type, fields, Comment( text, match.Index ) ) );
		}

		return records;
	}

	static (RecordFieldKind Kind, int Components)? Classify( IntPtr type )
	{
		switch ( SlangApi.TypeGetKind( type ) )
		{
			case SlangApi.TypeKindScalar:
				return Scalar( SlangApi.TypeGetScalarType( type ) ) is { } s ? (s, 1) : null;
			case SlangApi.TypeKindVector:
				var count = (int)SlangApi.TypeGetElementCount( type );
				return count is >= 2 and <= 4 && Scalar( SlangApi.TypeGetScalarType( SlangApi.TypeGetElementType( type ) ) ) is RecordFieldKind.Float ? (RecordFieldKind.Float, count) : null;
			default:
				return null;
		}
	}

	static RecordFieldKind? Scalar( int scalar ) => scalar switch
	{
		SlangApi.ScalarFloat32 => RecordFieldKind.Float,
		SlangApi.ScalarInt32 => RecordFieldKind.Int,
		SlangApi.ScalarUInt32 => RecordFieldKind.UInt,
		_ => null,
	};

	/// <summary>The <c>///</c> lines just before <paramref name="at"/>, joined; null for none.</summary>
	static string Comment( string text, int at )
	{
		var lines = new List<string>();
		var end = at;
		while ( end > 0 )
		{
			var lineEnd = text.LastIndexOf( '\n', Math.Max( end - 1, 0 ) );
			if ( lineEnd < 0 ) break;
			var lineStart = text.LastIndexOf( '\n', Math.Max( lineEnd - 1, 0 ) ) + 1;
			if ( lineStart > lineEnd ) break;
			var line = text[lineStart..lineEnd].Trim();
			if ( !line.StartsWith( "///", StringComparison.Ordinal ) ) break;
			lines.Insert( 0, line[3..].Trim() );
			end = lineStart;
		}

		return lines.Count == 0 ? null : string.Join( " ", lines );
	}

	/// <summary>Each field's <c>///</c> comment in the struct body that starts at <paramref name="body"/>, by field name.</summary>
	static Dictionary<string, string> FieldComments( string text, int body )
	{
		var comments = new Dictionary<string, string>();
		var close = text.IndexOf( "};", body, StringComparison.Ordinal );
		if ( close < 0 ) return comments;
		foreach ( Match field in FieldLine().Matches( text[..close], body ) )
		{
			if ( Comment( text, field.Index ) is { } comment ) comments[field.Groups["name"].Value] = comment;
		}

		return comments;
	}

	[GeneratedRegex( @"^[ \t]*struct\s+(?<name>\w+)\s*\{", RegexOptions.Multiline )]
	private static partial Regex Struct();

	[GeneratedRegex( @"^[ \t]*\w+[ \t]+(?<name>\w+)[ \t]*(\[[^\]]*\])?[ \t]*;", RegexOptions.Multiline )]
	private static partial Regex FieldLine();
}

public sealed partial class SlangCompiler
{
	/// <summary>
	/// The record definitions in the core shaders (<see cref="RecordDefinitions.File"/> under the first search path), or
	/// those of the file at <paramref name="path"/>. Throws <see cref="ShaderCompileException"/> with Slang's messages on errors.
	/// </summary>
	public RecordDefinitions ReflectRecords( string path = null )
	{
		path = Path.GetFullPath( path ?? Path.Combine( searchPaths[0], RecordDefinitions.File ) );
		var text = File.ReadAllText( path );
		return SlangWorkers.Run( () =>
		{
			using var zone = Zone( CompilePhase.Reflect );
			using var request = Request( StageKind.Compute, [Path.GetDirectoryName( path )], null );
			var unit = Path.ChangeExtension( path, ".records.slang" );
			request.AddSource( unit, text );
			request.SkipCodegen();
			request.Compile( unit );
			return new RecordDefinitions( RecordReflection.Read( request.Reflection, text, path ), RecordDefinitions.HashOf( text ) );
		}, null ).GetAwaiter().GetResult();
	}
}
