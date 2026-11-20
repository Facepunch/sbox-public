namespace HotloadTests;

/// <summary>
/// Upgrading collections, arrays and weak references holding swapped types.
/// </summary>
[TestClass]
public class CollectionTest : HotloadFixtureTestBase
{
	[TestMethod]
	public async Task HashSetOfStructs()
	{
		await HotloadFixtureAsync(
			"""
			using System;
			using System.Collections.Generic;

			public struct ExampleStruct : IEquatable<ExampleStruct>
			{
				public readonly int Field;

				public ExampleStruct( int field ) => Field = field;

				public bool Equals( ExampleStruct other ) => Field == other.Field;
				public override bool Equals( object obj ) => obj is ExampleStruct other && Equals( other );
				public override int GetHashCode() => Field;
			}

			public class HashSetOfStructs : IHotloadTestFixture
			{
				public readonly HashSet<ExampleStruct> HashSet = new();

				void IHotloadTestFixture.Test()
				{
			-		HashSet.Add( new ExampleStruct( 2 ) );
			-		HashSet.Add( new ExampleStruct( 5 ) );
			-		HashSet.Add( new ExampleStruct( 9 ) );
			+		Assert.AreEqual( 3, HashSet.Count );
			+		Assert.IsTrue( HashSet.Contains( new ExampleStruct( 2 ) ) );
			+		Assert.IsTrue( HashSet.Contains( new ExampleStruct( 5 ) ) );
			+		Assert.IsTrue( HashSet.Contains( new ExampleStruct( 9 ) ) );
				}
			}
			""" );
	}

	[TestMethod]
	public async Task HashSetOfClasses()
	{
		await HotloadFixtureAsync(
			"""
			using System;
			using System.Collections.Generic;

			public class ExampleClass : IEquatable<ExampleClass>
			{
				public readonly int Field;

				public ExampleClass( int field ) => Field = field;

				public bool Equals( ExampleClass other ) => other is not null && Field == other.Field;
				public override bool Equals( object obj ) => obj is not null && obj.GetType() == GetType() && Equals( (ExampleClass)obj );
				public override int GetHashCode() => Field;
			}

			public class HashSetOfClasses : IHotloadTestFixture
			{
				public readonly HashSet<ExampleClass> HashSet = new();

				void IHotloadTestFixture.Test()
				{
			-		HashSet.Add( new ExampleClass( 2 ) );
			-		HashSet.Add( new ExampleClass( 5 ) );
			-		HashSet.Add( new ExampleClass( 9 ) );
			+		Assert.AreEqual( 3, HashSet.Count );
			+		Assert.IsTrue( HashSet.Contains( new ExampleClass( 2 ) ) );
			+		Assert.IsTrue( HashSet.Contains( new ExampleClass( 5 ) ) );
			+		Assert.IsTrue( HashSet.Contains( new ExampleClass( 9 ) ) );
				}
			}
			""" );
	}

	[TestMethod]
	public async Task ConcurrentDictionary()
	{
		await HotloadFixtureAsync(
			"""
			using System.Collections.Concurrent;

			public class Item
			{
				public int IntProperty;
			}

			public class ConcurrentDictionary : IHotloadTestFixture
			{
				public static ConcurrentDictionary<int, Item> Dictionary;

				void IHotloadTestFixture.Test()
				{
			-		Dictionary = new ConcurrentDictionary<int, Item>();
			-		Dictionary.TryAdd( 1, new Item { IntProperty = 1 } );
			-		Dictionary.TryAdd( 2, new Item { IntProperty = 2 } );
			-		Dictionary.TryAdd( 3, new Item { IntProperty = 3 } );
			+		Assert.IsNotNull( Dictionary );
			+		Assert.AreEqual( 3, Dictionary.Count );
			+		Assert.AreEqual( 2, Dictionary[2].IntProperty );
				}
			}
			""" );
	}

	[TestMethod]
	public async Task MultiDimIntArray()
	{
		await HotloadFixtureAsync(
			"""
			public class MultiDimIntArray : IHotloadTestFixture
			{
				public int[,] Array;

				void IHotloadTestFixture.Test()
				{
			-		Array = new int[10, 20];
			-		Array[7, 3] = 290;
			+		Assert.AreEqual( 10, Array.GetLength( 0 ) );
			+		Assert.AreEqual( 20, Array.GetLength( 1 ) );
			+		Assert.AreEqual( 290, Array[7, 3] );
				}
			}
			""" );
	}

	[TestMethod]
	public async Task MultiDimObjectArray()
	{
		await HotloadFixtureAsync(
			"""
			public class Item
			{
				public int IntField;
			}

			public class MultiDimObjectArray : IHotloadTestFixture
			{
				public Item[,] Array;

				void IHotloadTestFixture.Test()
				{
			-		Array = new Item[10, 20];
			-		Array[7, 3] = new Item { IntField = 91 };
			+		Assert.AreEqual( 10, Array.GetLength( 0 ) );
			+		Assert.AreEqual( 20, Array.GetLength( 1 ) );
			+		Assert.AreEqual( 91, Array[7, 3].IntField );
				}
			}
			""" );
	}

	/// <summary>
	/// Test struct block copy of a multi-dimensional array.
	/// </summary>
	[TestMethod]
	public async Task MultiDimStructArray()
	{
		await HotloadFixtureAsync(
			"""
			public struct ExampleStruct
			{
				public int Value;
			}

			public class MultiDimStructArray : IHotloadTestFixture
			{
				public ExampleStruct[,] Array;

				void IHotloadTestFixture.Test()
				{
			-		Array = new ExampleStruct[10, 20];
			-		Array[7, 3] = new ExampleStruct { Value = 73 };
			+		Assert.AreEqual( 10, Array.GetLength( 0 ) );
			+		Assert.AreEqual( 20, Array.GetLength( 1 ) );
			+		Assert.AreEqual( 73, Array[7, 3].Value );
				}
			}
			""" );
	}

	/// <summary>
	/// A multi-dimensional array of a struct that gains a field, so can't be block copied.
	/// </summary>
	[TestMethod]
	public async Task MultiDimChangedStructArray()
	{
		await HotloadFixtureAsync(
			"""
			public struct ExampleStruct
			{
				public int Value;
			+	public int Added;
			}

			public class MultiDimChangedStructArray : IHotloadTestFixture
			{
				public ExampleStruct[,] Array;

				void IHotloadTestFixture.Test()
				{
			-		Array = new ExampleStruct[10, 20];
			-		Array[7, 3] = new ExampleStruct { Value = 73 };
			+		Assert.AreEqual( 10, Array.GetLength( 0 ) );
			+		Assert.AreEqual( 20, Array.GetLength( 1 ) );
			+		Assert.AreEqual( 73, Array[7, 3].Value );
				}
			}
			""" );
	}

	/// <summary>
	/// Test struct array block copy.
	/// </summary>
	[TestMethod]
	public async Task BlockCopyArray()
	{
		var result = await HotloadFixtureAsync(
			"""
			using Sandbox;

			public struct BlockCopyableStruct
			{
				public int IntValue;
				public float FloatValue;
				public Vector3 InnerStructValue;
			}

			public class BlockCopyArray : IHotloadTestFixture
			{
				public BlockCopyableStruct[] Array;

				void IHotloadTestFixture.Test()
				{
			-		Array = new BlockCopyableStruct[1_000_000];
			-		Array[123_456] = new BlockCopyableStruct { IntValue = 1928, FloatValue = 100f, InnerStructValue = new Vector3( 1f, 2f, 3f ) };
			+		Assert.AreEqual( 1_000_000, Array.Length );
			+		Assert.AreEqual( 1928, Array[123_456].IntValue );
			+		Assert.AreEqual( 100f, Array[123_456].FloatValue );
			+		Assert.AreEqual( new Vector3( 1f, 2f, 3f ), Array[123_456].InnerStructValue );
				}
			}
			""" );

		Assert.AreEqual( 1, result.TypeTimings["BlockCopyableStruct[]"].Instances );
		Assert.IsFalse( result.TypeTimings.ContainsKey( "BlockCopyableStruct" ) );
	}

	/// <summary>
	/// Test struct list block copy.
	/// </summary>
	[TestMethod]
	public async Task BlockCopyList()
	{
		var result = await HotloadFixtureAsync(
			"""
			using System.Collections.Generic;
			using Sandbox;

			public struct BlockCopyableStruct
			{
				public int IntValue;
				public float FloatValue;
				public Vector3 InnerStructValue;
			}

			public class BlockCopyList : IHotloadTestFixture
			{
				public List<BlockCopyableStruct> List;

				void IHotloadTestFixture.Test()
				{
			-		List = new List<BlockCopyableStruct>( new BlockCopyableStruct[1_000_000] );
			-		List[123_456] = new BlockCopyableStruct { IntValue = 1928, FloatValue = 100f, InnerStructValue = new Vector3( 1f, 2f, 3f ) };
			+		Assert.AreEqual( 1_000_000, List.Count );
			+		Assert.AreEqual( 1928, List[123_456].IntValue );
			+		Assert.AreEqual( 100f, List[123_456].FloatValue );
			+		Assert.AreEqual( new Vector3( 1f, 2f, 3f ), List[123_456].InnerStructValue );
				}
			}
			""" );

		Assert.AreEqual( 1, result.TypeTimings["System.Collections.Generic.List<BlockCopyableStruct>"].Instances );
		Assert.IsFalse( result.TypeTimings.ContainsKey( "BlockCopyableStruct" ) );
	}

	/// <summary>
	/// Test struct list block copy when the list's backing array is larger than its count.
	/// </summary>
	[TestMethod]
	public async Task BlockCopyListSpareCapacity()
	{
		var result = await HotloadFixtureAsync(
			"""
			using System.Collections.Generic;

			public struct BlockCopyableStruct
			{
				public int Index;
			}

			public class BlockCopyListSpareCapacity : IHotloadTestFixture
			{
				public List<BlockCopyableStruct> List = new();

				void IHotloadTestFixture.Test()
				{
			-		for ( var i = 0; i < 1_000_000; ++i )
			-		{
			-			List.Add( new BlockCopyableStruct { Index = i } );
			-		}
			+		Assert.AreEqual( 1_000_000, List.Count );
			+		Assert.AreEqual( 123_456, List[123_456].Index );
				}
			}
			""" );

		Assert.AreEqual( 1, result.TypeTimings["System.Collections.Generic.List<BlockCopyableStruct>"].Instances );
		Assert.IsFalse( result.TypeTimings.ContainsKey( "BlockCopyableStruct" ) );
	}

	[TestMethod]
	public async Task DictionaryComparer()
	{
		await HotloadFixtureAsync(
			"""
			using System;
			using System.Collections.Generic;

			public class DictionaryComparer : IHotloadTestFixture
			{
				public Dictionary<string, int> Dictionary;

				void IHotloadTestFixture.Test()
				{
			-		Dictionary = new( StringComparer.OrdinalIgnoreCase ) { { "TEST", 28 } };
			+		Assert.IsTrue( Dictionary.ContainsKey( "TEST" ) );
			+		Assert.IsTrue( Dictionary.ContainsKey( "test" ) );
			+		Assert.AreEqual( 28, Dictionary["test"] );
				}
			}
			""" );
	}

	/// <summary>
	/// A collection whose type and comparer are unchanged is upgraded in place. A replacement would
	/// be empty until the late queue fills it, and code that runs during the swap, such as a replaced
	/// type's static constructor, would read that empty collection.
	/// </summary>
	[TestMethod]
	public async Task UnchangedDictionaryKeepsItsInstance()
	{
		using var fixture = await CreateFixtureAsync(
			"""
			using System;
			using System.Collections.Generic;

			public class UnchangedDictionaryKeepsItsInstance : IHotloadTestFixture
			{
				public Dictionary<string, int> Dictionary;

				void IHotloadTestFixture.Test()
				{
			-		Dictionary = new( StringComparer.OrdinalIgnoreCase ) { { "TEST", 28 } };
			-
					Assert.AreEqual( 1, Dictionary.Count );
					Assert.AreEqual( 28, Dictionary["test"] );
				}
			}
			""" );

		var dictionary = GetField( fixture.OldInstance, "Dictionary" );

		await fixture.HotloadAsync();

		Assert.AreSame( dictionary, GetField( fixture.Instance, "Dictionary" ) );
	}

	[TestMethod]
	public async Task DictionaryComparerSwappedValues()
	{
		await HotloadFixtureAsync(
			"""
			using System;
			using System.Collections.Generic;

			public class Item
			{
				public int IntField;
			}

			public class DictionaryComparerSwappedValues : IHotloadTestFixture
			{
				public Dictionary<string, Item> Dictionary;

				void IHotloadTestFixture.Test()
				{
			-		Dictionary = new( StringComparer.OrdinalIgnoreCase ) { { "TEST", new Item { IntField = 29 } } };
			+		Assert.IsTrue( Dictionary.ContainsKey( "TEST" ) );
			+		Assert.IsTrue( Dictionary.ContainsKey( "test" ) );
			+		Assert.AreEqual( 29, Dictionary["test"].IntField );
				}
			}
			""" );
	}

	[TestMethod]
	public async Task SortedDictionaryComparer()
	{
		await HotloadFixtureAsync(
			"""
			using System;
			using System.Collections.Generic;

			public class Item
			{
				public int IntField;
			}

			public class SortedDictionaryComparer : IHotloadTestFixture
			{
				public SortedDictionary<string, Item> Dictionary;

				void IHotloadTestFixture.Test()
				{
			-		Dictionary = new( StringComparer.OrdinalIgnoreCase ) { { "TEST", new Item { IntField = 29 } } };
			+		Assert.IsTrue( Dictionary.ContainsKey( "TEST" ) );
			+		Assert.IsTrue( Dictionary.ContainsKey( "test" ) );
			+		Assert.AreEqual( 29, Dictionary["test"].IntField );
				}
			}
			""" );
	}

	[TestMethod]
	public async Task DictionaryOrder()
	{
		// Lots of adds and removes leave gaps in the dictionary's entries, which affect enumeration order.
		// Both versions make the same changes, so the second can check the upgraded order against its own.

		await HotloadFixtureAsync(
			"""
			using System;
			using System.Collections.Generic;
			using System.Linq;

			public class StringWrapper
			{
				public static implicit operator StringWrapper( string x ) => new StringWrapper { Value = x };

				public string Value;
			}

			public class DictionaryOrder : IHotloadTestFixture
			{
				public Dictionary<int, StringWrapper> Dictionary;

				private static Dictionary<int, StringWrapper> Populate()
				{
					var dictionary = new Dictionary<int, StringWrapper>();
					var random = new Random( 123 );

					for ( var j = 0; j < 1_000_000; ++j )
					{
						var index = random.Next( 0, 1_000 );

						if ( !dictionary.Remove( index ) )
						{
							dictionary.Add( index, $"Item_{j}" );
						}
					}

					return dictionary;
				}

				void IHotloadTestFixture.Test()
				{
			-		Dictionary = Populate();
			+		var expected = Populate().Values.Select( x => x.Value ).ToArray();
			+		var actual = Dictionary.Values.Select( x => x.Value ).ToArray();
			+
			+		CollectionAssert.AreEqual( expected, actual );
				}
			}
			""" );
	}

	[TestMethod]
	public async Task ClearedList()
	{
		// Processing the cleared backing array would count its whole capacity (131,072)

		var result = await HotloadFixtureAsync(
			"""
			using System.Collections.Generic;

			public struct ListElement
			{
				public object Object;
			}

			public class ClearedList : IHotloadTestFixture
			{
				public List<ListElement> List { get; } = new();

				void IHotloadTestFixture.Test()
				{
			-		for ( var i = 0; i < 100_000; ++i )
			-		{
			-			List.Add( new ListElement() );
			-		}
			-
			-		List.Clear();
			-		List.Add( new ListElement() );
			-
					Assert.AreEqual( 1, List.Count );
				}
			}
			""" );

		Assert.IsFalse( result.InstancesProcessed > 100_000 );
	}

	[TestMethod]
	public async Task CollectionExpressionSingle()
	{
		// The compiler generates a collection type for this, so both versions need it

		await HotloadFixtureAsync(
			"""
			using System.Collections.Generic;
			using System.Linq;

			public class CollectionExpressionSingle : IHotloadTestFixture
			{
				public static IEnumerable<int> Collection;

				public static void Initialize()
				{
					Collection = [123];
				}

				void IHotloadTestFixture.Test()
				{
			-		Initialize();
			-
					Assert.AreEqual( 123, Collection.Single() );
				}
			}
			""" );
	}

	[TestMethod]
	public async Task CollectionExpressionMultiple()
	{
		await HotloadFixtureAsync(
			"""
			using System.Collections.Generic;
			using System.Linq;

			public class CollectionExpressionMultiple : IHotloadTestFixture
			{
				public static IEnumerable<int> Collection;

				public static void Initialize()
				{
					Collection = [123, 456];
				}

				void IHotloadTestFixture.Test()
				{
			-		Initialize();
			-
					CollectionAssert.AreEqual( new[] { 123, 456 }, Collection.ToArray() );
				}
			}
			""" );
	}

	[TestMethod]
	public async Task WeakReferenceTarget()
	{
		await HotloadFixtureAsync(
			"""
			using System;

			public class Item;

			public class WeakReferenceTarget : IHotloadTestFixture
			{
				// Keeps the target alive
				public Item Target = new();

				public WeakReference<Item> WeakReference;

				void IHotloadTestFixture.Test()
				{
			-		WeakReference = new WeakReference<Item>( Target );
			+		Assert.IsTrue( WeakReference.TryGetTarget( out var instance ) );
			+		Assert.AreSame( Target, instance );
				}
			}
			""" );
	}

	[TestMethod]
	public async Task ConditionalWeakTableValue()
	{
		await HotloadFixtureAsync(
			"""
			using System.Runtime.CompilerServices;

			public class Item;

			public class ConditionalWeakTableValue : IHotloadTestFixture
			{
				public Item Key = new();
				public ConditionalWeakTable<Item, string> Table = new();

				void IHotloadTestFixture.Test()
				{
			-		Table.Add( Key, "Hello!" );
			+		Assert.IsTrue( Table.TryGetValue( Key, out var value ) );
			+		Assert.AreEqual( "Hello!", value );
				}
			}
			""" );
	}

	[TestMethod]
	public async Task ConditionalWeakTableSwappedValue()
	{
		await HotloadFixtureAsync(
			"""
			using System.Runtime.CompilerServices;

			public class Key;
			public class Value;

			public class ConditionalWeakTableSwappedValue : IHotloadTestFixture
			{
				public Key Key = new();
				public Value Value = new();
				public ConditionalWeakTable<Key, Value> Table = new();

				void IHotloadTestFixture.Test()
				{
			-		Table.Add( Key, Value );
			+		Assert.IsTrue( Table.TryGetValue( Key, out var value ) );
			+		Assert.AreSame( Value, value );
				}
			}
			""" );
	}

	/// <summary>
	/// Lots of struct arrays, both flat and multi-dimensional.
	/// </summary>
	[TestMethod]
	public async Task VoxelArrays()
	{
		// Each chunk's arrays take the same path, so a few dozen chunks cover it. Both versions generate
		// the same voxels, so the second can check the upgraded ones against its own.

		await HotloadFixtureAsync(
			"""
			using System;

			public record struct Voxel( byte R, byte G, byte B );

			public class Chunk
			{
				public Voxel[] Array { get; set; }
				public Voxel[,,] Array3D { get; set; }
			}

			public class VoxelArrays : IHotloadTestFixture
			{
				private const int ChunkCount = 96;
				private const int ChunkSize = 16;

				public Chunk[] Chunks { get; set; }

				private static void ForEachVoxel( Action<int, int, int, int, Voxel> action )
				{
					var random = new Random( 12345 );

					for ( var i = 0; i < ChunkCount; i++ )
						for ( var x = 0; x < ChunkSize; x++ )
							for ( var y = 0; y < ChunkSize; y++ )
								for ( var z = 0; z < ChunkSize; z++ )
								{
									var rgb = random.Next( 0, 256 * 256 * 256 );
									var voxel = new Voxel( (byte)(rgb & 0xff), (byte)((rgb >> 8) & 0xff), (byte)((rgb >> 16) & 0xff) );

									action( i, x, y, z, voxel );
								}
				}

				void IHotloadTestFixture.Test()
				{
			-		Chunks = new Chunk[ChunkCount];
			-
			-		for ( var i = 0; i < ChunkCount; i++ )
			-		{
			-			Chunks[i] = new Chunk
			-			{
			-				Array = new Voxel[ChunkSize * ChunkSize * ChunkSize],
			-				Array3D = new Voxel[ChunkSize, ChunkSize, ChunkSize]
			-			};
			-		}
			-
			-		ForEachVoxel( ( i, x, y, z, voxel ) =>
			-		{
			-			Chunks[i].Array[x * ChunkSize * ChunkSize + y * ChunkSize + z] = voxel;
			-			Chunks[i].Array3D[x, y, z] = voxel;
			-		} );
			+		Assert.AreEqual( ChunkCount, Chunks.Length );
			+
			+		ForEachVoxel( ( i, x, y, z, voxel ) =>
			+		{
			+			Assert.AreEqual( voxel, Chunks[i].Array[x * ChunkSize * ChunkSize + y * ChunkSize + z] );
			+			Assert.AreEqual( voxel, Chunks[i].Array3D[x, y, z] );
			+		} );
				}
			}
			""" );
	}
}
