using System.Collections.Generic;
using System.Threading;

namespace Sandbox.CodeUpgrader;

/// <summary>
/// Detects synced structs with incomplete hashes or unsupported network container fields.
/// </summary>
[DiagnosticAnalyzer( LanguageNames.CSharp )]
public sealed class SyncStructAnalyzer : Analyzer
{
	// Track constructed types so Box<Box<T>> is allowed; cap depth for expanding generics that never repeat.
	const int MaxDepth = 16;

	static readonly ImmutableArray<DiagnosticDescriptor> Rules = ImmutableArray.Create( Diagnostics.SyncStructHash, Diagnostics.SyncStructContainer );

	public override DiagnosticDescriptor Rule => Diagnostics.SyncStructHash;

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => Rules;

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

			if ( FindContainer( type, context.Compilation, path, context.CancellationToken ) is { } container )
			{
				context.ReportDiagnostic( Diagnostic.Create( Diagnostics.SyncStructContainer, location, property.Name, Display( type ), container ) );
				continue;
			}

			if ( hashed && FindWeakHash( type, path, context.CancellationToken ) is { } weak && reportedWeak.Add( weak ) )
				context.ReportDiagnostic( Diagnostic.Create( Diagnostics.SyncStructHash, location, property.Name, Display( weak ) ) );
		}
	}

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

			var collection = SymbolHelpers.FindInterface( type, compilation, "Sandbox.NetList`1" )
				?? SymbolHelpers.FindInterface( type, compilation, "Sandbox.NetDictionary`2" );

			if ( collection is not null )
			{
				foreach ( var argument in collection.TypeArguments )
					yield return (argument, false);
			}

			yield break;
		}

		var isDictionary = SymbolHelpers.Implements( type, compilation, "System.Collections.IDictionary" );
		var isList = !isDictionary && SymbolHelpers.Implements( type, compilation, "System.Collections.IList" );
		yield return (type, !isDictionary && !isList);

		if ( type.TypeKind == TypeKind.Interface ) yield break;

		var items = isDictionary ? SymbolHelpers.FindInterface( type, compilation, "System.Collections.Generic.IDictionary`2" )
			: isList ? SymbolHelpers.FindInterface( type, compilation, "System.Collections.Generic.IEnumerable`1" )
			: null;

		if ( items is null ) yield break;

		foreach ( var argument in items.TypeArguments )
			yield return (argument, true);
	}

	static string FindContainer( ITypeSymbol type, Compilation compilation, HashSet<ITypeSymbol> path, CancellationToken cancellationToken )
	{
		if ( path.Count >= MaxDepth || !path.Add( type ) ) return null;

		try
		{
			foreach ( var field in InstanceFields( type ) )
			{
				cancellationToken.ThrowIfCancellationRequested();

				var name = (field.AssociatedSymbol ?? field).Name;

				foreach ( var serialized in SerializedTypes( field.Type, compilation ) )
				{
					if ( SymbolHelpers.Implements( serialized, compilation, "Sandbox.INetworkSerializer" ) )
						return name;

					if ( IsSourceStruct( serialized ) && FindContainer( serialized, compilation, path, cancellationToken ) is { } nested )
						return $"{name}.{nested}";
				}
			}

			return null;
		}
		finally
		{
			path.Remove( type );
		}
	}

	static IEnumerable<ITypeSymbol> SerializedTypes( ITypeSymbol type, Compilation compilation )
	{
		type = Unwrap( type );
		yield return type;

		if ( type is IArrayTypeSymbol array )
		{
			foreach ( var item in SerializedTypes( array.ElementType, compilation ) )
				yield return item;

			yield break;
		}

		var collection = FindBase( type, compilation, "System.Collections.Generic.List`1" )
			?? FindBase( type, compilation, "System.Collections.Generic.Dictionary`2" );

		if ( collection is null ) yield break;

		foreach ( var argument in collection.TypeArguments )
		{
			foreach ( var item in SerializedTypes( argument, compilation ) )
				yield return item;
		}
	}

	static INamedTypeSymbol FindBase( ITypeSymbol type, Compilation compilation, string metadataName )
	{
		var definition = compilation.GetTypeByMetadataName( metadataName );
		if ( definition is null ) return null;

		for ( var t = type as INamedTypeSymbol; t is not null; t = t.BaseType )
		{
			if ( SymbolEqualityComparer.Default.Equals( t.OriginalDefinition, definition ) ) return t;
		}

		return null;
	}

	// Trust explicit hashes; generated record hashes still depend on each field's hash.
	static ITypeSymbol FindWeakHash( ITypeSymbol type, HashSet<ITypeSymbol> path, CancellationToken cancellationToken )
	{
		if ( path.Count >= MaxDepth || !path.Add( type ) ) return null;

		try
		{
			var hash = type.GetMembers( "GetHashCode" )
				.OfType<IMethodSymbol>()
				.FirstOrDefault( m => m.IsOverride && m.Parameters.IsEmpty );

			if ( hash is not null && !hash.IsImplicitlyDeclared ) return null;

			var fields = InstanceFields( type ).ToArray();

			if ( hash is null && fields.Length > 1 )
				return fields.Any( f => BreaksBitwiseHash( f.Type ) ) || HasPadding( type, fields ) ? type : null;

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
			path.Remove( type );
		}
	}

	// Padding makes ValueType.GetHashCode fall back to the first field.
	static bool HasPadding( ITypeSymbol type, IFieldSymbol[] fields )
	{
		if ( SymbolHelpers.HasAttribute( type, "System.Runtime.InteropServices.StructLayoutAttribute" ) ) return false;

		var offset = 0;
		var alignment = 1;
		var used = 0;

		foreach ( var field in fields )
		{
			var size = SizeOf( field.Type );
			if ( size == 0 ) return false;

			offset = AlignUp( offset, size ) + size;
			alignment = System.Math.Max( alignment, size );
			used += size;
		}

		return AlignUp( offset, alignment ) != used;
	}

	static int AlignUp( int value, int alignment ) => (value + alignment - 1) / alignment * alignment;

	static int SizeOf( ITypeSymbol type )
	{
		if ( type is INamedTypeSymbol { EnumUnderlyingType: { } underlying } ) type = underlying;

		return type.SpecialType switch
		{
			SpecialType.System_Boolean or SpecialType.System_Byte or SpecialType.System_SByte => 1,
			SpecialType.System_Char or SpecialType.System_Int16 or SpecialType.System_UInt16 => 2,
			SpecialType.System_Int32 or SpecialType.System_UInt32 => 4,
			SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_IntPtr or SpecialType.System_UIntPtr => 8,
			_ => 0,
		};
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

	static ITypeSymbol Unwrap( ITypeSymbol type )
	{
		return type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
			? nullable.TypeArguments[0]
			: type;
	}

	static bool IsSourceStruct( ITypeSymbol type ) => type.TypeKind == TypeKind.Struct && !type.DeclaringSyntaxReferences.IsEmpty;

	static string Display( ITypeSymbol type ) => type.ToDisplayString( SymbolDisplayFormat.MinimallyQualifiedFormat );

	public override async Task RunTests( IAnalyzerTest tester )
	{
		// Weak hashes in synced values and collection items.
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
			public struct Single<T> { public T Value; }
			public enum Level : byte { Low, High }
			public struct Ammo { public int Count; public bool Reloading; }
			public struct Flag { public bool Active; public int Count; }
			public struct Wide { public int Id; public long Ticks; }
			public struct Trailing { public long Ticks; public int Id; }
			public struct Leveled { public Level Level; public short Count; }
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
				[Sync] public {|SB3008:Single<Single<Item>>|} Boxed { get; set; }
				[Sync] public {|SB3008:Ammo|} Ammo { get; set; }
				[Sync] public {|SB3008:Flag|} Flag { get; set; }
				[Sync] public {|SB3008:Wide|} Wide { get; set; }
				[Sync] public {|SB3008:Trailing|} Trailing { get; set; }
				[Sync] public {|SB3008:Leveled|} Leveled { get; set; }
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

		// Complete hashes, external types and values that aren't hashed.
		await tester.TestWithMarkup(
			"""
			using Sandbox;
			using System;
			using System.Collections;
			using System.Collections.Generic;
			using System.Runtime.InteropServices;

			public record struct RecordItem( string Id, int Count );
			public record struct RecordSlot( RecordItem Item, int Index );
			public struct Item { public string Id; public int Count; }
			public struct Ints { public int Hp; public int Armor; public Slot Slot; }
			public struct Name { public string Value; }
			public struct Pair<T> { public T A; public T B; }
			public record struct RecordInts( Ints Stats, int Index );
			public struct Packed { public bool Active; public bool Visible; public short Count; }
			public struct Color { public byte R; public byte G; public byte B; }
			public struct Mixed { public int Id; public byte A; public byte B; public short C; }
			[StructLayout( LayoutKind.Sequential, Pack = 1 )] public struct Tight { public int Count; public bool Reloading; }
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
				[Sync] public Packed Packed { get; set; }
				[Sync] public Color Color { get; set; }
				[Sync] public Mixed Mixed { get; set; }
				[Sync] public Tight Tight { get; set; }
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

		// Incomplete code and expanding generic structs.
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

		// Network containers inside a synced struct only get SB3009.
		await tester.TestWithMarkup(
			"""
			using Sandbox;
			using System;
			using System.Collections.Generic;

			public struct Item { public string Id; public int Count; }
			public struct Holder { public NetDictionary<int, Item?> Inventory; }
			public struct AutoHolder { public NetList<int> List { get; set; } }
			public record struct RecordHolder( NetDictionary<int, int> Map );
			public struct Outer { public int Id; public Holder Inner; }
			public struct HashedHolder
			{
				public NetDictionary<int, Item?> Inventory;
				public override int GetHashCode() => HashCode.Combine( Inventory );
			}
			public struct Single<T> { public T Value; }
			public struct ListsHolder { public int Id; public NetList<int>[] Lists; }
			public struct MapsHolder { public List<NetDictionary<int, int>> Maps; }
			public struct TeamHolder { public Dictionary<int, Holder[]> Teams; }
			public class Player
			{
				[Sync] public {|SB3009:Holder|} Holder { get; set; }
				[Sync] public {|SB3009:AutoHolder?|} Auto { get; set; }
				[Sync] public {|SB3009:RecordHolder|} Record { get; set; }
				[Sync] public {|SB3009:Outer|} Nested { get; set; }
				[Sync] public {|SB3009:HashedHolder|} Hashed { get; set; }
				[Sync] public {|SB3009:List<Holder>|} Many { get; set; }
				[Sync] public NetDictionary<int, Item?> Inventory { get; set; } = new();
				public Holder Local { get; set; }
				[Sync] public {|SB3009:ListsHolder|} Lists { get; set; }
				[Sync] public {|SB3009:MapsHolder|} Maps { get; set; }
				[Sync] public {|SB3009:TeamHolder|} Teams { get; set; }
				[Sync] public {|SB3009:Single<Single<Holder>>|} Boxed { get; set; }
				[Sync] public {|SB3009:NetList<Holder>|} Values { get; set; } = new();
				[Sync] public {|SB3009:NetDictionary<int, Holder>|} ValueMap { get; set; } = new();
			#pragma warning disable SB3009
				[Sync] public Holder Suppressed { get; set; }
			#pragma warning restore SB3009
			}
			""" );
	}
}
