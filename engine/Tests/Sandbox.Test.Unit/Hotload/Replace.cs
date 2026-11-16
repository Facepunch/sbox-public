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
using Sandbox.Engine;
using Sandbox.Upgraders;
using Sandbox.Utility;

// ReSharper disable PossibleNullReferenceException

namespace HotloadTests
{
	[TestClass]
	[DoNotParallelize]
	public partial class ReplaceTest : HotloadTestBase
	{
		[Reset]
		public static object ObjectField1;

		[TestMethod]
		public void StaticField()
		{
			Before::BasicMembersClass.StaticIntField = 81;

			Assert.AreEqual( 0, After::BasicMembersClass.StaticIntField );

			Hotload();

			Assert.AreEqual( 81, After::BasicMembersClass.StaticIntField );
		}

		[TestMethod]
		public void InstanceField()
		{
			Before::BasicMembersClass.Instance = new Before::BasicMembersClass();
			Before::BasicMembersClass.Instance.IntField = 32;

			Assert.AreEqual( 32, Before::BasicMembersClass.Instance.IntField );
			Assert.IsNull( After::BasicMembersClass.Instance );

			Hotload();

			Assert.AreEqual( 32, Before::BasicMembersClass.Instance.IntField );
			Assert.AreEqual( 32, After::BasicMembersClass.Instance.IntField );
		}

		/// <remarks>
		/// This will fail if Sandbox.Hotload was built without MONO_CECIL defined.
		/// </remarks>
		[TestMethod]
		public void AddedFieldInitializer()
		{
			Before::BasicMembersClass.Instance = new Before::BasicMembersClass();
			Assert.IsNull( After::BasicMembersClass.Instance );

			Hotload();

			Assert.AreEqual( 83, After::BasicMembersClass.Instance.AddedField );
		}

		[TestMethod]
		public void ObjectFieldInNonSwappedType()
		{
			ObjectField1 = new Before::BasicMembersClass();
			Assert.IsInstanceOfType( ObjectField1, typeof( Before::BasicMembersClass ) );

			Hotload();

			Assert.IsInstanceOfType( ObjectField1, typeof( After::BasicMembersClass ) );
		}

		[TestMethod]
		public void AddedFieldInitializerWithCtorBody()
		{
			Before::FieldInitializerClass.Instance = new Before::FieldInitializerClass();

			Assert.IsNull( After::FieldInitializerClass.Instance );

			Hotload();

			Assert.IsNotNull( After::FieldInitializerClass.Instance );
			Assert.AreEqual( 123, After::FieldInitializerClass.Instance.AddedField );
		}

		[TestMethod]
		public void AutoProperty()
		{
			Before::BasicMembersClass.Instance = new Before::BasicMembersClass();
			Before::BasicMembersClass.Instance.IntProperty = 22;

			Assert.AreEqual( 22, Before::BasicMembersClass.Instance.IntProperty );
			Assert.IsNull( After::BasicMembersClass.Instance );

			Hotload();

			Assert.AreEqual( 22, Before::BasicMembersClass.Instance.IntProperty );
			Assert.AreEqual( 22, After::BasicMembersClass.Instance.IntProperty );
		}

		[TestMethod]
		public void AddedPropertyInitializer()
		{
			Before::BasicMembersClass.Instance = new Before::BasicMembersClass();
			Assert.IsNull( After::BasicMembersClass.Instance );

			Hotload();

			Assert.AreEqual( 84, After::BasicMembersClass.Instance.AddedProperty );
		}

		public static event Action ExampleEvent;

		/// <summary>
		/// Static delegate fields / events in non-swapped types must have their values replaced.
		/// </summary>
		[TestMethod]
		public void StaticEventInNonSwappedType()
		{
			try
			{
				ExampleEvent += Before::StaticHandlerClass.Handler;

				Assert.AreEqual( typeof( Before::StaticHandlerClass ), ExampleEvent?.Method.DeclaringType );

				Hotload();

				Assert.AreEqual( typeof( After::StaticHandlerClass ), ExampleEvent?.Method.DeclaringType );
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
		public void DefaultUpgraderRejectsDelegate()
		{
			var hotload = CreateHotload();

			hotload.UpdateReferences();

			var deleg = Before::StaticHandlerClass.Handler;

			Assert.IsFalse( hotload.GetUpgrader<DefaultUpgrader>().TryUpgradeInstance( deleg, deleg ) );
		}

		[TestMethod]
		public void ReflectionType()
		{
			Before::ReflectionClass.Instance = new Before::ReflectionClass();

			var beforeType = typeof( Before::ReflectionClass );
			var afterType = typeof( After::ReflectionClass );

			Assert.AreNotEqual( beforeType, afterType );

			Before::ReflectionClass.Instance.FetchReflectionInstances();

			Assert.AreEqual( typeof( Before::ReflectionClass ), Before::ReflectionClass.Instance.Type );
			Assert.IsNull( After::ReflectionClass.Instance );

			Hotload();

			Assert.AreEqual( typeof( After::ReflectionClass ), After::ReflectionClass.Instance.Type );
		}

		[TestMethod]
		public void ReflectionFieldInfo()
		{
			Before::ReflectionClass.Instance = new Before::ReflectionClass();

			var beforeField = typeof( Before::ReflectionClass )
				.GetField( nameof( Before::ReflectionClass.Field ), BindingFlags.Public | BindingFlags.Instance );

			var afterField = typeof( After::ReflectionClass )
				.GetField( nameof( After::ReflectionClass.Field ), BindingFlags.Public | BindingFlags.Instance );

			Assert.IsNotNull( beforeField );
			Assert.IsNotNull( afterField );

			Assert.AreNotEqual( beforeField, afterField );

			Before::ReflectionClass.Instance.FetchReflectionInstances();

			Assert.AreEqual( beforeField, Before::ReflectionClass.Instance.FieldInfo );
			Assert.IsNull( After::ReflectionClass.Instance );

			Hotload();

			Assert.AreEqual( afterField, After::ReflectionClass.Instance.FieldInfo );
		}

		[TestMethod]
		public void ReflectionPropertyInfo()
		{
			Before::ReflectionClass.Instance = new Before::ReflectionClass();

			var beforeProperty = typeof( Before::ReflectionClass )
				.GetProperty( nameof( Before::ReflectionClass.Property ), BindingFlags.Public | BindingFlags.Instance );

			var afterProperty = typeof( After::ReflectionClass )
				.GetProperty( nameof( After::ReflectionClass.Property ), BindingFlags.Public | BindingFlags.Instance );

			Assert.IsNotNull( beforeProperty );
			Assert.IsNotNull( afterProperty );

			Assert.AreNotEqual( beforeProperty, afterProperty );

			Before::ReflectionClass.Instance.FetchReflectionInstances();

			Assert.AreEqual( beforeProperty, Before::ReflectionClass.Instance.PropertyInfo );
			Assert.IsNull( After::ReflectionClass.Instance );

			Hotload();

			Assert.AreEqual( afterProperty, After::ReflectionClass.Instance.PropertyInfo );
		}

		[TestMethod]
		public void ReflectionMethodInfo()
		{
			Before::ReflectionClass.Instance = new Before::ReflectionClass();

			var beforeMethod = typeof( Before::ReflectionClass )
				.GetMethod( nameof( Before::ReflectionClass.Method ), BindingFlags.Public | BindingFlags.Instance );

			var afterMethod = typeof( After::ReflectionClass )
				.GetMethod( nameof( After::ReflectionClass.Method ), BindingFlags.Public | BindingFlags.Instance );

			Assert.IsNotNull( beforeMethod );
			Assert.IsNotNull( afterMethod );

			Assert.AreNotEqual( beforeMethod, afterMethod );

			Before::ReflectionClass.Instance.FetchReflectionInstances();

			Assert.AreEqual( beforeMethod, Before::ReflectionClass.Instance.MethodInfo );
			Assert.IsNull( After::ReflectionClass.Instance );

			Hotload();

			Assert.AreEqual( afterMethod, After::ReflectionClass.Instance.MethodInfo );
		}

		/// <summary>
		/// Make sure that we don't lose type parameters from generic method MethodInfos.
		/// </summary>
		[TestMethod]
		public void ReflectionGenericMethodInfo()
		{
			Before::GenericMethodClass.MethodInfo = typeof( Before::GenericMethodClass )
				.GetMethod( nameof( Before::GenericMethodClass.GenericMethod ) )
				.MakeGenericMethod( typeof( string ) );

			Assert.IsNotNull( Before::GenericMethodClass.MethodInfo );

			Assert.AreEqual( typeof( Before::GenericMethodClass ), Before::GenericMethodClass.MethodInfo.DeclaringType );
			Assert.IsTrue( Before::GenericMethodClass.MethodInfo.IsConstructedGenericMethod );
			Assert.AreEqual( typeof( string ), Before::GenericMethodClass.MethodInfo.GetGenericArguments()[0] );

			Assert.IsNull( After::GenericMethodClass.MethodInfo );

			Hotload();

			Assert.IsNotNull( After::GenericMethodClass.MethodInfo );

			Assert.AreEqual( typeof( After::GenericMethodClass ), After::GenericMethodClass.MethodInfo.DeclaringType );
			Assert.IsTrue( After::GenericMethodClass.MethodInfo.IsConstructedGenericMethod );
			Assert.AreEqual( typeof( string ), After::GenericMethodClass.MethodInfo.GetGenericArguments()[0] );
		}

		/// <summary>
		/// Make sure that we don't lose type parameters from generic declaring type MethodInfos.
		/// </summary>
		[TestMethod]
		public void ReflectionGenericTypeMethodInfo()
		{
			Before::GenericMethodClass.MethodInfo = typeof( Before::GenericTypeClass<string> )
				.GetMethod( nameof( Before::GenericTypeClass<string>.GenericMethod ) );

			Assert.IsNotNull( Before::GenericMethodClass.MethodInfo );

			Assert.AreEqual( typeof( Before::GenericTypeClass<string> ), Before::GenericMethodClass.MethodInfo.DeclaringType );

			Assert.IsNull( After::GenericMethodClass.MethodInfo );

			Hotload();

			Assert.IsNotNull( After::GenericMethodClass.MethodInfo );

			Assert.AreEqual( typeof( After::GenericTypeClass<string> ), After::GenericMethodClass.MethodInfo.DeclaringType );
		}

		/// <summary>
		/// Special case for <see cref="ParameterInfo"/>.
		/// </summary>
		[TestMethod]
		public void ReflectionParameterInfo()
		{
			Before::ParameterInfoClass.ParameterInfo = typeof( Before::ParameterInfoClass )
				.GetMethod( nameof( Before::ParameterInfoClass.Method1 ) ).GetParameters()[0];

			Assert.IsNotNull( Before::ParameterInfoClass.ParameterInfo );

			Assert.AreEqual( typeof( Before::ParameterInfoClass ), Before::ParameterInfoClass.ParameterInfo.Member.DeclaringType );
			Assert.AreEqual( typeof( int ), Before::ParameterInfoClass.ParameterInfo.ParameterType );

			Assert.IsNull( After::ParameterInfoClass.ParameterInfo );

			Hotload();

			Assert.IsNotNull( After::ParameterInfoClass.ParameterInfo );

			Assert.AreEqual( typeof( After::ParameterInfoClass ).GetMethod( nameof( After::ParameterInfoClass.Method1 ) ).GetParameters()[0], After::ParameterInfoClass.ParameterInfo );
			Assert.AreEqual( typeof( int ), After::ParameterInfoClass.ParameterInfo.ParameterType );
		}

		/// <summary>
		/// Test upgrading <see cref="ParameterInfo"/> from a generic instance method.
		/// </summary>
		[TestMethod]
		public void ReflectionGenericMethodParameterInfo()
		{
			Before::ParameterInfoClass.ParameterInfo = typeof( Before::ParameterInfoClass )
				.GetMethod( nameof( Before::ParameterInfoClass.Method2 ) )
				.MakeGenericMethod( typeof( string ) ).GetParameters()[0];

			Assert.IsNotNull( Before::ParameterInfoClass.ParameterInfo );

			Assert.AreEqual( typeof( Before::ParameterInfoClass ), Before::ParameterInfoClass.ParameterInfo.Member.DeclaringType );
			Assert.AreEqual( typeof( string ), Before::ParameterInfoClass.ParameterInfo.ParameterType );

			Assert.IsNull( After::ParameterInfoClass.ParameterInfo );

			Hotload();

			Assert.IsNotNull( After::ParameterInfoClass.ParameterInfo );

			Assert.AreEqual( typeof( After::ParameterInfoClass ), After::ParameterInfoClass.ParameterInfo.Member.DeclaringType );
			Assert.AreEqual( typeof( string ), After::ParameterInfoClass.ParameterInfo.ParameterType );
		}

		[TestMethod]
		public void WeakReferenceTarget()
		{
			Before::BasicMembersClass.Instance = new Before::BasicMembersClass();
			Before::WeakReferenceClass.Instance = new Before::WeakReferenceClass();

			Before::WeakReferenceClass.Instance.WeakReference = new WeakReference<Before::BasicMembersClass>( Before::BasicMembersClass.Instance );

			Assert.IsNull( After::BasicMembersClass.Instance );
			Assert.IsNull( After::WeakReferenceClass.Instance );

			Hotload();

			Assert.IsTrue( After::WeakReferenceClass.Instance.WeakReference.TryGetTarget( out var instance ) );

			Assert.AreEqual( After::BasicMembersClass.Instance, instance );
		}

		[TestMethod]
		public void ConditionalWeakTableValue()
		{
			Before::BasicMembersClass.Instance = new Before::BasicMembersClass();
			Before::WeakReferenceClass.Instance = new Before::WeakReferenceClass();

			const string testValue = "Hello!";

			Before::WeakReferenceClass.Instance.ConditionalWeakTable1.Add( Before::BasicMembersClass.Instance, testValue );

			Assert.IsNull( After::BasicMembersClass.Instance );
			Assert.IsNull( After::WeakReferenceClass.Instance );

			Hotload();

			Assert.IsTrue( After::WeakReferenceClass.Instance.ConditionalWeakTable1.TryGetValue( After::BasicMembersClass.Instance, out var value ) );
			Assert.AreEqual( testValue, value );
		}

		[TestMethod]
		public void ConditionalWeakTableSwappedValue()
		{
			Before::BasicMembersClass.Instance = new Before::BasicMembersClass();
			Before::NestedActionClass.Instance = new Before::NestedActionClass();
			Before::WeakReferenceClass.Instance = new Before::WeakReferenceClass();

			Before::WeakReferenceClass.Instance.ConditionalWeakTable2.Add( Before::BasicMembersClass.Instance, Before::NestedActionClass.Instance );

			Assert.IsNull( After::BasicMembersClass.Instance );
			Assert.IsNull( After::NestedActionClass.Instance );
			Assert.IsNull( After::WeakReferenceClass.Instance );

			Hotload();

			Assert.IsTrue( After::WeakReferenceClass.Instance.ConditionalWeakTable2.TryGetValue( After::BasicMembersClass.Instance, out var value ) );
			Assert.AreEqual( After::NestedActionClass.Instance, value );
		}

		[TestMethod]
		public void CompletedTask()
		{
			Before::AsyncMethodClass.Task = Before::AsyncMethodClass.AsyncMethod( 30 );

			Assert.IsNull( After::AsyncMethodClass.Task );

			Before::AsyncMethodClass.Task.Wait();

			Assert.IsTrue( Before::AsyncMethodClass.Task.IsCompletedSuccessfully );

			Hotload();

			Assert.IsNotNull( After::AsyncMethodClass.Task );
			Assert.IsTrue( After::AsyncMethodClass.Task.IsCompletedSuccessfully );

			Assert.AreEqual( 30, After::AsyncMethodClass.Task.Result );
		}

		[TestMethod]
		public void HashSetOfStructs()
		{
			Before::HashSetClass.Instance = new Before::HashSetClass();

			Before::HashSetClass.Instance.HashSet1.Add( new Before::HashSetClass.ExampleStruct( 2 ) );
			Before::HashSetClass.Instance.HashSet1.Add( new Before::HashSetClass.ExampleStruct( 5 ) );
			Before::HashSetClass.Instance.HashSet1.Add( new Before::HashSetClass.ExampleStruct( 9 ) );

			Assert.IsNull( After::HashSetClass.Instance );

			Hotload();

			Assert.AreEqual( 3, After::HashSetClass.Instance.HashSet1.Count );
			Assert.IsTrue( After::HashSetClass.Instance.HashSet1.Contains( new After::HashSetClass.ExampleStruct( 2 ) ) );
			Assert.IsTrue( After::HashSetClass.Instance.HashSet1.Contains( new After::HashSetClass.ExampleStruct( 5 ) ) );
			Assert.IsTrue( After::HashSetClass.Instance.HashSet1.Contains( new After::HashSetClass.ExampleStruct( 9 ) ) );
		}

		[TestMethod]
		public void HashSetOfClasses()
		{
			Before::HashSetClass.Instance = new Before::HashSetClass();

			Before::HashSetClass.Instance.HashSet2.Add( new Before::HashSetClass.ExampleClass( 2 ) );
			Before::HashSetClass.Instance.HashSet2.Add( new Before::HashSetClass.ExampleClass( 5 ) );
			Before::HashSetClass.Instance.HashSet2.Add( new Before::HashSetClass.ExampleClass( 9 ) );

			Assert.IsNull( After::HashSetClass.Instance );

			Hotload();

			Assert.AreEqual( 3, After::HashSetClass.Instance.HashSet2.Count );
			Assert.IsTrue( After::HashSetClass.Instance.HashSet2.Contains( new After::HashSetClass.ExampleClass( 2 ) ) );
			Assert.IsTrue( After::HashSetClass.Instance.HashSet2.Contains( new After::HashSetClass.ExampleClass( 5 ) ) );
			Assert.IsTrue( After::HashSetClass.Instance.HashSet2.Contains( new After::HashSetClass.ExampleClass( 9 ) ) );
		}

		private static readonly HashSet<object> InitOnlyHashSet = new();

		[TestMethod]
		public void ReadonlyHashSetField()
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
			using var spinning = new ManualResetEventSlim();

			// A dedicated thread that is already spinning: a pool task can start after the hotload has finished
			var interferer = new Thread( () =>
			{
				spinning.Set();

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
			} );

			interferer.Start();
			spinning.Wait();

			var result = Hotload( true );

			cts.Cancel();
			interferer.Join();

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
			Before::ConcurrentDictionaryClass.Dictionary = new ConcurrentDictionary<int, Before::ConcurrentDictionaryClass>();

			Before::ConcurrentDictionaryClass.Dictionary.TryAdd( 1, new Before::ConcurrentDictionaryClass { IntProperty = 1 } );
			Before::ConcurrentDictionaryClass.Dictionary.TryAdd( 2, new Before::ConcurrentDictionaryClass { IntProperty = 2 } );
			Before::ConcurrentDictionaryClass.Dictionary.TryAdd( 3, new Before::ConcurrentDictionaryClass { IntProperty = 3 } );

			Assert.IsNull( After::ConcurrentDictionaryClass.Dictionary );

			Hotload();

			Assert.IsNotNull( After::ConcurrentDictionaryClass.Dictionary );
			Assert.AreEqual( 3, After::ConcurrentDictionaryClass.Dictionary.Count );
			Assert.AreEqual( 2, After::ConcurrentDictionaryClass.Dictionary[2].IntProperty );
		}

		[TestMethod]
		public void MultiDimIntArray()
		{
			Before::MultiDimArrayClass.Instance = new Before::MultiDimArrayClass
			{
				IntArray2D = new int[10, 20]
			};

			Before::MultiDimArrayClass.Instance.IntArray2D[7, 3] = 290;

			Assert.IsNull( After::MultiDimArrayClass.Instance );

			Hotload();

			Assert.IsNotNull( After::MultiDimArrayClass.Instance.IntArray2D );
			Assert.AreEqual( 10, After::MultiDimArrayClass.Instance.IntArray2D.GetLength( 0 ) );
			Assert.AreEqual( 20, After::MultiDimArrayClass.Instance.IntArray2D.GetLength( 1 ) );
			Assert.AreEqual( 290, After::MultiDimArrayClass.Instance.IntArray2D[7, 3] );
		}

		[TestMethod]
		public void MultiDimObjectArray()
		{
			Before::MultiDimArrayClass.Instance = new Before::MultiDimArrayClass
			{
				ObjArray2D = new Before::BasicMembersClass[10, 20]
			};

			Before::MultiDimArrayClass.Instance.ObjArray2D[7, 3] = new Before::BasicMembersClass
			{
				IntField = 91
			};

			Assert.IsNull( After::MultiDimArrayClass.Instance );

			Hotload();

			Assert.IsNotNull( After::MultiDimArrayClass.Instance.ObjArray2D );
			Assert.AreEqual( 10, After::MultiDimArrayClass.Instance.ObjArray2D.GetLength( 0 ) );
			Assert.AreEqual( 20, After::MultiDimArrayClass.Instance.ObjArray2D.GetLength( 1 ) );
			Assert.AreEqual( 91, After::MultiDimArrayClass.Instance.ObjArray2D[7, 3].IntField );
		}

		/// <summary>
		/// Test struct block copy of a multi-dimensional array.
		/// </summary>
		[TestMethod]
		public void MultiDimStructArray()
		{
			Before::MultiDimArrayClass.Instance = new Before::MultiDimArrayClass
			{
				StructArray2D1 = new Before::MultiDimArrayClass.ExampleStruct1[10, 20]
			};

			Before::MultiDimArrayClass.Instance.StructArray2D1[7, 3] = new Before::MultiDimArrayClass.ExampleStruct1
			{
				Value = 73
			};

			Assert.IsNull( After::MultiDimArrayClass.Instance );

			Hotload();

			Assert.IsNotNull( After::MultiDimArrayClass.Instance.StructArray2D1 );
			Assert.AreEqual( 10, After::MultiDimArrayClass.Instance.StructArray2D1.GetLength( 0 ) );
			Assert.AreEqual( 20, After::MultiDimArrayClass.Instance.StructArray2D1.GetLength( 1 ) );
			Assert.AreEqual( 73, After::MultiDimArrayClass.Instance.StructArray2D1[7, 3].Value );
		}

		/// <summary>
		/// Test struct block copy of a multi-dimensional array.
		/// </summary>
		[TestMethod]
		public void MultiDimChangedStructArray()
		{
			Before::MultiDimArrayClass.Instance = new Before::MultiDimArrayClass
			{
				StructArray2D2 = new Before::MultiDimArrayClass.ExampleStruct2[10, 20]
			};

			Before::MultiDimArrayClass.Instance.StructArray2D2[7, 3] = new Before::MultiDimArrayClass.ExampleStruct2
			{
				Value = 73
			};

			Assert.IsNull( After::MultiDimArrayClass.Instance );

			Hotload();

			Assert.IsNotNull( After::MultiDimArrayClass.Instance.StructArray2D2 );
			Assert.AreEqual( 10, After::MultiDimArrayClass.Instance.StructArray2D2.GetLength( 0 ) );
			Assert.AreEqual( 20, After::MultiDimArrayClass.Instance.StructArray2D2.GetLength( 1 ) );
			Assert.AreEqual( 73, After::MultiDimArrayClass.Instance.StructArray2D2[7, 3].Value );
		}

		/// <summary>
		/// Test struct array block copy.
		/// </summary>
		[TestMethod]
		public void BlockCopyArray()
		{
			const int testLength = 1_000_000;
			const int testIndex = 123_456;
			const int testInt = 1928;
			const float testFloat = 100f;
			var testVector = new Vector3( 1f, 2f, 3f );

			Before::BlockCopyClass.Instance = new Before::BlockCopyClass { Array = new Before::BlockCopyableStruct[testLength] };
			Before::BlockCopyClass.Instance.Array[testIndex] = new Before::BlockCopyableStruct
			{
				IntValue = testInt,
				FloatValue = testFloat,
				InnerStructValue = testVector
			};

			Assert.IsNull( After::BlockCopyClass.Instance );

			var result = Hotload();

			Assert.IsNotNull( After::BlockCopyClass.Instance );

			Assert.AreEqual( 1_000_000, After::BlockCopyClass.Instance.Array.Length );
			Assert.AreEqual( testInt, After::BlockCopyClass.Instance.Array[testIndex].IntValue );
			Assert.AreEqual( testFloat, After::BlockCopyClass.Instance.Array[testIndex].FloatValue );
			Assert.AreEqual( testVector, After::BlockCopyClass.Instance.Array[testIndex].InnerStructValue );

			Assert.AreEqual( 1, result.TypeTimings["BlockCopyableStruct[]"].Instances );
			Assert.IsFalse( result.TypeTimings.ContainsKey( "BlockCopyableStruct" ) );
		}

		/// <summary>
		/// Test struct list block copy.
		/// </summary>
		[TestMethod]
		public void BlockCopyList()
		{
			const int testLength = 1_000_000;
			const int testIndex = 123_456;
			const int testInt = 1928;
			const float testFloat = 100f;
			var testVector = new Vector3( 1f, 2f, 3f );

			Before::BlockCopyClass.Instance = new Before::BlockCopyClass { List = new List<Before::BlockCopyableStruct>( new Before::BlockCopyableStruct[testLength] ) };
			Before::BlockCopyClass.Instance.List[testIndex] = new Before::BlockCopyableStruct
			{
				IntValue = testInt,
				FloatValue = testFloat,
				InnerStructValue = testVector
			};

			Assert.IsNull( After::BlockCopyClass.Instance );

			var result = Hotload();

			Assert.IsNotNull( After::BlockCopyClass.Instance );

			Assert.AreEqual( 1_000_000, After::BlockCopyClass.Instance.List.Count );
			Assert.AreEqual( testInt, After::BlockCopyClass.Instance.List[testIndex].IntValue );
			Assert.AreEqual( testFloat, After::BlockCopyClass.Instance.List[testIndex].FloatValue );
			Assert.AreEqual( testVector, After::BlockCopyClass.Instance.List[testIndex].InnerStructValue );

			Assert.AreEqual( 1, result.TypeTimings["System.Collections.Generic.List<BlockCopyableStruct>"].Instances );
			Assert.IsFalse( result.TypeTimings.ContainsKey( "BlockCopyableStruct" ) );
		}

		/// <summary>
		/// Test struct list block copy when the list's backing array is larger than its count.
		/// </summary>
		[TestMethod]
		public void BlockCopyListSpareCapacity()
		{
			const int testLength = 1_000_000;
			const int testIndex = 123_456;

			Before::BlockCopyClass.Instance = new Before::BlockCopyClass { List2 = new List<Before::BlockCopyableStruct2>() };

			for ( var i = 0; i < testLength; ++i )
			{
				Before::BlockCopyClass.Instance.List2.Add( new Before::BlockCopyableStruct2 { Index = i } );
			}

			Assert.IsNull( After::BlockCopyClass.Instance );

			var result = Hotload();

			Assert.IsNotNull( After::BlockCopyClass.Instance );

			Assert.AreEqual( 1_000_000, After::BlockCopyClass.Instance.List2.Count );
			Assert.AreEqual( testIndex, After::BlockCopyClass.Instance.List2[testIndex].Index );

			Assert.AreEqual( 1, result.TypeTimings["System.Collections.Generic.List<BlockCopyableStruct2>"].Instances );
			Assert.IsFalse( result.TypeTimings.ContainsKey( "BlockCopyableStruct2" ) );
		}

		[TestMethod]
		public void DictionaryComparer()
		{
			Before::DictionaryClass.Instance = new Before::DictionaryClass
			{
				Dict1 = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase )
			{
				{ "TEST", 28 }
			}
			};

			Assert.IsNull( After::DictionaryClass.Instance );

			Hotload();

			Assert.IsTrue( After::DictionaryClass.Instance.Dict1.ContainsKey( "TEST" ) );
			Assert.IsTrue( After::DictionaryClass.Instance.Dict1.ContainsKey( "test" ) );
			Assert.AreEqual( 28, After::DictionaryClass.Instance.Dict1["test"] );
		}

		[TestMethod]
		public void DictionaryComparerSwappedValues()
		{
			Before::DictionaryClass.Instance = new Before::DictionaryClass
			{
				Dict2 = new Dictionary<string, Before::BasicMembersClass>( StringComparer.OrdinalIgnoreCase )
				{
					{ "TEST", new Before::BasicMembersClass { IntField = 29 } }
				}
			};

			Assert.IsNull( After::DictionaryClass.Instance );

			Hotload();

			Assert.IsTrue( After::DictionaryClass.Instance.Dict2.ContainsKey( "TEST" ) );
			Assert.IsTrue( After::DictionaryClass.Instance.Dict2.ContainsKey( "test" ) );
			Assert.AreEqual( 29, After::DictionaryClass.Instance.Dict2["test"].IntField );
		}

		[TestMethod]
		public void SortedDictionaryComparer()
		{
			Before::DictionaryClass.Instance = new Before::DictionaryClass
			{
				Dict3 = new SortedDictionary<string, Before::BasicMembersClass>( StringComparer.OrdinalIgnoreCase )
				{
					{ "TEST", new Before::BasicMembersClass { IntField = 29 } }
				}
			};

			Assert.IsNull( After::DictionaryClass.Instance );

			Hotload();

			Assert.IsTrue( After::DictionaryClass.Instance.Dict3.ContainsKey( "TEST" ) );
			Assert.IsTrue( After::DictionaryClass.Instance.Dict3.ContainsKey( "test" ) );
			Assert.AreEqual( 29, After::DictionaryClass.Instance.Dict3["test"].IntField );
		}

		[TestMethod]
		public void DictionaryOrder()
		{
			Before::CollectionOrderClass.Instance = new Before::CollectionOrderClass
			{
				Dictionary = new Dictionary<int, Before::CollectionOrderClass.StringWrapper>()
			};

			var random = new Random( 123 );

			for ( var j = 0; j < 1_000_000; ++j )
			{
				var index = random.Next( 0, 1_000 );

				if ( Before::CollectionOrderClass.Instance.Dictionary.ContainsKey( index ) )
				{
					Before::CollectionOrderClass.Instance.Dictionary.Remove( index );
				}
				else
				{
					Before::CollectionOrderClass.Instance.Dictionary.Add( index, $"Item_{j}" );
				}
			}

			var order = Before::CollectionOrderClass.Instance.Dictionary.Select( x => x.Value.Value ).ToArray();

			for ( var i = 0; i < order.Length; ++i )
			{
				Assert.AreEqual( order[i], Before::CollectionOrderClass.Instance.Dictionary.ElementAt( i ).Value.Value );
			}

			Assert.IsNull( After::CollectionOrderClass.Instance );

			Hotload();

			Assert.IsNotNull( After::CollectionOrderClass.Instance?.Dictionary );

			for ( var i = 0; i < order.Length; ++i )
			{
				Assert.AreEqual( order[i], After::CollectionOrderClass.Instance.Dictionary.ElementAt( i ).Value.Value );
			}
		}

		[TestMethod]
		public void EnumField()
		{
			Before::EnumClass.Instance = new Before::EnumClass { EnumValue = Before::TestEnum.C };

			Assert.IsNull( After::EnumClass.Instance );

			Hotload();

			Assert.AreEqual( After::TestEnum.C, After::EnumClass.Instance.EnumValue );
		}

		[TestMethod]
		public void EnumArray()
		{
			Before::EnumClass.Instance = new Before::EnumClass { EnumArray = new Before::TestEnum[] { Before::TestEnum.B } };

			Assert.IsNull( After::EnumClass.Instance );

			Hotload();

			Assert.AreEqual( 1, After::EnumClass.Instance.EnumArray.Length );
			Assert.AreEqual( After::TestEnum.B, After::EnumClass.Instance.EnumArray[0] );
		}

		[TestMethod]
		public void StaticReadonlyField()
		{
			Before::StaticReadonlyClass.Instance.IntValue = 20;
			After::StaticReadonlyClass.Instance.IntValue = 0;

			Hotload();

			Assert.AreEqual( 20, After::StaticReadonlyClass.Instance.IntValue );
		}

		[TestMethod]
		public void InstanceReadonlyField()
		{
			Before::InstanceReadonlyClass.Instance = new Before::InstanceReadonlyClass( 21 );

			Assert.IsNull( After::InstanceReadonlyClass.Instance );

			Hotload();

			Assert.AreEqual( 21, After::InstanceReadonlyClass.Instance.IntValue );
		}

		[TestMethod]
		public void JsonSerializerOptionsField()
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

			Assert.IsFalse( hotload.WatchedAssemblies.Contains( typeof( After::BasicMembersClass ).Assembly ) );

			hotload.UpdateReferences();

			Assert.IsTrue( hotload.WatchedAssemblies.Contains( typeof( After::BasicMembersClass ).Assembly ) );
		}

		/// <summary>
		/// After a hotload, any replaced assemblies must not be auto-watched.
		/// </summary>
		[TestMethod]
		public void DontWatchOldAssembly()
		{
			var hotload = CreateHotload();

			Assert.IsFalse( hotload.WatchedAssemblies.Contains( typeof( Before::BasicMembersClass ).Assembly ) );

			hotload.UpdateReferences();

			Assert.IsFalse( hotload.WatchedAssemblies.Contains( typeof( Before::BasicMembersClass ).Assembly ) );
		}

		/// <summary>
		/// After a hotload, any assemblies replaced with null should not be watched.
		/// </summary>
		[TestMethod]
		public void UnloadAssembly()
		{
			var hotload = CreateHotload();

			hotload.UpdateReferences();

			hotload.ReplacingAssembly( typeof( After::BasicMembersClass ).Assembly, null );

			hotload.UpdateReferences();

			Assert.IsFalse( hotload.WatchedAssemblies.Contains( typeof( Before::BasicMembersClass ).Assembly ) );
			Assert.IsFalse( hotload.WatchedAssemblies.Contains( typeof( After::BasicMembersClass ).Assembly ) );
		}

		[TestMethod]
		public void ClearedList()
		{
			Before::ClearedListClass.Instance = new Before::ClearedListClass();

			// Processing the cleared backing array would count its whole capacity (131,072)
			const int addedItems = 100_000;

			for ( var i = 0; i < addedItems; ++i )
			{
				Before::ClearedListClass.Instance.List.Add( new Before::ListElement() );
			}

			Before::ClearedListClass.Instance.List.Clear();
			Before::ClearedListClass.Instance.List.Add( new Before::ListElement() );

			Assert.IsNull( After::ClearedListClass.Instance );

			Assert.AreEqual( 1, Before::ClearedListClass.Instance.List.Count );

			var result = Hotload();

			Assert.IsNotNull( After::ClearedListClass.Instance );
			Assert.IsNotNull( After::ClearedListClass.Instance.List );

			Assert.AreEqual( 1, After::ClearedListClass.Instance.List.Count );

			Assert.IsFalse( result.InstancesProcessed > addedItems );
		}

		/// <summary>
		/// Tests class hierarchies changing after a swap.
		/// </summary>
		[TestMethod]
		public void AddedBaseClass()
		{
			Before::HierarchyChangeClass.Instance = new Before::HierarchyChangeClass();

			Assert.IsNull( After::HierarchyChangeClass.Instance );

			Before::HierarchyChangeClass.Instance.BaseClassField = new Before::HierarchyChangeClass.TestBaseClass { Field1 = 7 };
			Before::HierarchyChangeClass.Instance.DerivingClassField = new Before::HierarchyChangeClass.TestDerivingClass { Field2 = 21 };

			Hotload();

			Assert.IsNotNull( After::HierarchyChangeClass.Instance );

			Assert.AreEqual( 7, After::HierarchyChangeClass.Instance.BaseClassField.Field1 );
			Assert.AreEqual( 0, After::HierarchyChangeClass.Instance.DerivingClassField.Field1 );
			Assert.AreEqual( 21, After::HierarchyChangeClass.Instance.DerivingClassField.Field2 );
		}

		/// <summary>
		/// Tests an instance field changing type.
		/// </summary>
		[TestMethod]
		public void FieldTypeChanged()
		{
			Before::FieldTypeChangeClass.Instance = new Before::FieldTypeChangeClass { Field = new Before::FieldTypeChangeClass.ExampleClass1() };

			Assert.IsNull( After::FieldTypeChangeClass.Instance );

			Hotload();

			Assert.IsNotNull( After::FieldTypeChangeClass.Instance );
			Assert.IsNull( After::FieldTypeChangeClass.Instance.Field );
		}

		/// <summary>
		/// Tests finding the default value of a new field with a generic type.
		/// </summary>
		[TestMethod]
		public void ResolveGenericTypeDefaultValue()
		{
			Before::GenericDefaultClass.Instance = new Before::GenericDefaultClass();

			Assert.IsNull( After::GenericDefaultClass.Instance );

			Hotload();

			Assert.IsNotNull( After::GenericDefaultClass.Instance );
			Assert.IsNotNull( After::GenericDefaultClass.Instance.Field );
			Assert.AreEqual( 21, After::GenericDefaultClass.Instance.Field.Field );
		}

		/// <summary>
		/// Tests <see cref="IHotloadManaged.Created"/> / <see cref="IHotloadManaged.Destroyed"/> being called.
		/// </summary>
		[TestMethod]
		public void HotloadManagedState()
		{
			Before::ManagedStateClass.Instance = new Before::ManagedStateClass();

			Assert.IsNull( After::ManagedStateClass.Instance );

			Before::ManagedStateClass.Instance.Field1 = "World";
			Before::ManagedStateClass.Instance.Field2 = null;

			Hotload();

			Assert.IsNotNull( After::ManagedStateClass.Instance );
			Assert.AreEqual( "World", After::ManagedStateClass.Instance.Field1 );
			Assert.AreEqual( "World", After::ManagedStateClass.Instance.Field2 );
		}

		/// <summary>
		/// Tests <see cref="IHotloadManaged.Failed"/> being called.
		/// </summary>
		[TestMethod]
		public void HotloadManagedFailed()
		{
			Before::ManagedFailureClass.Instance = new Before::ManagedFailureClass();

			Assert.IsNull( After::ManagedFailureClass.Instance );

			Before::ManagedFailureClass.Instance.RemovedInstance = new Before::ManagedFailureClass.RemovedClass
			{
				Parent = Before::ManagedFailureClass.Instance
			};

			Assert.IsFalse( Before::ManagedFailureClass.Instance.FailureHandled );

			Hotload();

			Assert.IsNotNull( After::ManagedFailureClass.Instance );
			Assert.IsNull( After::ManagedFailureClass.Instance.RemovedInstance );
			Assert.IsTrue( Before::ManagedFailureClass.Instance.FailureHandled );
		}

		[TestMethod]
		public void VoxelArrays()
		{
#pragma warning disable CS0162 // Unreachable code detected
			// ReSharper disable ConditionIsAlwaysTrueOrFalse

			// Each chunk's arrays take the same path, so a few dozen chunks cover it
			const int chunkCount = 96;
			const int chunkSize = 16;
			const int seed = 12345;

			const bool testInterfaceArrays = false;

			Before::VoxelArrayClass.Instance = new Before::VoxelArrayClass();

			Assert.IsNull( After::VoxelArrayClass.Instance );

			Before::VoxelArrayClass.Instance.Chunks = new Before::VoxelArrayClass.Chunk[chunkCount];

			var random = new Random( seed );

			for ( var i = 0; i < chunkCount; i++ )
			{
				var chunk = new Before::VoxelArrayClass.Chunk();

				Before::VoxelArrayClass.Instance.Chunks[i] = chunk;

				chunk.Array = new Before::VoxelArrayClass.Voxel[chunkSize * chunkSize * chunkSize];
				chunk.Array3D = new Before::VoxelArrayClass.Voxel[chunkSize, chunkSize, chunkSize];

				if ( testInterfaceArrays )
				{
					chunk.InterfaceArray = new Before::VoxelArrayClass.IVoxel[chunkSize * chunkSize * chunkSize];
					chunk.InterfaceArray3D = new Before::VoxelArrayClass.IVoxel[chunkSize, chunkSize, chunkSize];
				}

				for ( var x = 0; x < chunkSize; x++ )
					for ( var y = 0; y < chunkSize; y++ )
						for ( var z = 0; z < chunkSize; z++ )
						{
							var rgb = random.Next( 0, 256 * 256 * 256 );
							var voxel = new Before::VoxelArrayClass.Voxel( (byte)(rgb & 0xff), (byte)((rgb >> 8) & 0xff), (byte)((rgb >> 16) & 0xff) );
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

			Assert.IsNotNull( After::VoxelArrayClass.Instance );
			Assert.IsNotNull( After::VoxelArrayClass.Instance.Chunks );

			random = new Random( seed );

			foreach ( var chunk in After::VoxelArrayClass.Instance.Chunks )
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
							var voxel = new After::VoxelArrayClass.Voxel( (byte)(rgb & 0xff), (byte)((rgb >> 8) & 0xff), (byte)((rgb >> 16) & 0xff) );
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
			Before::RecursiveGenericClass.Instance = new Before::RecursiveGenericClass
			{
				SelfReferencingGeneric = new() { Value = new() { Value = 123 } }
			};

			Assert.IsNull( After::RecursiveGenericClass.Instance );

			Hotload();

			Assert.IsNotNull( After::RecursiveGenericClass.Instance );
			Assert.AreEqual( 123, After::RecursiveGenericClass.Instance.SelfReferencingGeneric.Value.Value );
		}

		/// <summary>
		/// Reproducing <see href="https://github.com/Facepunch/sbox/issues/1673"/>.
		/// </summary>
		[TestMethod]
		public void CompareGenericMethods()
		{
			Before::GenericOverloadClass.Instance = new Before::GenericOverloadClass();

			Assert.IsNull( After::GenericOverloadClass.Instance );
			Assert.IsNull( Before::GenericOverloadClass.Instance.MethodInfo );

			Before::GenericOverloadClass.Instance.MethodInfo = typeof( Before::GenericOverloadClass )
				.GetMethods()
				.FirstOrDefault( x => x.Name == nameof( Before::GenericOverloadClass.GenericMethod ) && x.IsGenericMethodDefinition );

			Assert.IsNotNull( Before::GenericOverloadClass.Instance.MethodInfo );

			Hotload();

			Assert.IsNotNull( After::GenericOverloadClass.Instance.MethodInfo );

			Assert.AreEqual( nameof( After::GenericOverloadClass.GenericMethod ), After::GenericOverloadClass.Instance.MethodInfo.Name );
			Assert.AreEqual( typeof( After::GenericOverloadClass ), After::GenericOverloadClass.Instance.MethodInfo.DeclaringType );
		}

		/// <summary>
		/// If types A, B derive C, but type A is changed to derive B, hotload should still process fields from
		/// type C when processing instances of type A.
		/// </summary>
		[TestMethod]
		public void InsertedBaseType()
		{
			Before::InsertedBaseClass.Instance = new Before::InsertedBaseClass.A { AField = 10, CField = 20 };

			Assert.IsNull( After::InsertedBaseClass.Instance );

			Hotload();

			Assert.IsNotNull( After::InsertedBaseClass.Instance );
			Assert.AreEqual( 10, After::InsertedBaseClass.Instance.AField );
			Assert.AreEqual( 20, After::InsertedBaseClass.Instance.CField );
		}

		/// <summary>
		/// If type A derives B, and B derive C, but type A is changed to derive C directly, hotload should still
		/// process fields from type C when processing instances of type A.
		/// </summary>
		[TestMethod]
		public void RemovedBaseType()
		{
			Before::RemovedBaseClass.Instance = new Before::RemovedBaseClass.A { AField = 10, BField = 15, CField = 20 };

			Assert.IsNull( After::RemovedBaseClass.Instance );

			Hotload();

			Assert.IsNotNull( After::RemovedBaseClass.Instance );
			Assert.AreEqual( 10, After::RemovedBaseClass.Instance.AField );
			Assert.AreEqual( 20, After::RemovedBaseClass.Instance.CField );
		}

		/// <summary>
		/// If type A derives B&lt;T1&gt;, but changes to derive B&lt;T2&gt;, hotload should try to process
		/// fields that are common to B&lt;T1&gt; and B&lt;T2&gt;.
		/// </summary>
		[TestMethod]
		public void ChangedBaseTypeGenericArgument()
		{
			Before::BaseGenericArgClass.Instance = new Before::BaseGenericArgClass.A { AField = 10, BField1 = 15, BField2 = 20 };

			Assert.IsNull( After::BaseGenericArgClass.Instance );

			Hotload();

			Assert.IsNotNull( After::BaseGenericArgClass.Instance );
			Assert.AreEqual( 10, After::BaseGenericArgClass.Instance.AField );
			Assert.AreEqual( 15, After::BaseGenericArgClass.Instance.BField1 );
			Assert.AreEqual( null, After::BaseGenericArgClass.Instance.BField2 );
		}

		/// <summary>
		/// Definitely persist anonymous types that don't change members.
		/// </summary>
		[TestMethod]
		public void AnonymousTypeUnchanged()
		{
			Before::AnonymousTypeClass.Unchanged();

			Assert.AreEqual( "{ Property1 = World }", Before::AnonymousTypeClass.Instance?.ToString() );
			Assert.IsNull( After::AnonymousTypeClass.Instance );

			Hotload();

			Assert.AreEqual( "{ Property1 = World }", After::AnonymousTypeClass.Instance?.ToString() );
		}

		/// <summary>
		/// Anonymous type substitution looks for identical property names / types, even if the compiler-generated
		/// name of the type has changed.
		/// </summary>
		[TestMethod]
		public void AnonymousTypeReordered()
		{
			Before::AnonymousTypeClass.Reordered();

			Assert.AreEqual( "{ Property3 = World }", Before::AnonymousTypeClass.Instance?.ToString() );
			Assert.IsNull( After::AnonymousTypeClass.Instance );

			Hotload();

			Assert.AreEqual( "{ Property3 = World }", After::AnonymousTypeClass.Instance?.ToString() );
		}

		/// <summary>
		/// We can't reliably handle adding / removing properties from anonymous types, just warn for now.
		/// </summary>
		private void TestAnonymousTypeChange()
		{
			Assert.IsNull( After::AnonymousTypeClass.Instance );

			var anonType = Before::AnonymousTypeClass.Instance.GetType().GetGenericTypeDefinition();
			var result = Hotload();

			Assert.IsNull( After::AnonymousTypeClass.Instance );
			Assert.IsTrue( result.Warnings.Any( x => x.Member == anonType ) );
		}

		/// <inheritdoc cref="TestAnonymousTypeChange" />
		[TestMethod]
		public void AnonymousTypeAddProperty()
		{
			Before::AnonymousTypeClass.AddProperty();

			Assert.AreEqual( "{ Property4 = World }", Before::AnonymousTypeClass.Instance?.ToString() );

			TestAnonymousTypeChange();
		}

		/// <inheritdoc cref="TestAnonymousTypeChange" />
		[TestMethod]
		public void AnonymousTypeRemoveProperty()
		{
			Before::AnonymousTypeClass.RemoveProperty();

			Assert.AreEqual( "{ Property6 = World, Property7 = 0 }", Before::AnonymousTypeClass.Instance?.ToString() );

			TestAnonymousTypeChange();
		}

		/// <inheritdoc cref="TestAnonymousTypeChange" />
		[TestMethod]
		public void AnonymousTypeChangeProperty()
		{
			Before::AnonymousTypeClass.ChangeProperty();

			Assert.AreEqual( "{ Property8 = World, Property9 = 0 }", Before::AnonymousTypeClass.Instance?.ToString() );

			TestAnonymousTypeChange();
		}

		[TestMethod]
		public void CollectionExpressionSingle()
		{
			Assert.IsNull( Before::CollectionExpressionClass.Collection );

			Before::CollectionExpressionClass.InitializeSingle();

			Assert.IsNull( After::CollectionExpressionClass.Collection );

			Assert.AreEqual( 123, Before::CollectionExpressionClass.Collection!.Single() );

			Hotload();

			Assert.AreEqual( 123, After::CollectionExpressionClass.Collection!.Single() );
		}

		[TestMethod]
		public void CollectionExpressionMultiple()
		{
			Assert.IsNull( Before::CollectionExpressionClass.Collection );

			Before::CollectionExpressionClass.InitializeMultiple();

			Assert.IsNull( After::CollectionExpressionClass.Collection );

			Assert.AreEqual( 2, Before::CollectionExpressionClass.Collection!.Count() );
			Assert.AreEqual( 123, Before::CollectionExpressionClass.Collection.First() );
			Assert.AreEqual( 456, Before::CollectionExpressionClass.Collection.Skip( 1 ).First() );

			Hotload();

			Assert.AreEqual( 2, After::CollectionExpressionClass.Collection!.Count() );
			Assert.AreEqual( 123, After::CollectionExpressionClass.Collection.First() );
			Assert.AreEqual( 456, After::CollectionExpressionClass.Collection.Skip( 1 ).First() );
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

		/// <summary>
		/// Prove that <see cref="ReflectionQueryCache.ClearTypeCache"/> gets called
		/// in <see cref="Sandbox.Engine.GlobalContext.OnHotload"/>.
		/// </summary>
		[TestMethod]
		public void ReflectionQueryCacheClearOnHotload()
		{
			using var ctxScope = GlobalContext.GameScope();

			// Do something that populates the cache

			ReflectionQueryCache.OrderedSerializableMembers( typeof( GameObject ) );

			Assert.IsFalse( ReflectionQueryCache.IsEmpty );

			// This should clear the cache

			Sandbox.Engine.GlobalContext.Current.OnHotload();

			Assert.IsTrue( ReflectionQueryCache.IsEmpty );
		}
	}
}
