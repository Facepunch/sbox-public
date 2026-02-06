extern alias After;
extern alias Before;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Sandbox;
using Sandbox.Upgraders;
using Sandbox.Utility;

// ReSharper disable PossibleNullReferenceException

namespace Hotload
{
	[TestClass]
	public partial class MiscTests : HotloadTests
	{
		[Reset]
		public static object ObjectField1;

		[TestMethod]
		public void StaticField1()
		{
			Before::TestClass1.StaticIntField = 81;

			Assert.AreEqual( 0, After::TestClass1.StaticIntField );

			Hotload();

			Assert.AreEqual( 81, After::TestClass1.StaticIntField );
		}

		[TestMethod]
		public void Field1()
		{
			Before::TestClass1.Instance = new Before::TestClass1();
			Before::TestClass1.Instance.IntField = 32;

			Assert.AreEqual( 32, Before::TestClass1.Instance.IntField );
			Assert.IsNull( After::TestClass1.Instance );

			Hotload();

			Assert.AreEqual( 32, Before::TestClass1.Instance.IntField );
			Assert.AreEqual( 32, After::TestClass1.Instance.IntField );
		}

		/// <remarks>
		/// This will fail if Sandbox.Hotload was built without MONO_CECIL defined.
		/// </remarks>
		[TestMethod]
		public void Field2()
		{
			Before::TestClass1.Instance = new Before::TestClass1();
			Assert.IsNull( After::TestClass1.Instance );

			Hotload();

			Assert.AreEqual( 83, After::TestClass1.Instance.AddedField );
		}

		[TestMethod]
		public void Field3()
		{
			ObjectField1 = new Before::TestClass1();
			Assert.IsInstanceOfType( ObjectField1, typeof( Before::TestClass1 ) );

			Hotload();

			Assert.IsInstanceOfType( ObjectField1, typeof( After::TestClass1 ) );
		}

		[TestMethod]
		public void Field4()
		{
			Before::TestClass29.Instance = new Before::TestClass29();

			Assert.IsNull( After::TestClass29.Instance );

			Hotload();

			Assert.IsNotNull( After::TestClass29.Instance );
			Assert.AreEqual( 123, After::TestClass29.Instance.AddedField );
		}

		[TestMethod]
		public void Property1()
		{
			Before::TestClass1.Instance = new Before::TestClass1();
			Before::TestClass1.Instance.IntProperty = 22;

			Assert.AreEqual( 22, Before::TestClass1.Instance.IntProperty );
			Assert.IsNull( After::TestClass1.Instance );

			Hotload();

			Assert.AreEqual( 22, Before::TestClass1.Instance.IntProperty );
			Assert.AreEqual( 22, After::TestClass1.Instance.IntProperty );
		}

		[TestMethod]
		public void Property2()
		{
			Before::TestClass1.Instance = new Before::TestClass1();
			Assert.IsNull( After::TestClass1.Instance );

			Hotload();

			Assert.AreEqual( 84, After::TestClass1.Instance.AddedProperty );
		}

		public static event Action ExampleEvent;

		/// <summary>
		/// Static delegate fields / events in non-swapped types must have their values replaced.
		/// </summary>
		[TestMethod]
		public void StaticEvent1()
		{
			try
			{
				ExampleEvent += Before::TestClass46.Handler;

				Assert.AreEqual( typeof( Before::TestClass46 ), ExampleEvent?.Method.DeclaringType );

				Hotload();

				Assert.AreEqual( typeof( After::TestClass46 ), ExampleEvent?.Method.DeclaringType );
			}
			finally
			{
				ExampleEvent = null;
			}
		}

		/// <summary>
		/// Delegates must not be processed by the DefaultUpgrader, they contain fields that throw when accessed.
		/// </summary>
		[TestMethod]
		public void DefaultUpgraderRejectDelegate1()
		{
			var hotload = CreateHotload();

			hotload.UpdateReferences();

			var deleg = Before::TestClass46.Handler;

			Assert.IsFalse( hotload.GetUpgrader<DefaultUpgrader>().TryUpgradeInstance( deleg, deleg ) );
		}

		[TestMethod]
		public void Reflection1()
		{
			Before::TestClass4.Instance = new Before::TestClass4();

			var beforeType = typeof( Before::TestClass4 );
			var afterType = typeof( After::TestClass4 );

			Assert.AreNotEqual( beforeType, afterType );

			Before::TestClass4.Instance.FetchReflectionInstances();

			Assert.AreEqual( typeof( Before::TestClass4 ), Before::TestClass4.Instance.Type );
			Assert.IsNull( After::TestClass4.Instance );

			Hotload();

			Assert.AreEqual( typeof( After::TestClass4 ), After::TestClass4.Instance.Type );
		}

		[TestMethod]
		public void Reflection2()
		{
			Before::TestClass4.Instance = new Before::TestClass4();

			var beforeField = typeof( Before::TestClass4 )
				.GetField( nameof( Before::TestClass4.Field ), BindingFlags.Public | BindingFlags.Instance );

			var afterField = typeof( After::TestClass4 )
				.GetField( nameof( After::TestClass4.Field ), BindingFlags.Public | BindingFlags.Instance );

			Assert.IsNotNull( beforeField );
			Assert.IsNotNull( afterField );

			Assert.AreNotEqual( beforeField, afterField );

			Before::TestClass4.Instance.FetchReflectionInstances();

			Assert.AreEqual( beforeField, Before::TestClass4.Instance.FieldInfo );
			Assert.IsNull( After::TestClass4.Instance );

			Hotload();

			Assert.AreEqual( afterField, After::TestClass4.Instance.FieldInfo );
		}

		[TestMethod]
		public void Reflection3()
		{
			Before::TestClass4.Instance = new Before::TestClass4();

			var beforeProperty = typeof( Before::TestClass4 )
				.GetProperty( nameof( Before::TestClass4.Property ), BindingFlags.Public | BindingFlags.Instance );

			var afterProperty = typeof( After::TestClass4 )
				.GetProperty( nameof( After::TestClass4.Property ), BindingFlags.Public | BindingFlags.Instance );

			Assert.IsNotNull( beforeProperty );
			Assert.IsNotNull( afterProperty );

			Assert.AreNotEqual( beforeProperty, afterProperty );

			Before::TestClass4.Instance.FetchReflectionInstances();

			Assert.AreEqual( beforeProperty, Before::TestClass4.Instance.PropertyInfo );
			Assert.IsNull( After::TestClass4.Instance );

			Hotload();

			Assert.AreEqual( afterProperty, After::TestClass4.Instance.PropertyInfo );
		}

		[TestMethod]
		public void Reflection4()
		{
			Before::TestClass4.Instance = new Before::TestClass4();

			var beforeMethod = typeof( Before::TestClass4 )
				.GetMethod( nameof( Before::TestClass4.Method ), BindingFlags.Public | BindingFlags.Instance );

			var afterMethod = typeof( After::TestClass4 )
				.GetMethod( nameof( After::TestClass4.Method ), BindingFlags.Public | BindingFlags.Instance );

			Assert.IsNotNull( beforeMethod );
			Assert.IsNotNull( afterMethod );

			Assert.AreNotEqual( beforeMethod, afterMethod );

			Before::TestClass4.Instance.FetchReflectionInstances();

			Assert.AreEqual( beforeMethod, Before::TestClass4.Instance.MethodInfo );
			Assert.IsNull( After::TestClass4.Instance );

			Hotload();

			Assert.AreEqual( afterMethod, After::TestClass4.Instance.MethodInfo );
		}

		/// <summary>
		/// Make sure that we don't lose type parameters from generic method MethodInfos.
		/// </summary>
		[TestMethod]
		public void Reflection5()
		{
			Before::TestClass20.MethodInfo = typeof( Before::TestClass20 )
				.GetMethod( nameof( Before::TestClass20.GenericMethod ) )
				.MakeGenericMethod( typeof( string ) );

			Assert.IsNotNull( Before::TestClass20.MethodInfo );

			Assert.AreEqual( typeof( Before::TestClass20 ), Before::TestClass20.MethodInfo.DeclaringType );
			Assert.IsTrue( Before::TestClass20.MethodInfo.IsConstructedGenericMethod );
			Assert.AreEqual( typeof( string ), Before::TestClass20.MethodInfo.GetGenericArguments()[0] );

			Assert.IsNull( After::TestClass20.MethodInfo );

			Hotload();

			Assert.IsNotNull( After::TestClass20.MethodInfo );

			Assert.AreEqual( typeof( After::TestClass20 ), After::TestClass20.MethodInfo.DeclaringType );
			Assert.IsTrue( After::TestClass20.MethodInfo.IsConstructedGenericMethod );
			Assert.AreEqual( typeof( string ), After::TestClass20.MethodInfo.GetGenericArguments()[0] );
		}

		/// <summary>
		/// Make sure that we don't lose type parameters from generic declaring type MethodInfos.
		/// </summary>
		[TestMethod]
		public void Reflection6()
		{
			Before::TestClass20.MethodInfo = typeof( Before::TestClass21<string> )
				.GetMethod( nameof( Before::TestClass21<string>.GenericMethod ) );

			Assert.IsNotNull( Before::TestClass20.MethodInfo );

			Assert.AreEqual( typeof( Before::TestClass21<string> ), Before::TestClass20.MethodInfo.DeclaringType );

			Assert.IsNull( After::TestClass20.MethodInfo );

			Hotload();

			Assert.IsNotNull( After::TestClass20.MethodInfo );

			Assert.AreEqual( typeof( After::TestClass21<string> ), After::TestClass20.MethodInfo.DeclaringType );
		}

		/// <summary>
		/// Special case for <see cref="ParameterInfo"/>.
		/// </summary>
		[TestMethod]
		public void Reflection7()
		{
			Before::TestClass36.ParameterInfo = typeof( Before::TestClass36 )
				.GetMethod( nameof( Before::TestClass36.Method1 ) ).GetParameters()[0];

			Assert.IsNotNull( Before::TestClass36.ParameterInfo );

			Assert.AreEqual( typeof( Before::TestClass36 ), Before::TestClass36.ParameterInfo.Member.DeclaringType );
			Assert.AreEqual( typeof( int ), Before::TestClass36.ParameterInfo.ParameterType );

			Assert.IsNull( After::TestClass36.ParameterInfo );

			Hotload();

			Assert.IsNotNull( After::TestClass36.ParameterInfo );

			Assert.AreEqual( typeof( After::TestClass36 ).GetMethod( nameof( After::TestClass36.Method1 ) ).GetParameters()[0], After::TestClass36.ParameterInfo );
			Assert.AreEqual( typeof( int ), After::TestClass36.ParameterInfo.ParameterType );
		}

		/// <summary>
		/// Test upgrading <see cref="ParameterInfo"/> from a generic instance method.
		/// </summary>
		[TestMethod]
		public void Reflection8()
		{
			Before::TestClass36.ParameterInfo = typeof( Before::TestClass36 )
				.GetMethod( nameof( Before::TestClass36.Method2 ) )
				.MakeGenericMethod( typeof( string ) ).GetParameters()[0];

			Assert.IsNotNull( Before::TestClass36.ParameterInfo );

			Assert.AreEqual( typeof( Before::TestClass36 ), Before::TestClass36.ParameterInfo.Member.DeclaringType );
			Assert.AreEqual( typeof( string ), Before::TestClass36.ParameterInfo.ParameterType );

			Assert.IsNull( After::TestClass36.ParameterInfo );

			Hotload();

			Assert.IsNotNull( After::TestClass36.ParameterInfo );

			Assert.AreEqual( typeof( After::TestClass36 ), After::TestClass36.ParameterInfo.Member.DeclaringType );
			Assert.AreEqual( typeof( string ), After::TestClass36.ParameterInfo.ParameterType );
		}

		[TestMethod]
		public void WeakReference1()
		{
			Before::TestClass1.Instance = new Before::TestClass1();
			Before::TestClass5.Instance = new Before::TestClass5();

			Before::TestClass5.Instance.WeakReference = new WeakReference<Before::TestClass1>( Before::TestClass1.Instance );

			Assert.IsNull( After::TestClass1.Instance );
			Assert.IsNull( After::TestClass5.Instance );

			Hotload();

			Assert.IsTrue( After::TestClass5.Instance.WeakReference.TryGetTarget( out var instance ) );

			Assert.AreEqual( After::TestClass1.Instance, instance );
		}

		[TestMethod]
		public void WeakReference2()
		{
			Before::TestClass1.Instance = new Before::TestClass1();
			Before::TestClass5.Instance = new Before::TestClass5();

			const string testValue = "Hello!";

			Before::TestClass5.Instance.ConditionalWeakTable1.Add( Before::TestClass1.Instance, testValue );

			Assert.IsNull( After::TestClass1.Instance );
			Assert.IsNull( After::TestClass5.Instance );

			Hotload();

			Assert.IsTrue( After::TestClass5.Instance.ConditionalWeakTable1.TryGetValue( After::TestClass1.Instance, out var value ) );
			Assert.AreEqual( testValue, value );
		}

		[TestMethod]
		public void WeakReference3()
		{
			Before::TestClass1.Instance = new Before::TestClass1();
			Before::TestClass2.Instance = new Before::TestClass2();
			Before::TestClass5.Instance = new Before::TestClass5();

			Before::TestClass5.Instance.ConditionalWeakTable2.Add( Before::TestClass1.Instance, Before::TestClass2.Instance );

			Assert.IsNull( After::TestClass1.Instance );
			Assert.IsNull( After::TestClass2.Instance );
			Assert.IsNull( After::TestClass5.Instance );

			Hotload();

			Assert.IsTrue( After::TestClass5.Instance.ConditionalWeakTable2.TryGetValue( After::TestClass1.Instance, out var value ) );
			Assert.AreEqual( After::TestClass2.Instance, value );
		}

		[TestMethod]
		public void Async1()
		{
			Before::TestClass19.Task = Before::TestClass19.AsyncMethod( 30 );

			Assert.IsNull( After::TestClass19.Task );

			Before::TestClass19.Task.Wait();

			Assert.IsTrue( Before::TestClass19.Task.IsCompletedSuccessfully );

			Hotload();

			Assert.IsNotNull( After::TestClass19.Task );
			Assert.IsTrue( After::TestClass19.Task.IsCompletedSuccessfully );

			Assert.AreEqual( 30, After::TestClass19.Task.Result );
		}

		[TestMethod]
		public void HashSet1()
		{
			Before::TestClass7.Instance = new Before::TestClass7();

			Before::TestClass7.Instance.HashSet1.Add( new Before::TestClass7.ExampleStruct( 2 ) );
			Before::TestClass7.Instance.HashSet1.Add( new Before::TestClass7.ExampleStruct( 5 ) );
			Before::TestClass7.Instance.HashSet1.Add( new Before::TestClass7.ExampleStruct( 9 ) );

			Assert.IsNull( After::TestClass7.Instance );

			Hotload();

			Assert.AreEqual( 3, After::TestClass7.Instance.HashSet1.Count );
			Assert.IsTrue( After::TestClass7.Instance.HashSet1.Contains( new After::TestClass7.ExampleStruct( 2 ) ) );
			Assert.IsTrue( After::TestClass7.Instance.HashSet1.Contains( new After::TestClass7.ExampleStruct( 5 ) ) );
			Assert.IsTrue( After::TestClass7.Instance.HashSet1.Contains( new After::TestClass7.ExampleStruct( 9 ) ) );
		}

		[TestMethod]
		public void HashSet2()
		{
			Before::TestClass7.Instance = new Before::TestClass7();

			Before::TestClass7.Instance.HashSet2.Add( new Before::TestClass7.ExampleClass( 2 ) );
			Before::TestClass7.Instance.HashSet2.Add( new Before::TestClass7.ExampleClass( 5 ) );
			Before::TestClass7.Instance.HashSet2.Add( new Before::TestClass7.ExampleClass( 9 ) );

			Assert.IsNull( After::TestClass7.Instance );

			Hotload();

			Assert.AreEqual( 3, After::TestClass7.Instance.HashSet2.Count );
			Assert.IsTrue( After::TestClass7.Instance.HashSet2.Contains( new After::TestClass7.ExampleClass( 2 ) ) );
			Assert.IsTrue( After::TestClass7.Instance.HashSet2.Contains( new After::TestClass7.ExampleClass( 5 ) ) );
			Assert.IsTrue( After::TestClass7.Instance.HashSet2.Contains( new After::TestClass7.ExampleClass( 9 ) ) );
		}

		private static readonly HashSet<object> InitOnlyHashSet = new();

		[TestMethod]
		public void HashSet3()
		{
			InitOnlyHashSet.Clear();
			InitOnlyHashSet.Add( "Hello" );
			InitOnlyHashSet.Add( "World" );

			Assert.IsNotNull( InitOnlyHashSet );
			Assert.AreEqual( 2, InitOnlyHashSet.Count );

			var result = Hotload();

			Assert.IsNotNull( InitOnlyHashSet );
			Assert.AreEqual( 2, InitOnlyHashSet.Count );
			Assert.IsFalse( result.HasErrors );
		}

		/// <summary>
		/// Test erroring when a hash set is modified by other thread during a hotload.
		/// </summary>
		[TestMethod]
		public void HashSetInterference()
		{
			InitOnlyHashSet.Clear();

			// Lots of items so hotload gives us enough time to interfere

			const int itemCount = 1_000_000;

			for ( var i = 0; i < itemCount; i++ )
			{
				InitOnlyHashSet.Add( i );
			}

			using var cts = new CancellationTokenSource();

			Task.Run( () =>
			{
				while ( !cts.IsCancellationRequested )
				{
					// Wait for hotload to clear the set and start repopulating

					if ( InitOnlyHashSet.Count == itemCount )
					{
						continue;
					}

					// Interfere!

					InitOnlyHashSet.Add( itemCount + 1 );
					break;
				}
			}, cts.Token );

			var result = Hotload( true );

			cts.Cancel();

			Assert.IsTrue( result.HasErrors );

			// Look for an error involving the interfered hash set

			var error = result.Errors.FirstOrDefault( x => x.Member?.Name == nameof( InitOnlyHashSet ) );

			Assert.IsNotNull( error );
		}

		/// <summary>
		/// Tests upgrading a concurrent dictionary.
		/// </summary>
		[TestMethod]
		public void ConcurrentDictionary()
		{
			Before::TestClass49.Dictionary = new ConcurrentDictionary<int, Before::TestClass49>();

			Before::TestClass49.Dictionary.TryAdd( 1, new Before::TestClass49 { IntProperty = 1 } );
			Before::TestClass49.Dictionary.TryAdd( 2, new Before::TestClass49 { IntProperty = 2 } );
			Before::TestClass49.Dictionary.TryAdd( 3, new Before::TestClass49 { IntProperty = 3 } );

			Assert.IsNull( After::TestClass49.Dictionary );

			Hotload();

			Assert.IsNotNull( After::TestClass49.Dictionary );
			Assert.AreEqual( 3, After::TestClass49.Dictionary.Count );
			Assert.AreEqual( 2, After::TestClass49.Dictionary[2].IntProperty );
		}

		[TestMethod]
		public void MultiDimensionalArray1()
		{
			Before::TestClass8.Instance = new Before::TestClass8
			{
				IntArray2D = new int[10, 20]
			};

			Before::TestClass8.Instance.IntArray2D[7, 3] = 290;

			Assert.IsNull( After::TestClass8.Instance );

			Hotload();

			Assert.IsNotNull( After::TestClass8.Instance.IntArray2D );
			Assert.AreEqual( 10, After::TestClass8.Instance.IntArray2D.GetLength( 0 ) );
			Assert.AreEqual( 20, After::TestClass8.Instance.IntArray2D.GetLength( 1 ) );
			Assert.AreEqual( 290, After::TestClass8.Instance.IntArray2D[7, 3] );
		}

		[TestMethod]
		public void MultiDimensionalArray2()
		{
			Before::TestClass8.Instance = new Before::TestClass8
			{
				ObjArray2D = new Before::TestClass1[10, 20]
			};

			Before::TestClass8.Instance.ObjArray2D[7, 3] = new Before::TestClass1
			{
				IntField = 91
			};

			Assert.IsNull( After::TestClass8.Instance );

			Hotload();

			Assert.IsNotNull( After::TestClass8.Instance.ObjArray2D );
			Assert.AreEqual( 10, After::TestClass8.Instance.ObjArray2D.GetLength( 0 ) );
			Assert.AreEqual( 20, After::TestClass8.Instance.ObjArray2D.GetLength( 1 ) );
			Assert.AreEqual( 91, After::TestClass8.Instance.ObjArray2D[7, 3].IntField );
		}

		/// <summary>
		/// Test struct block copy of a multi-dimensional array.
		/// </summary>
		[TestMethod]
		public void MultiDimensionalArray3()
		{
			Before::TestClass8.Instance = new Before::TestClass8
			{
				StructArray2D1 = new Before::TestClass8.ExampleStruct1[10, 20]
			};

			Before::TestClass8.Instance.StructArray2D1[7, 3] = new Before::TestClass8.ExampleStruct1
			{
				Value = 73
			};

			Assert.IsNull( After::TestClass8.Instance );

			Hotload();

			Assert.IsNotNull( After::TestClass8.Instance.StructArray2D1 );
			Assert.AreEqual( 10, After::TestClass8.Instance.StructArray2D1.GetLength( 0 ) );
			Assert.AreEqual( 20, After::TestClass8.Instance.StructArray2D1.GetLength( 1 ) );
			Assert.AreEqual( 73, After::TestClass8.Instance.StructArray2D1[7, 3].Value );
		}

		/// <summary>
		/// Test struct block copy of a multi-dimensional array.
		/// </summary>
		[TestMethod]
		public void MultiDimensionalArray4()
		{
			Before::TestClass8.Instance = new Before::TestClass8
			{
				StructArray2D2 = new Before::TestClass8.ExampleStruct2[10, 20]
			};

			Before::TestClass8.Instance.StructArray2D2[7, 3] = new Before::TestClass8.ExampleStruct2
			{
				Value = 73
			};

			Assert.IsNull( After::TestClass8.Instance );

			Hotload();

			Assert.IsNotNull( After::TestClass8.Instance.StructArray2D2 );
			Assert.AreEqual( 10, After::TestClass8.Instance.StructArray2D2.GetLength( 0 ) );
			Assert.AreEqual( 20, After::TestClass8.Instance.StructArray2D2.GetLength( 1 ) );
			Assert.AreEqual( 73, After::TestClass8.Instance.StructArray2D2[7, 3].Value );
		}

		/// <summary>
		/// Test struct array block copy.
		/// </summary>
		[TestMethod]
		public void BlockCopyArray1()
		{
			const int testLength = 1_000_000;
			const int testIndex = 123_456;
			const int testInt = 1928;
			const float testFloat = 100f;
			var testVector = new Vector3( 1f, 2f, 3f );

			Before::TestClass24.Instance = new Before::TestClass24 { Array = new Before::BlockCopyableStruct[testLength] };
			Before::TestClass24.Instance.Array[testIndex] = new Before::BlockCopyableStruct
			{
				IntValue = testInt,
				FloatValue = testFloat,
				InnerStructValue = testVector
			};

			Assert.IsNull( After::TestClass24.Instance );

			var result = Hotload();

			Assert.IsNotNull( After::TestClass24.Instance );

			Assert.AreEqual( 1_000_000, After::TestClass24.Instance.Array.Length );
			Assert.AreEqual( testInt, After::TestClass24.Instance.Array[testIndex].IntValue );
			Assert.AreEqual( testFloat, After::TestClass24.Instance.Array[testIndex].FloatValue );
			Assert.AreEqual( testVector, After::TestClass24.Instance.Array[testIndex].InnerStructValue );

			Assert.AreEqual( 1, result.TypeTimings["BlockCopyableStruct[]"].Instances );
			Assert.IsFalse( result.TypeTimings.ContainsKey( "BlockCopyableStruct" ) );
		}

		/// <summary>
		/// Test struct list block copy.
		/// </summary>
		[TestMethod]
		public void BlockCopyList1()
		{
			const int testLength = 1_000_000;
			const int testIndex = 123_456;
			const int testInt = 1928;
			const float testFloat = 100f;
			var testVector = new Vector3( 1f, 2f, 3f );

			Before::TestClass24.Instance = new Before::TestClass24 { List = new List<Before::BlockCopyableStruct>( new Before::BlockCopyableStruct[testLength] ) };
			Before::TestClass24.Instance.List[testIndex] = new Before::BlockCopyableStruct
			{
				IntValue = testInt,
				FloatValue = testFloat,
				InnerStructValue = testVector
			};

			Assert.IsNull( After::TestClass24.Instance );

			var result = Hotload();

			Assert.IsNotNull( After::TestClass24.Instance );

			Assert.AreEqual( 1_000_000, After::TestClass24.Instance.List.Count );
			Assert.AreEqual( testInt, After::TestClass24.Instance.List[testIndex].IntValue );
			Assert.AreEqual( testFloat, After::TestClass24.Instance.List[testIndex].FloatValue );
			Assert.AreEqual( testVector, After::TestClass24.Instance.List[testIndex].InnerStructValue );

			Assert.AreEqual( 1, result.TypeTimings["System.Collections.Generic.List<BlockCopyableStruct>"].Instances );
			Assert.IsFalse( result.TypeTimings.ContainsKey( "BlockCopyableStruct" ) );
		}

		/// <summary>
		/// Test readonly struct list block copy.
		/// </summary>
		[TestMethod]
		public void BlockCopyList2()
		{
			const int testLength = 1_000_000;
			const int testIndex = 123_456;

			Before::TestClass24.Instance = new Before::TestClass24 { List2 = new List<Before::BlockCopyableStruct2>() };

			for ( var i = 0; i < testLength; ++i )
			{
				Before::TestClass24.Instance.List2.Add( new Before::BlockCopyableStruct2 { Index = i } );
			}

			Assert.IsNull( After::TestClass24.Instance );

			var result = Hotload();

			Assert.IsNotNull( After::TestClass24.Instance );

			Assert.AreEqual( 1_000_000, After::TestClass24.Instance.List2.Count );
			Assert.AreEqual( testIndex, After::TestClass24.Instance.List2[testIndex].Index );

			Assert.AreEqual( 1, result.TypeTimings["System.Collections.Generic.List<BlockCopyableStruct2>"].Instances );
			Assert.IsFalse( result.TypeTimings.ContainsKey( "BlockCopyableStruct2" ) );
		}

		[TestMethod]
		public void DictionaryComparer1()
		{
			Before::TestClass9.Instance = new Before::TestClass9
			{
				Dict1 = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase )
			{
				{ "TEST", 28 }
			}
			};

			Assert.IsNull( After::TestClass9.Instance );

			Hotload();

			Assert.IsTrue( After::TestClass9.Instance.Dict1.ContainsKey( "TEST" ) );
			Assert.IsTrue( After::TestClass9.Instance.Dict1.ContainsKey( "test" ) );
			Assert.AreEqual( 28, After::TestClass9.Instance.Dict1["test"] );
		}

		[TestMethod]
		public void DictionaryComparer2()
		{
			Before::TestClass9.Instance = new Before::TestClass9
			{
				Dict2 = new Dictionary<string, Before::TestClass1>( StringComparer.OrdinalIgnoreCase )
				{
					{ "TEST", new Before::TestClass1 { IntField = 29 } }
				}
			};

			Assert.IsNull( After::TestClass9.Instance );

			Hotload();

			Assert.IsTrue( After::TestClass9.Instance.Dict2.ContainsKey( "TEST" ) );
			Assert.IsTrue( After::TestClass9.Instance.Dict2.ContainsKey( "test" ) );
			Assert.AreEqual( 29, After::TestClass9.Instance.Dict2["test"].IntField );
		}

		[TestMethod]
		public void DictionaryComparer3()
		{
			Before::TestClass9.Instance = new Before::TestClass9
			{
				Dict3 = new SortedDictionary<string, Before::TestClass1>( StringComparer.OrdinalIgnoreCase )
				{
					{ "TEST", new Before::TestClass1 { IntField = 29 } }
				}
			};

			Assert.IsNull( After::TestClass9.Instance );

			Hotload();

			Assert.IsTrue( After::TestClass9.Instance.Dict3.ContainsKey( "TEST" ) );
			Assert.IsTrue( After::TestClass9.Instance.Dict3.ContainsKey( "test" ) );
			Assert.AreEqual( 29, After::TestClass9.Instance.Dict3["test"].IntField );
		}

		[TestMethod]
		public void CollectionOrder1()
		{
			Before::TestClass35.Instance = new Before::TestClass35
			{
				Dictionary = new Dictionary<int, Before::TestClass35.StringWrapper>()
			};

			var random = new Random( 123 );

			for ( var j = 0; j < 1_000_000; ++j )
			{
				var index = random.Next( 0, 1_000 );

				if ( Before::TestClass35.Instance.Dictionary.ContainsKey( index ) )
				{
					Before::TestClass35.Instance.Dictionary.Remove( index );
				}
				else
				{
					Before::TestClass35.Instance.Dictionary.Add( index, $"Item_{j}" );
				}
			}

			var order = Before::TestClass35.Instance.Dictionary.Select( x => x.Value.Value ).ToArray();

			for ( var i = 0; i < order.Length; ++i )
			{
				Assert.AreEqual( order[i], Before::TestClass35.Instance.Dictionary.ElementAt( i ).Value.Value );
			}

			Assert.IsNull( After::TestClass35.Instance );

			Hotload();

			Assert.IsNotNull( After::TestClass35.Instance?.Dictionary );

			for ( var i = 0; i < order.Length; ++i )
			{
				Assert.AreEqual( order[i], After::TestClass35.Instance.Dictionary.ElementAt( i ).Value.Value );
			}
		}

		[TestMethod]
		public void Enum1()
		{
			Before::TestClass12.Instance = new Before::TestClass12 { EnumValue = Before::TestEnum.C };

			Assert.IsNull( After::TestClass12.Instance );

			Hotload();

			Assert.AreEqual( After::TestEnum.C, After::TestClass12.Instance.EnumValue );
		}

		[TestMethod]
		public void Enum2()
		{
			Before::TestClass12.Instance = new Before::TestClass12 { EnumArray = new Before::TestEnum[] { Before::TestEnum.B } };

			Assert.IsNull( After::TestClass12.Instance );

			Hotload();

			Assert.AreEqual( 1, After::TestClass12.Instance.EnumArray.Length );
			Assert.AreEqual( After::TestEnum.B, After::TestClass12.Instance.EnumArray[0] );
		}

		[TestMethod]
		public void StaticReadonly1()
		{
			Before::TestClass13.Instance.IntValue = 20;
			After::TestClass13.Instance.IntValue = 0;

			Hotload();

			Assert.AreEqual( 20, After::TestClass13.Instance.IntValue );
		}

		[TestMethod]
		public void InstanceReadonly1()
		{
			Before::TestClass15.Instance = new Before::TestClass15( 21 );

			Assert.IsNull( After::TestClass15.Instance );

			Hotload();

			Assert.AreEqual( 21, After::TestClass15.Instance.IntValue );
		}

		[TestMethod]
		public void JsonSerialize1()
		{
			Before::SerializableClass.Options = new System.Text.Json.JsonSerializerOptions();

			var beforeInstance = new Before::ContainerClass { ObjectProperty = new Before::SerializableClass { IntProperty = 23 } };

			Console.WriteLine( System.Text.Json.JsonSerializer.Serialize( beforeInstance, beforeInstance.GetType(), Before::SerializableClass.Options ) );

			Assert.IsNull( After::SerializableClass.Options );

			Hotload();

			Assert.IsNotNull( After::SerializableClass.Options );

			var afterInstance = new After::ContainerClass { ObjectProperty = new After::SerializableClass { IntProperty = 37 } };

			Console.WriteLine( System.Text.Json.JsonSerializer.Serialize( afterInstance, afterInstance.GetType(), After::SerializableClass.Options ) );
		}

		/// <summary>
		/// After a hotload, the most recent replacing assembly must be auto-watched.
		/// </summary>
		[TestMethod]
		public void WatchNewAssembly()
		{
			var hotload = CreateHotload();

			Assert.IsFalse( hotload.WatchedAssemblies.Contains( typeof( After::TestClass1 ).Assembly ) );

			hotload.UpdateReferences();

			Assert.IsTrue( hotload.WatchedAssemblies.Contains( typeof( After::TestClass1 ).Assembly ) );
		}

		/// <summary>
		/// After a hotload, any replaced assemblies must not be auto-watched.
		/// </summary>
		[TestMethod]
		public void DontWatchOldAssembly()
		{
			var hotload = CreateHotload();

			Assert.IsFalse( hotload.WatchedAssemblies.Contains( typeof( Before::TestClass1 ).Assembly ) );

			hotload.UpdateReferences();

			Assert.IsFalse( hotload.WatchedAssemblies.Contains( typeof( Before::TestClass1 ).Assembly ) );
		}

		/// <summary>
		/// After a hotload, any assemblies replaced with null should not be watched.
		/// </summary>
		[TestMethod]
		public void UnloadAssembly()
		{
			var hotload = CreateHotload();

			hotload.UpdateReferences();

			hotload.ReplacingAssembly( typeof( After::TestClass1 ).Assembly, null );

			hotload.UpdateReferences();

			Assert.IsFalse( hotload.WatchedAssemblies.Contains( typeof( Before::TestClass1 ).Assembly ) );
			Assert.IsFalse( hotload.WatchedAssemblies.Contains( typeof( After::TestClass1 ).Assembly ) );
		}

		[TestMethod]
		public void ClearedList1()
		{
			Before::TestClass23.Instance = new Before::TestClass23();

			const int addedItems = 1_000_000;

			for ( var i = 0; i < addedItems; ++i )
			{
				Before::TestClass23.Instance.List.Add( new Before::ListElement() );
			}

			Before::TestClass23.Instance.List.Clear();
			Before::TestClass23.Instance.List.Add( new Before::ListElement() );

			Assert.IsNull( After::TestClass23.Instance );

			Assert.AreEqual( 1, Before::TestClass23.Instance.List.Count );

			var result = Hotload();

			Assert.IsNotNull( After::TestClass23.Instance );
			Assert.IsNotNull( After::TestClass23.Instance.List );

			Assert.AreEqual( 1, After::TestClass23.Instance.List.Count );

			Assert.IsFalse( result.InstancesProcessed > addedItems );
		}

		/// <summary>
		/// Tests class hierarchies changing after a swap.
		/// </summary>
		[TestMethod]
		public void ClassHierarchyChange1()
		{
			Before::TestClass26.Instance = new Before::TestClass26();

			Assert.IsNull( After::TestClass26.Instance );

			Before::TestClass26.Instance.BaseClassField = new Before::TestClass26.TestBaseClass { Field1 = 7 };
			Before::TestClass26.Instance.DerivingClassField = new Before::TestClass26.TestDerivingClass { Field2 = 21 };

			Hotload();

			Assert.IsNotNull( After::TestClass26.Instance );

			Assert.AreEqual( 7, After::TestClass26.Instance.BaseClassField.Field1 );
			Assert.AreEqual( 0, After::TestClass26.Instance.DerivingClassField.Field1 );
			Assert.AreEqual( 21, After::TestClass26.Instance.DerivingClassField.Field2 );
		}

		/// <summary>
		/// Tests an instance field changing type.
		/// </summary>
		[TestMethod]
		public void FieldTypeChanged()
		{
			Before::TestClass27.Instance = new Before::TestClass27 { Field = new Before::TestClass27.ExampleClass1() };

			Assert.IsNull( After::TestClass27.Instance );

			Hotload();

			Assert.IsNotNull( After::TestClass27.Instance );
			Assert.IsNull( After::TestClass27.Instance.Field );
		}

		/// <summary>
		/// Tests finding the default value of a new field with a generic type.
		/// </summary>
		[TestMethod]
		public void ResolveGenericTypeDefaultValue()
		{
			Before::TestClass28.Instance = new Before::TestClass28();

			Assert.IsNull( After::TestClass28.Instance );

			Hotload();

			Assert.IsNotNull( After::TestClass28.Instance );
			Assert.IsNotNull( After::TestClass28.Instance.Field );
			Assert.AreEqual( 21, After::TestClass28.Instance.Field.Field );
		}

		/// <summary>
		/// Tests <see cref="IHotloadManaged.Created"/> / <see cref="IHotloadManaged.Destroyed"/> being called.
		/// </summary>
		[TestMethod]
		public void IHotloadManaged1()
		{
			Before::TestClass33.Instance = new Before::TestClass33();

			Assert.IsNull( After::TestClass33.Instance );

			Before::TestClass33.Instance.Field1 = "World";
			Before::TestClass33.Instance.Field2 = null;

			Hotload();

			Assert.IsNotNull( After::TestClass33.Instance );
			Assert.AreEqual( "World", After::TestClass33.Instance.Field1 );
			Assert.AreEqual( "World", After::TestClass33.Instance.Field2 );
		}

		/// <summary>
		/// Tests <see cref="IHotloadManaged.Failed"/> being called.
		/// </summary>
		[TestMethod]
		public void IHotloadManaged2()
		{
			Before::TestClass34.Instance = new Before::TestClass34();

			Assert.IsNull( After::TestClass34.Instance );

			Before::TestClass34.Instance.RemovedInstance = new Before::TestClass34.RemovedClass
			{
				Parent = Before::TestClass34.Instance
			};

			Assert.IsFalse( Before::TestClass34.Instance.FailureHandled );

			Hotload();

			Assert.IsNotNull( After::TestClass34.Instance );
			Assert.IsNull( After::TestClass34.Instance.RemovedInstance );
			Assert.IsTrue( Before::TestClass34.Instance.FailureHandled );
		}

		[TestMethod]
		public void VoxelArrays()
		{
#pragma warning disable CS0162 // Unreachable code detected
			// ReSharper disable ConditionIsAlwaysTrueOrFalse

			const int chunkCount = 9666;
			const int chunkSize = 16;
			const int seed = 12345;

			const bool testInterfaceArrays = false;

			Before::TestClass38.Instance = new Before::TestClass38();

			Assert.IsNull( After::TestClass38.Instance );

			Before::TestClass38.Instance.Chunks = new Before::TestClass38.Chunk[chunkCount];

			var random = new Random( seed );

			for ( var i = 0; i < chunkCount; i++ )
			{
				var chunk = new Before::TestClass38.Chunk();

				Before::TestClass38.Instance.Chunks[i] = chunk;

				chunk.Array = new Before::TestClass38.Voxel[chunkSize * chunkSize * chunkSize];
				chunk.Array3D = new Before::TestClass38.Voxel[chunkSize, chunkSize, chunkSize];

				if ( testInterfaceArrays )
				{
					chunk.InterfaceArray = new Before::TestClass38.IVoxel[chunkSize * chunkSize * chunkSize];
					chunk.InterfaceArray3D = new Before::TestClass38.IVoxel[chunkSize, chunkSize, chunkSize];
				}

				for ( var x = 0; x < chunkSize; x++ )
					for ( var y = 0; y < chunkSize; y++ )
						for ( var z = 0; z < chunkSize; z++ )
						{
							var rgb = random.Next( 0, 256 * 256 * 256 );
							var voxel = new Before::TestClass38.Voxel( (byte)(rgb & 0xff), (byte)((rgb >> 8) & 0xff), (byte)((rgb >> 16) & 0xff) );
							var index = x * chunkSize * chunkSize + y * chunkSize + z;

							chunk.Array[index] = voxel;
							chunk.Array3D[x, y, z] = voxel;

							if ( testInterfaceArrays )
							{
								chunk.InterfaceArray[index] = voxel;
								chunk.InterfaceArray3D[x, y, z] = voxel;
							}
						}
			}

			Hotload();

			Assert.IsNotNull( After::TestClass38.Instance );
			Assert.IsNotNull( After::TestClass38.Instance.Chunks );

			random = new Random( seed );

			foreach ( var chunk in After::TestClass38.Instance.Chunks )
			{
				Assert.IsNotNull( chunk.Array );
				Assert.IsNotNull( chunk.Array3D );

				if ( testInterfaceArrays )
				{
					Assert.IsNotNull( chunk.InterfaceArray );
					Assert.IsNotNull( chunk.InterfaceArray3D );
				}

				for ( var x = 0; x < chunkSize; x++ )
					for ( var y = 0; y < chunkSize; y++ )
						for ( var z = 0; z < chunkSize; z++ )
						{
							var rgb = random.Next( 0, 256 * 256 * 256 );
							var voxel = new After::TestClass38.Voxel( (byte)(rgb & 0xff), (byte)((rgb >> 8) & 0xff), (byte)((rgb >> 16) & 0xff) );
							var index = x * chunkSize * chunkSize + y * chunkSize + z;

							Assert.AreEqual( voxel, chunk.Array[index] );
							Assert.AreEqual( voxel, chunk.Array3D[x, y, z] );

							if ( testInterfaceArrays )
							{
								Assert.AreEqual( voxel, chunk.InterfaceArray[index] );
								Assert.AreEqual( voxel, chunk.InterfaceArray3D[x, y, z] );
							}
						}
			}

			// ReSharper restore ConditionIsAlwaysTrueOrFalse
#pragma warning restore CS0162 // Unreachable code detected
		}

		/// <summary>
		/// Making sure that we don't get any stack overflows if a generic instance uses its definition as a type arg.
		/// </summary>
		[TestMethod]
		public void SelfReferencingGeneric()
		{
			Before::TestClass39.Instance = new Before::TestClass39
			{
				SelfReferencingGeneric = new() { Value = new() { Value = 123 } }
			};

			Assert.IsNull( After::TestClass39.Instance );

			Hotload();

			Assert.IsNotNull( After::TestClass39.Instance );
			Assert.AreEqual( 123, After::TestClass39.Instance.SelfReferencingGeneric.Value.Value );
		}

		/// <summary>
		/// Reproducing <see href="https://github.com/Facepunch/sbox/issues/1673"/>.
		/// </summary>
		[TestMethod]
		public void CompareGenericMethods()
		{
			Before::TestClass40.Instance = new Before::TestClass40();

			Assert.IsNull( After::TestClass40.Instance );
			Assert.IsNull( Before::TestClass40.Instance.MethodInfo );

			Before::TestClass40.Instance.MethodInfo = typeof( Before::TestClass40 )
				.GetMethods()
				.FirstOrDefault( x => x.Name == nameof( Before::TestClass40.GenericMethod ) && x.IsGenericMethodDefinition );

			Assert.IsNotNull( Before::TestClass40.Instance.MethodInfo );

			Hotload();

			Assert.IsNotNull( After::TestClass40.Instance.MethodInfo );

			Assert.AreEqual( nameof( After::TestClass40.GenericMethod ), After::TestClass40.Instance.MethodInfo.Name );
			Assert.AreEqual( typeof( After::TestClass40 ), After::TestClass40.Instance.MethodInfo.DeclaringType );
		}

		/// <summary>
		/// If types A, B derive C, but type A is changed to derive B, hotload should still process fields from
		/// type C when processing instances of type A.
		/// </summary>
		[TestMethod]
		public void InsertedBaseType()
		{
			Before::TestClass41.Instance = new Before::TestClass41.A { AField = 10, CField = 20 };

			Assert.IsNull( After::TestClass41.Instance );

			Hotload();

			Assert.IsNotNull( After::TestClass41.Instance );
			Assert.AreEqual( 10, After::TestClass41.Instance.AField );
			Assert.AreEqual( 20, After::TestClass41.Instance.CField );
		}

		/// <summary>
		/// If type A derives B, and B derive C, but type A is changed to derive C directly, hotload should still
		/// process fields from type C when processing instances of type A.
		/// </summary>
		[TestMethod]
		public void RemovedBaseType()
		{
			Before::TestClass42.Instance = new Before::TestClass42.A { AField = 10, BField = 15, CField = 20 };

			Assert.IsNull( After::TestClass42.Instance );

			Hotload();

			Assert.IsNotNull( After::TestClass42.Instance );
			Assert.AreEqual( 10, After::TestClass42.Instance.AField );
			Assert.AreEqual( 20, After::TestClass42.Instance.CField );
		}

		/// <summary>
		/// If type A derives B&lt;T1&gt;, but changes to derive B&lt;T2&gt;, hotload should try to process
		/// fields that are common to B&lt;T1&gt; and B&lt;T2&gt;.
		/// </summary>
		[TestMethod]
		public void ChangedBaseTypeGenericArgument()
		{
			Before::TestClass43.Instance = new Before::TestClass43.A { AField = 10, BField1 = 15, BField2 = 20 };

			Assert.IsNull( After::TestClass43.Instance );

			Hotload();

			Assert.IsNotNull( After::TestClass43.Instance );
			Assert.AreEqual( 10, After::TestClass43.Instance.AField );
			Assert.AreEqual( 15, After::TestClass43.Instance.BField1 );
			Assert.AreEqual( null, After::TestClass43.Instance.BField2 );
		}

		/// <summary>
		/// Definitely persist anonymous types that don't change members.
		/// </summary>
		[TestMethod]
		public void AnonymousTypeUnchanged()
		{
			Before::TestClass44.Unchanged();

			Assert.AreEqual( "{ Property1 = World }", Before::TestClass44.Instance?.ToString() );
			Assert.IsNull( After::TestClass44.Instance );

			Hotload();

			Assert.AreEqual( "{ Property1 = World }", After::TestClass44.Instance?.ToString() );
		}

		/// <summary>
		/// Anonymous type substitution looks for identical property names / types, even if the compiler-generated
		/// name of the type has changed.
		/// </summary>
		[TestMethod]
		public void AnonymousTypeReordered()
		{
			Before::TestClass44.Reordered();

			Assert.AreEqual( "{ Property3 = World }", Before::TestClass44.Instance?.ToString() );
			Assert.IsNull( After::TestClass44.Instance );

			Hotload();

			Assert.AreEqual( "{ Property3 = World }", After::TestClass44.Instance?.ToString() );
		}

		/// <summary>
		/// We can't reliably handle adding / removing properties from anonymous types, just warn for now.
		/// </summary>
		private void TestAnonymousTypeChange()
		{
			Assert.IsNull( After::TestClass44.Instance );

			var anonType = Before::TestClass44.Instance.GetType().GetGenericTypeDefinition();
			var result = Hotload();

			Assert.IsNull( After::TestClass44.Instance );
			Assert.IsTrue( result.Warnings.Any( x => x.Member == anonType ) );
		}

		/// <inheritdoc cref="TestAnonymousTypeChange" />
		[TestMethod]
		public void AnonymousTypeAddProperty()
		{
			Before::TestClass44.AddProperty();

			Assert.AreEqual( "{ Property4 = World }", Before::TestClass44.Instance?.ToString() );

			TestAnonymousTypeChange();
		}

		/// <inheritdoc cref="TestAnonymousTypeChange" />
		[TestMethod]
		public void AnonymousTypeRemoveProperty()
		{
			Before::TestClass44.RemoveProperty();

			Assert.AreEqual( "{ Property6 = World, Property7 = 0 }", Before::TestClass44.Instance?.ToString() );

			TestAnonymousTypeChange();
		}

		/// <inheritdoc cref="TestAnonymousTypeChange" />
		[TestMethod]
		public void AnonymousTypeChangeProperty()
		{
			Before::TestClass44.ChangeProperty();

			Assert.AreEqual( "{ Property8 = World, Property9 = 0 }", Before::TestClass44.Instance?.ToString() );

			TestAnonymousTypeChange();
		}

		[TestMethod]
		public void CompilerGeneratedCollectionType1()
		{
			Assert.IsNull( Before::TestClass48.Collection );

			Before::TestClass48.InitializeSingle();

			Assert.IsNull( After::TestClass48.Collection );

			Assert.AreEqual( 123, Before::TestClass48.Collection!.Single() );

			Hotload();

			Assert.AreEqual( 123, After::TestClass48.Collection!.Single() );
		}

		[TestMethod]
		public void CompilerGeneratedCollectionType2()
		{
			Assert.IsNull( Before::TestClass48.Collection );

			Before::TestClass48.InitializeMultiple();

			Assert.IsNull( After::TestClass48.Collection );

			Assert.AreEqual( 2, Before::TestClass48.Collection!.Count() );
			Assert.AreEqual( 123, Before::TestClass48.Collection.First() );
			Assert.AreEqual( 456, Before::TestClass48.Collection.Skip( 1 ).First() );

			Hotload();

			Assert.AreEqual( 2, After::TestClass48.Collection!.Count() );
			Assert.AreEqual( 123, After::TestClass48.Collection.First() );
			Assert.AreEqual( 456, After::TestClass48.Collection.Skip( 1 ).First() );
		}

		[Reset]
		private static HashSetEx<object> _hashSetEx;

		/// <summary>
		/// HashSetEx must gracefully handle items being removed during hotload
		/// because their types don't exist in the new assembly.
		/// </summary>
		[TestMethod]
		public void HashSetExRemovedItem()
		{
			_hashSetEx = new HashSetEx<object>();
			_hashSetEx.Add( new Before::RemovedClass() );
			_hashSetEx.Add( new object() );

			Assert.AreEqual( 2, _hashSetEx.Count );
			Assert.AreEqual( 2, _hashSetEx.List.Count );

			Hotload();

			Assert.AreEqual( 1, _hashSetEx.Count );
			Assert.AreEqual( 1, _hashSetEx.List.Count );
		}
	}
}
