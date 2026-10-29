using System.Collections.Generic;
using System.Threading;

namespace Sandbox.CodeUpgrader;

/// <summary>
/// NetworkTable decides whether a synced value changed by comparing its hash with the last one. The default
/// struct hash (ValueType.GetHashCode) only covers every field for tightly packed, primitive-only structs,
/// otherwise edits to later fields keep the old hash and aren't sent. A record struct's generated hash uses
/// each field's own hash, so a plain struct field inside one has the same problem.
/// </summary>
[DiagnosticAnalyzer( LanguageNames.CSharp )]
public sealed class SyncStructAnalyzer : Analyzer
{
	public override DiagnosticDescriptor Rule => Diagnostics.SyncStructHash;

	public override void Init( AnalysisContext context )
	{
		context.RegisterSyntaxNodeAction( AnalyzeProperty, SyntaxKind.PropertyDeclaration );
	}

	void AnalyzeProperty( SyntaxNodeAnalysisContext context )
	{
		var syntax = (PropertyDeclarationSyntax)context.Node;
		if ( context.SemanticModel.GetDeclaredSymbol( syntax, context.CancellationToken ) is not IPropertySymbol property ) return;
		if ( !SymbolHelpers.HasAttribute( property, "Sandbox.SyncAttribute", "Sandbox.HostSyncAttribute" ) ) return;

		var location = syntax.Type.GetLocation();
		var checkedTypes = new HashSet<ITypeSymbol>( SymbolEqualityComparer.Default );
		var reportedWeak = new HashSet<ITypeSymbol>( SymbolEqualityComparer.Default );
		var path = new HashSet<ITypeSymbol>( SymbolEqualityComparer.Default );

		foreach ( var (value, hashed) in SyncedValues( property.Type, context.Compilation ) )
		{
			var type = Unwrap( value );
			if ( !IsSourceStruct( type ) || !checkedTypes.Add( type ) ) continue;

			if ( hashed && FindWeakHash( type, path, context.CancellationToken ) is { } weak && reportedWeak.Add( weak ) )
				context.ReportDiagnostic( Diagnostic.Create( Diagnostics.SyncStructHash, location, property.Name, Display( weak ) ) );
		}
	}

	/// <summary>
	/// The value itself, and whether NetworkTable hashes it whole, plus the items of collections it hashes item
	/// by item (ToHashCode / ListHasher): single-dimension arrays and concrete IList/IDictionary types. Interfaces
	/// don't say what the value really is, so their items aren't assumed to be hashed. NetList and NetDictionary
	/// track their own changes and are never hashed.
	/// </summary>
	static IEnumerable<(ITypeSymbol Type, bool Hashed)> SyncedValues( ITypeSymbol type, Compilation compilation )
	{
		if ( type is IArrayTypeSymbol array )
		{
			if ( array.Rank == 1 ) yield return (array.ElementType, true);
			yield break;
		}

		if ( SymbolHelpers.Implements( type, compilation, "Sandbox.INetworkSerializer" ) )
		{
			yield return (type, false);
			yield break;
		}

		var isDictionary = SymbolHelpers.Implements( type, compilation, "System.Collections.IDictionary" );
		var isList = !isDictionary && SymbolHelpers.Implements( type, compilation, "System.Collections.IList" );
		yield return (type, !isDictionary && !isList);

		if ( type.TypeKind == TypeKind.Interface ) yield break;

		var collection = isDictionary ? SymbolHelpers.FindInterface( type, compilation, "System.Collections.Generic.IDictionary`2" )
			: isList ? SymbolHelpers.FindInterface( type, compilation, "System.Collections.Generic.IEnumerable`1" )
			: null;

		if ( collection is null ) yield break;

		foreach ( var argument in collection.TypeArguments )
			yield return (argument, true);
	}

	/// <summary>
	/// The struct whose hash misses changes to some of its fields, if any. Without its own GetHashCode, a struct
	/// with more than one field only gets every field hashed when they're all integers, chars, bools or enums;
	/// otherwise the default hash uses its first non-null field alone. A single-field struct hashes that field,
	/// and a record struct's generated GetHashCode hashes each field with its own hash, so those are checked
	/// through their fields. A hand-written GetHashCode is trusted. Only fields known to break the full hash
	/// count, so a type parameter or unresolved field doesn't, and struct layout (padding) isn't modelled. Some
	/// broken structs aren't caught.
	/// </summary>
	static ITypeSymbol FindWeakHash( ITypeSymbol type, HashSet<ITypeSymbol> path, CancellationToken cancellationToken )
	{
		if ( !path.Add( type.OriginalDefinition ) ) return null;

		try
		{
			var hash = type.GetMembers( "GetHashCode" )
				.OfType<IMethodSymbol>()
				.FirstOrDefault( m => m.IsOverride && m.Parameters.IsEmpty );

			if ( hash is not null && !hash.IsImplicitlyDeclared ) return null;

			var fields = InstanceFields( type ).ToArray();

			if ( hash is null && fields.Length > 1 )
				return fields.Any( f => BreaksBitwiseHash( f.Type ) ) ? type : null;

			foreach ( var field in fields )
			{
				cancellationToken.ThrowIfCancellationRequested();

				var fieldType = Unwrap( field.Type );
				if ( IsSourceStruct( fieldType ) && FindWeakHash( fieldType, path, cancellationToken ) is { } weak )
					return weak;
			}

			return null;
		}
		finally
		{
			path.Remove( type.OriginalDefinition );
		}
	}

	static bool IsBitwiseHashed( ITypeSymbol type ) => type.TypeKind == TypeKind.Enum || type.SpecialType is
		SpecialType.System_Boolean or SpecialType.System_Char or
		SpecialType.System_SByte or SpecialType.System_Byte or
		SpecialType.System_Int16 or SpecialType.System_UInt16 or
		SpecialType.System_Int32 or SpecialType.System_UInt32 or
		SpecialType.System_Int64 or SpecialType.System_UInt64 or
		SpecialType.System_IntPtr or SpecialType.System_UIntPtr;

	static bool BreaksBitwiseHash( ITypeSymbol type ) => type.TypeKind != TypeKind.Error
		&& (type.IsReferenceType
			|| type.SpecialType is SpecialType.System_Single or SpecialType.System_Double
			|| (type.TypeKind == TypeKind.Struct && !IsBitwiseHashed( type )));

	// Includes auto-property backing fields: structs are serialized and record-hashed by field.
	static IEnumerable<IFieldSymbol> InstanceFields( ITypeSymbol type ) =>
		type.GetMembers().OfType<IFieldSymbol>().Where( f => !f.IsStatic && !f.IsConst );

	// Nullable<T> hashes and serializes the value it wraps.
	static ITypeSymbol Unwrap( ITypeSymbol type )
	{
		return type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
			? nullable.TypeArguments[0]
			: type;
	}

	/// <summary>
	/// Structs with source the user can edit: this project or a referenced project. Types that only exist as
	/// metadata (engine, BCL, packages) are left alone.
	/// </summary>
	static bool IsSourceStruct( ITypeSymbol type ) => type.TypeKind == TypeKind.Struct && !type.DeclaringSyntaxReferences.IsEmpty;

	static string Display( ITypeSymbol type ) => type.ToDisplayString( SymbolDisplayFormat.MinimallyQualifiedFormat );

	public override async Task RunTests( IAnalyzerTest tester )
	{
		// Plain values, nullables, arrays, concrete collections and generic structs all go through the hash.
		await tester.TestWithMarkup(
			"""
			using Sandbox;
			using System.Collections.Generic;
			using Replicated = Sandbox.SyncAttribute;

			public struct Item { public string Id; public int Count; }
			public struct Pair<T> { public T A; public T B; }
			public struct Stats { public int Hp; public float Speed; }
			public struct Optional { public int Id; public int? Count; }
			public struct Wrapper { public Item Inner; }
			public record struct Slot( Item Item, int Index );
			public record struct Outer( Slot Slot );
			public record struct Key( Item Value );
			public record struct Entry( Item Item );
			public class Inventory
			{
				[Sync] public {|SB3008:Item|} Held { get; set; }
				[Sync( SyncFlags.FromHost )] public {|SB3008:Item?|} Maybe { get; set; }
				[Sync] public {|SB3008:Item[]|} Slots { get; set; }
				[Sync] public {|SB3008:List<Item>|} List { get; set; } = new();
				[Sync] public {|SB3008:Dictionary<int, Item>|} Map { get; set; } = new();
				[Sync] public {|SB3008:Dictionary<Key, Entry>|} Entries { get; set; } = new();
				[Sync] public {|SB3008:Pair<string>|} Generic { get; set; }
				[Sync] public {|SB3008:Stats|} Stats { get; set; }
				[Sync] public {|SB3008:Optional|} Optional { get; set; }
				[Sync] public {|SB3008:Wrapper|} Wrapper { get; set; }
				[Sync] public {|SB3008:Slot|} Record { get; set; }
				[Sync] public {|SB3008:Outer|} NestedRecord { get; set; }
				[Replicated] public {|SB3008:Item|} Aliased { get; set; }
			#pragma warning disable CS0618
				[HostSync] public {|SB3008:Item|} Legacy { get; set; }
			#pragma warning restore CS0618
			}
			public struct Tagged<T> { public string Name; public T Value; }
			public class Box<T>
			{
				[Sync] public {|SB3008:Tagged<T>|} Value { get; set; }
			}
			""" );

		// Structs whose hash covers every field, types the user can't change, and values that aren't hashed.
		await tester.TestWithMarkup(
			"""
			using Sandbox;
			using System;
			using System.Collections;
			using System.Collections.Generic;

			public record struct RecordItem( string Id, int Count );
			public record struct RecordSlot( RecordItem Item, int Index );
			public struct Item { public string Id; public int Count; }
			public struct Ints { public int Hp; public int Armor; public Slot Slot; }
			public struct Name { public string Value; }
			public struct Pair<T> { public T A; public T B; }
			public record struct RecordInts( Ints Stats, int Index );
			public struct HashedItem
			{
				public string Id;
				public int Count;
				public override bool Equals( object obj ) => obj is HashedItem other && other.Id == Id && other.Count == Count;
				public override int GetHashCode() => HashCode.Combine( Id, Count );
			}
			public struct NameList : IList
			{
				public List<string> Names;
				public int Version;
				public object this[int index] { get => Names[index]; set => Names[index] = (string)value; }
				public bool IsFixedSize => false;
				public bool IsReadOnly => false;
				public int Count => Names.Count;
				public bool IsSynchronized => false;
				public object SyncRoot => null;
				public int Add( object value ) { Names.Add( (string)value ); return Names.Count - 1; }
				public void Clear() => Names.Clear();
				public bool Contains( object value ) => Names.Contains( (string)value );
				public int IndexOf( object value ) => Names.IndexOf( (string)value );
				public void Insert( int index, object value ) => Names.Insert( index, (string)value );
				public void Remove( object value ) => Names.Remove( (string)value );
				public void RemoveAt( int index ) => Names.RemoveAt( index );
				public void CopyTo( Array array, int index ) => ((ICollection)Names).CopyTo( array, index );
				public IEnumerator GetEnumerator() => Names.GetEnumerator();
			}
			public enum Slot { Head, Body }
			public class Inventory
			{
				[Sync] public RecordItem Record { get; set; }
				[Sync] public RecordSlot NestedRecord { get; set; }
				[Sync] public HashedItem Hashed { get; set; }
				[Sync] public List<RecordItem> RecordList { get; set; }
				[Sync] public Ints Ints { get; set; }
				[Sync] public Name Name { get; set; }
				[Sync] public Pair<int> IntPair { get; set; }
				[Sync] public RecordInts RecordInts { get; set; }
				[Sync] public NameList Names { get; set; }
				[Sync] public Slot Slot { get; set; }
				[Sync] public int Count { get; set; }
				[Sync] public Vector3 Position { get; set; }
				[Sync] public Guid Id { get; set; }
				[Sync] public KeyValuePair<int, int> Pair { get; set; }
				[Sync] public Item[,] Grid { get; set; }
				[Sync] public IList<Item> ListInterface { get; set; }
				[Sync] public IReadOnlyList<Item> ReadOnlyList { get; set; }
				[Sync] public IDictionary<int, Item> MapInterface { get; set; }
				[Sync] public IReadOnlyDictionary<string, Item> ReadOnlyMap { get; set; }
				[Sync] public ISet<Item> Set { get; set; }
				[Sync] public IEnumerable<Item> Sequence { get; set; }
				[Sync] public NetList<Item> NetList { get; set; } = new();
				[Sync] public NetDictionary<int, Item> NetMap { get; set; } = new();
				[Sync] public NetDictionary<int, Item?> NetMapNullable { get; set; } = new();
				public Item Local { get; set; }
				[Property] public Item Edited { get; set; }
			#pragma warning disable SB3008
				[Sync] public Item Suppressed { get; set; }
			#pragma warning restore SB3008
			}
			public class Box<T>
			{
				[Sync] public Pair<T> Value { get; set; }
			}
			namespace Other
			{
				public class SyncAttribute : System.Attribute {}
				public class Inventory
				{
					[Sync] public Item Held { get; set; }
				}
			}
			""" );

		// Code that doesn't compile yet gets no warning, and a recursive generic struct doesn't hang the analyzer.
		await tester.TestWithMarkup(
			"""
			using Sandbox;
			using System.Collections.Generic;

			public struct Broken { public int A; public {|CS0246:Missing|} B; }
			public struct Expanding<T> { public Expanding<List<T>> {|CS0523:Next|}; }
			public class Inventory
			{
				[Sync] public Broken Broken { get; set; }
				[Sync] public Expanding<int> Expanding { get; set; }
			}
			""" );
	}
}
