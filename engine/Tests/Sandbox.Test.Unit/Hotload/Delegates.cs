extern alias After;
extern alias Before;

using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Facepunch.ActionGraphs;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox;
using Sandbox.ActionGraphs;
using Sandbox.Internal;

// ReSharper disable PossibleNullReferenceException

namespace HotloadTests
{
	[TestClass]
	[DoNotParallelize]
	public class DelegateTest : HotloadTestBase
	{
		/// <summary>
		/// Test lambdas declared in a non-swapped assembly. These should be preserved.
		/// </summary>
		[TestMethod]
		public void NonSwapped1()
		{
			var testString = "";

			Before::TestClass1.Instance = new Before::TestClass1();
			Before::TestClass1.Instance.ActionSimple = () => testString = "Action Simple!";

			Assert.IsNotNull( Before::TestClass1.Instance.ActionSimple );
			Assert.IsNull( After::TestClass1.Instance );

			Hotload();

			Assert.IsNotNull( Before::TestClass1.Instance.ActionSimple );
			Assert.IsNotNull( After::TestClass1.Instance.ActionSimple );

			// Delegate was created in a non-hotloaded assembly, so should be the same instance after
			Assert.AreEqual( Before::TestClass1.Instance.ActionSimple, After::TestClass1.Instance.ActionSimple );

			Assert.AreEqual( "", testString );

			After::TestClass1.Instance.ActionSimple();

			Assert.AreEqual( "Action Simple!", testString );
		}

		/// <summary>
		/// Test a lambda declared in a swapped assembly that involves a compiler-generated display class.
		/// </summary>
		[TestMethod]
		public void DisplayClass1()
		{
			Before::TestClass1.Instance = new Before::TestClass1();
			Before::TestClass1.Instance.ActionSimple = Before::TestClass1.Instance.CreateAction1( 50 );

			Assert.IsNotNull( Before::TestClass1.Instance.ActionSimple );

			Assert.AreEqual( 0, Before::TestClass1.Instance.IntField );
			Assert.IsNull( After::TestClass1.Instance );

			Hotload();

			Assert.AreEqual( 0, Before::TestClass1.Instance.IntField );
			Assert.AreEqual( 0, After::TestClass1.Instance.IntField );

			Assert.IsNotNull( After::TestClass1.Instance.ActionSimple );

			After::TestClass1.Instance.ActionSimple();

			Assert.AreEqual( 0, Before::TestClass1.Instance.IntField );
			Assert.AreEqual( 50, After::TestClass1.Instance.IntField );
		}

		/// <summary>
		/// Same as <see cref="DisplayClass1"/>, but with a body that references a static method.
		/// </summary>
		[TestMethod]
		public void DisplayClass2()
		{
			Before::TestClass1.Instance = new Before::TestClass1();
			Before::TestClass1.Instance.ActionSimple = Before::TestClass1.Instance.CreateAction2( 10 );

			Assert.IsNotNull( Before::TestClass1.Instance.ActionSimple );

			Assert.AreEqual( 0, Before::TestClass1.Instance.IntField );
			Assert.IsNull( After::TestClass1.Instance );

			Hotload();

			Assert.AreEqual( 0, Before::TestClass1.Instance.IntField );
			Assert.AreEqual( 0, After::TestClass1.Instance.IntField );

			Assert.IsNotNull( After::TestClass1.Instance.ActionSimple );

			After::TestClass1.Instance.ActionSimple();

			Assert.AreEqual( 0, Before::TestClass1.Instance.IntField );
			Assert.AreEqual( 12, After::TestClass1.Instance.IntField );
		}

		/// <summary>
		/// Tests handling the removal of a lambda definition. At the moment this should replace
		/// the delegate with one that throws an exception explaining that the definition is gone.
		/// </summary>
		[TestMethod]
		public void RemovedDeclaration1()
		{
			Before::TestClass1.Instance = new Before::TestClass1();
			Before::TestClass1.Instance.ActionSimple = Before::TestClass1.Instance.CreateAction3( 10 );

			Assert.IsNotNull( Before::TestClass1.Instance.ActionSimple );

			Assert.AreEqual( 0, Before::TestClass1.Instance.IntField );
			Assert.IsNull( After::TestClass1.Instance );

			var result = Hotload( true );

			Assert.AreEqual( 0, Before::TestClass1.Instance.IntField );
			Assert.AreEqual( 0, After::TestClass1.Instance.IntField );

			// TODO: maybe we can emit a valid version of the old method body
			Assert.ThrowsException<NotImplementedException>( After::TestClass1.Instance.ActionSimple );

			Assert.AreEqual( 1, result.Warnings.Count() );
		}

		/// <summary>
		/// Tests lambdas declared in a removed scope method. This should throw when trying to call the lambda.
		/// </summary>
		[TestMethod]
		public void RemovedDeclaration2()
		{
			Before::TestClass17.Instance = new Before::TestClass17();

			Before::TestClass17.Instance.RemovedMethod();

			Assert.AreEqual( 0, Before::TestClass17.Instance.Func() );
			Assert.AreEqual( 1, Before::TestClass17.Instance.Func() );

			var result = Hotload();

			Assert.AreEqual( 1, result.Warnings.Count() );
			Assert.ThrowsException<NotImplementedException>( () => After::TestClass17.Instance.Func() );
		}

		/// <summary>
		/// Tests lambdas defined dynamically, for example created by DelegateUpgrader.CreateErrorDelegate().
		/// </summary>
		[TestMethod]
		public void Dynamic1()
		{
			Before::TestClass1.Instance = new Before::TestClass1();
			Before::TestClass1.Instance.ActionSimple = Before::TestClass1.Instance.CreateDynamic();

			Assert.ThrowsException<NotImplementedException>( () => Before::TestClass1.Instance.ActionSimple() );

			var result = Hotload( true );

			Assert.ThrowsException<NotImplementedException>( () => After::TestClass1.Instance.ActionSimple() );
		}

		/// <summary>
		/// Tests lambdas capturing other lambdas.
		/// </summary>
		[TestMethod]
		public void Nested1()
		{
			Before::TestClass1.Instance = new Before::TestClass1();
			Before::TestClass2.Instance = new Before::TestClass2();
			var inner = Before::TestClass1.Instance.CreateAction4( 10, 20 );

			Before::TestClass1.Instance.ActionSimple = Before::TestClass2.Instance.CreateAction1( inner );
			Before::TestClass2.Instance.ActionSimple = Before::TestClass1.Instance.ActionSimple;

			Assert.IsNotNull( Before::TestClass1.Instance.ActionSimple );
			Assert.IsNotNull( Before::TestClass2.Instance.ActionSimple );

			Assert.AreEqual( 0, Before::TestClass1.Instance.IntField );
			Assert.AreEqual( 0, Before::TestClass1.Instance.IntProperty );

			Assert.IsNull( After::TestClass1.Instance );
			Assert.IsNull( After::TestClass2.Instance );

			Hotload();

			Assert.AreEqual( 0, Before::TestClass1.Instance.IntField );
			Assert.AreEqual( 0, Before::TestClass1.Instance.IntProperty );
			Assert.AreEqual( 0, After::TestClass1.Instance.IntField );
			Assert.AreEqual( 0, After::TestClass1.Instance.IntProperty );

			Assert.IsNotNull( After::TestClass1.Instance.ActionSimple );
			Assert.IsNotNull( After::TestClass2.Instance.ActionSimple );

			Assert.AreEqual( After::TestClass1.Instance.ActionSimple, After::TestClass2.Instance.ActionSimple );

			After::TestClass2.Instance.ActionSimple();

			Assert.AreEqual( 0, Before::TestClass1.Instance.IntField );
			Assert.AreEqual( 0, Before::TestClass1.Instance.IntProperty );
			Assert.AreEqual( 10, After::TestClass1.Instance.IntField );
			Assert.AreEqual( 20, After::TestClass1.Instance.IntProperty );
		}

		/// <summary>
		/// Testing another case for nested lambdas.
		/// </summary>
		[TestMethod]
		public void Nested2()
		{
			Before::TestClass2.Instance = new Before::TestClass2();
			Before::TestClass3.Instance = new Before::TestClass3( 2, 3 );

			Assert.IsNotNull( Before::TestClass2.Instance.ActionSimple );

			Assert.AreEqual( 0, Before::TestClass3.Instance.IntField );
			Assert.AreEqual( 0, Before::TestClass3.Instance.IntProperty );

			Assert.IsNull( After::TestClass2.Instance );
			Assert.IsNull( After::TestClass3.Instance );

			Hotload();

			Assert.IsNotNull( After::TestClass2.Instance.ActionSimple );

			Assert.AreEqual( 0, Before::TestClass3.Instance.IntField );
			Assert.AreEqual( 0, Before::TestClass3.Instance.IntProperty );
			Assert.AreEqual( 0, After::TestClass3.Instance.IntField );
			Assert.AreEqual( 0, After::TestClass3.Instance.IntProperty );

			After::TestClass2.Instance.ActionSimple();

			Assert.AreEqual( 0, Before::TestClass3.Instance.IntField );
			Assert.AreEqual( 0, Before::TestClass3.Instance.IntProperty );
			Assert.AreEqual( 2, After::TestClass3.Instance.IntField );
			Assert.AreEqual( 3, After::TestClass3.Instance.IntProperty );
		}

		/// <summary>
		/// Tests another more complex nested lambda case.
		/// </summary>
		[TestMethod]
		public void Nested3()
		{
			Before::TestClass17.Instance = new Before::TestClass17();

			Before::TestClass17.Instance.LambdaInLambda();

			Assert.AreEqual( 0, Before::TestClass17.Instance.Func() );
			Assert.AreEqual( 1, Before::TestClass17.Instance.Func() );

			Hotload();

			Assert.AreEqual( 7, After::TestClass17.Instance.Func() );
			Assert.AreEqual( 25, After::TestClass17.Instance.Func() );
		}

		/// <summary>
		/// Tests a lambda that goes from capturing the declaring scope type to just capturing
		/// a local value.
		/// </summary>
		[TestMethod]
		public void CaptureTypeChange1()
		{
			Before::TestClass6.Instance = new Before::TestClass6();

			Before::TestClass6.Instance.IntField = 5;
			Before::TestClass6.Instance.Delegate = Before::TestClass6.Instance.Foo( 10 );

			Assert.AreEqual( 15, Before::TestClass6.Instance.Delegate() );

			Hotload();

			Assert.AreEqual( 10, After::TestClass6.Instance.Delegate() );
		}

		/// <summary>
		/// Tests a lambda that goes from a capturing local values to non-capturing.
		/// </summary>
		[TestMethod]
		public void CaptureTypeChange2()
		{
			Before::TestClass6.Instance = new Before::TestClass6();

			Before::TestClass6.Instance.IntField = 20;
			Before::TestClass6.Instance.Delegate = Before::TestClass6.Instance.Bar( 6 );

			Assert.AreEqual( 26, Before::TestClass6.Instance.Delegate() );

			Hotload();

			Assert.AreEqual( 20, After::TestClass6.Instance.Delegate() );
		}

		/// <summary>
		/// Tests a lambda changing argument type(s).
		/// </summary>
		[TestMethod]
		public void SignatureChange1()
		{
			Before::TestClass32.Instance = new Before::TestClass32();

			Before::TestClass32.Instance.AssignDelegate();

			Assert.IsNotNull( Before::TestClass32.Instance.Delegate );
			Assert.IsNull( After::TestClass32.Instance );

			var result = Hotload();

			Assert.IsTrue( result.Warnings.Any( x => x.Path?.ToString().Contains( "TestClass32" ) ?? false ) );
			Assert.ThrowsException<NotImplementedException>( () =>
			{
				try
				{
					After::TestClass32.Instance.Delegate.DynamicInvoke( 1 );
				}
				catch ( TargetInvocationException e )
				{
					throw e.InnerException;
				}
			} );
		}

		/// <summary>
		/// Tests a simple multicast delegate.
		/// </summary>
		[TestMethod]
		public void Multicast1()
		{
			Before::TestClass10.Instance = new Before::TestClass10();

			Assert.IsNull( After::TestClass10.Instance );

			Assert.AreEqual( false, Before::TestClass10.Bool1 );
			Assert.AreEqual( false, Before::TestClass10.Bool2 );

			Assert.AreEqual( false, After::TestClass10.Bool1 );
			Assert.AreEqual( false, After::TestClass10.Bool2 );

			Hotload();

			Assert.AreEqual( false, After::TestClass10.Bool1 );
			Assert.AreEqual( false, After::TestClass10.Bool2 );

			After::TestClass10.Instance.Action( After::TestClass10.Instance );

			Assert.AreEqual( true, After::TestClass10.Bool1 );
			Assert.AreEqual( true, After::TestClass10.Bool2 );
		}

		/// <summary>
		/// Tests a lambda defined in a non-swapped assembly that captures a swapped method.
		/// </summary>
		[TestMethod]
		public void ExternalNested1()
		{
			Before::TestClass11.Instance = new Before::TestClass11();

			Action action = Before::TestClass11.Instance.TestMethod;

			Assert.IsNull( After::TestClass11.Instance );

			Before::TestClass11.Instance.Action = () => action();

			Hotload();

			Assert.AreEqual( 0, Before::TestClass11.Instance.IntValue );
			Assert.AreEqual( 0, After::TestClass11.Instance.IntValue );

			After::TestClass11.Instance.Action();

			Assert.AreEqual( 0, Before::TestClass11.Instance.IntValue );
			Assert.AreEqual( 2, After::TestClass11.Instance.IntValue );
		}

		/// <summary>
		/// Tests lambdas declared in a scope method with an ambiguous name.
		/// </summary>
		[TestMethod]
		public void SameNameScope1()
		{
			Before::TestClass16.Instance = new Before::TestClass16();

			Before::TestClass16.Instance.SameNameMethod();
			Before::TestClass16.Instance.SameNameMethod( 0 );

			Hotload();

			Assert.AreEqual( 0, Before::TestClass16.Instance.IntField );
			Assert.AreEqual( 0, After::TestClass16.Instance.IntField );

			After::TestClass16.Instance.Action1();

			Assert.AreEqual( 0, Before::TestClass16.Instance.IntField );
			Assert.AreEqual( 1, After::TestClass16.Instance.IntField );

			After::TestClass16.Instance.Action2();

			Assert.AreEqual( 0, Before::TestClass16.Instance.IntField );
			Assert.AreEqual( 2, After::TestClass16.Instance.IntField );
		}

		/// <summary>
		/// Tests lambdas declared in a scope method implemented as a generator state machine.
		/// </summary>
		[TestMethod]
		public void StateMachineScope1()
		{
			Before::TestClass17.Instance = new Before::TestClass17();

			var array = Before::TestClass17.Instance.GeneratorMethod().ToArray();

			Assert.AreEqual( 0, Before::TestClass17.Instance.Func() );
			Assert.AreEqual( 1, Before::TestClass17.Instance.Func() );

			Hotload();

			Assert.AreEqual( 2, After::TestClass17.Instance.Func() );
			Assert.AreEqual( 3, After::TestClass17.Instance.Func() );
		}

		/// <summary>
		/// Tests lambdas declared in a scope method implemented as an async state machine.
		/// </summary>
		[TestMethod]
		public void StateMachineScope2()
		{
			Before::TestClass17.Instance = new Before::TestClass17();

			Before::TestClass17.Instance.AsyncMethod().Wait();

			Assert.AreEqual( 0, Before::TestClass17.Instance.Func() );
			Assert.AreEqual( 1, Before::TestClass17.Instance.Func() );

			Hotload();

			Assert.AreEqual( 2, After::TestClass17.Instance.Func() );
			Assert.AreEqual( 3, After::TestClass17.Instance.Func() );
		}

		/// <summary>
		/// Tests lambdas declared in a scope method involving complex parameter types.
		/// </summary>
		[TestMethod]
		public void ComplexParamScope1()
		{
			Before::TestClass17.Instance = new Before::TestClass17();

			Before::TestClass17.Instance.ComplexParameterMethod( null );

			Assert.AreEqual( 0, Before::TestClass17.Instance.Func() );
			Assert.AreEqual( 1, Before::TestClass17.Instance.Func() );

			Hotload();

			Assert.AreEqual( 2, After::TestClass17.Instance.Func() );
			Assert.AreEqual( 3, After::TestClass17.Instance.Func() );
		}

		/// <summary>
		/// Tests lambdas declared in a scope method involving a multi-dimensional array parameter.
		/// </summary>
		[TestMethod]
		public void ComplexParamScope2()
		{
			Before::TestClass17.Instance = new Before::TestClass17();

			Before::TestClass17.Instance.MultiDimArrayMethod( null );

			Assert.AreEqual( 0, Before::TestClass17.Instance.Func() );
			Assert.AreEqual( 1, Before::TestClass17.Instance.Func() );

			Hotload();

			Assert.AreEqual( 2, After::TestClass17.Instance.Func() );
			Assert.AreEqual( 3, After::TestClass17.Instance.Func() );
		}

		/// <summary>
		/// Tests lambdas declared in a scope method involving a ref parameter.
		/// </summary>
		[TestMethod]
		public void ComplexParamScope3()
		{
			Before::TestClass17.Instance = new Before::TestClass17();

			var foo = 0;

			Before::TestClass17.Instance.ByRefMethod( ref foo );

			Assert.AreEqual( 0, Before::TestClass17.Instance.Func() );
			Assert.AreEqual( 1, Before::TestClass17.Instance.Func() );

			Hotload();

			Assert.AreEqual( 2, After::TestClass17.Instance.Func() );
			Assert.AreEqual( 3, After::TestClass17.Instance.Func() );
		}

		/// <summary>
		/// Tests lambdas declared in a scope method involving a namespaceless parameter type.
		/// </summary>
		[TestMethod]
		public void ComplexParamScope4()
		{
			Before::TestClass17.Instance = new Before::TestClass17();
			Before::TestClass17.Instance.NoNamespaceTypeMethod( new Before::TestClass1 { IntField = 7 } );

			Assert.AreEqual( 7, Before::TestClass17.Instance.Func() );
			Assert.AreEqual( 8, Before::TestClass17.Instance.Func() );

			Hotload();

			Assert.AreEqual( 9, After::TestClass17.Instance.Func() );
			Assert.AreEqual( 10, After::TestClass17.Instance.Func() );
		}

		/// <summary>
		/// Tests lambdas declared in a generic scope method.
		/// </summary>
		[TestMethod]
		public void GenericScope1()
		{
			Before::TestClass17.Instance = new Before::TestClass17();

			Before::TestClass17.Instance.GenericMethod<bool>( false );

			Assert.AreEqual( 0, Before::TestClass17.Instance.Func() );
			Assert.AreEqual( 1, Before::TestClass17.Instance.Func() );

			Hotload();

			Assert.AreEqual( 2, After::TestClass17.Instance.Func() );
			Assert.AreEqual( 3, After::TestClass17.Instance.Func() );
		}

		/// <summary>
		/// Tests generic lambdas declared in a generic scope method.
		/// </summary>
		[TestMethod]
		public void GenericScope2()
		{
			Before::TestClass17.Instance = new Before::TestClass17();

			Assert.IsNull( Before::TestClass17.Instance.FuncTarget );

			Before::TestClass17.Instance.GenericMethod( x => x, 81 );

			Assert.AreEqual( 81, Before::TestClass17.Instance.Func() );

			Hotload();

			Assert.AreEqual( 81, After::TestClass17.Instance.Func() );
		}

		/// <summary>
		/// Tests generic lambdas declared in a non-swapped generic scope method, but with swapped type parameters.
		/// </summary>
		[TestMethod]
		public void GenericScope3()
		{
			Before::TestClass17.Instance = new Before::TestClass17();

			Before::TestClass17.Instance.ExternalGenericMethod( Before::TestClass17.Instance, Before::TestClass17.Instance );

			Assert.IsNotNull( Before::TestClass17.Instance.Func );

			Hotload();

			Assert.IsNotNull( After::TestClass17.Instance.Func );
			Assert.AreEqual( 1, After::TestClass17.Instance.Func() );
		}

		[TestMethod]
		public void GenericScope4()
		{
			Assert.IsNull( Before::TestClass22A.Lambda );
			Assert.IsNull( After::TestClass22A.Lambda );

			Before::TestClass22B<string>.LambdaScopeMethod( 3 );

			Assert.IsNotNull( Before::TestClass22A.Lambda );

			Assert.AreEqual( "Str", Before::TestClass22A.Lambda() );

			Hotload();

			Assert.IsNotNull( After::TestClass22A.Lambda );

			Assert.AreEqual( "Str", After::TestClass22A.Lambda() );

			Assert.AreNotEqual( Before::TestClass22A.Lambda, After::TestClass22A.Lambda );
		}

		[TestMethod]
		public void GenericScope5()
		{
			Assert.IsNull( Before::TestClass22A.Lambda );
			Assert.IsNull( After::TestClass22A.Lambda );

			Before::TestClass22B<string>.GenericLambdaScopeMethod1<int>( 3 );

			Assert.IsNotNull( Before::TestClass22A.Lambda );

			Assert.AreEqual( "StrInt", Before::TestClass22A.Lambda() );

			Hotload();

			Assert.IsNotNull( After::TestClass22A.Lambda );

			Assert.AreEqual( "StrInt", After::TestClass22A.Lambda() );

			Assert.AreNotEqual( Before::TestClass22A.Lambda, After::TestClass22A.Lambda );
		}

		[TestMethod]
		public void GenericScope6()
		{
			Assert.IsNull( Before::TestClass22A.Lambda );
			Assert.IsNull( After::TestClass22A.Lambda );

			Before::TestClass22B<string>.GenericLambdaScopeMethod2<int>( 3 );

			Assert.IsNotNull( Before::TestClass22A.Lambda );

			Assert.AreEqual( "Int", Before::TestClass22A.Lambda() );

			Hotload();

			Assert.IsNotNull( After::TestClass22A.Lambda );

			Assert.AreEqual( "Int", After::TestClass22A.Lambda() );

			Assert.AreNotEqual( Before::TestClass22A.Lambda, After::TestClass22A.Lambda );
		}

		/// <summary>
		/// Tests lambdas declared in a static scope method.
		/// </summary>
		[TestMethod]
		public void StaticScope1()
		{
			Before::TestClass17.Instance = new Before::TestClass17();

			Before::TestClass17.StaticMethod();

			Assert.AreEqual( 0, Before::TestClass17.Instance.Func() );
			Assert.AreEqual( 1, Before::TestClass17.Instance.Func() );

			Hotload();

			Assert.AreEqual( 2, After::TestClass17.Instance.Func() );
			Assert.AreEqual( 3, After::TestClass17.Instance.Func() );
		}

		/// <summary>
		/// Tests lambdas implemented inside nested methods.
		/// </summary>
		[TestMethod]
		public void NestedScopeMethod1()
		{
			Before::TestClass25.Instance = new Before::TestClass25();

			Before::TestClass25.Instance.ScopeMethod1();

			Assert.AreEqual( 4, Before::TestClass25.Instance.Func1() );

			Hotload();

			Assert.AreEqual( 4, After::TestClass25.Instance.Func1() );
		}

		/// <summary>
		/// Tests lambdas implemented inside nested methods.
		/// </summary>
		[TestMethod]
		public void NestedScopeMethod2()
		{
			Before::TestClass25.Instance = new Before::TestClass25();

			Before::TestClass25.Instance.ScopeMethod2( 1 );

			Assert.AreEqual( 5, Before::TestClass25.Instance.Func2() );

			Hotload();

			Assert.AreEqual( 5, After::TestClass25.Instance.Func2() );
		}

		/// <summary>
		/// Tests lambdas declared in an instance constructor scope.
		/// </summary>
		[TestMethod]
		public void CtorScope1()
		{
			Assert.AreEqual( 1, Before::TestClass18.Func() );

			Hotload();

			Assert.AreEqual( 1, After::TestClass18.Func() );
		}

		/// <summary>
		/// Another test for lambdas declared in an instance constructor scope.
		/// </summary>
		[TestMethod]
		public void CtorScope2()
		{
			Before::TestClass18.Counter = 0;
			Before::TestClass18.InvokeEvent();

			Assert.AreEqual( 1, Before::TestClass18.Counter );

			Before::TestClass18.InvokeEvent();

			Assert.AreEqual( 2, Before::TestClass18.Counter );

			Hotload();

			After::TestClass18.InvokeEvent();

			Assert.AreEqual( 3, After::TestClass18.Counter );

			After::TestClass18.InvokeEvent();

			Assert.AreEqual( 4, After::TestClass18.Counter );
		}

		/// <summary>
		/// Tests lambdas involving a named method declared within another method.
		/// </summary>
		[TestMethod]
		public void LocalMethod1()
		{
			Before::TestClass17.Instance = new Before::TestClass17();

			Assert.IsNull( Before::TestClass17.Instance.FuncTarget );

			Before::TestClass17.Instance.NestedMethod1();

			Assert.IsNotNull( Before::TestClass17.Instance.FuncTarget );

			Hotload();

			Assert.IsNotNull( After::TestClass17.Instance.FuncTarget );
		}

		/// <summary>
		/// Tests lambdas involving a named method declared within another method.
		/// </summary>
		[TestMethod]
		public void LocalMethod2()
		{
			Before::TestClass17.Instance = new Before::TestClass17();

			Assert.IsNull( Before::TestClass17.Instance.FuncTarget );

			Before::TestClass17.Instance.NestedMethod2();

			Assert.IsNotNull( Before::TestClass17.Instance.FuncTarget );

			Hotload();

			Assert.IsNotNull( After::TestClass17.Instance.FuncTarget );
		}

		/// <summary>
		/// Odd case that was causing a stack overflow.
		/// </summary>
		[TestMethod]
		public void GenericStructParam1()
		{
			Before::TestClass30.Instance = new Before::TestClass30();

			Assert.IsNull( After::TestClass30.Instance );

			Before::TestClass30.Instance.StoreDelegate();

			Assert.IsNotNull( Before::TestClass30.Instance.Delegate );

			Hotload();

			Assert.IsNotNull( After::TestClass30.Instance.Delegate );

			After::TestClass30.Instance.Delegate.DynamicInvoke( new After::TestClass30.ValueChangedEvent<string>( "Hello", "World " ) );
		}

		/// <summary>
		/// Static lambda methods should try to hotload if unchanged.
		/// </summary>
		[TestMethod]
		public void StaticAnonymous1()
		{
			Before::TestClass31.Instance = new Before::TestClass31();

			Assert.IsNull( After::TestClass31.Instance );

			Before::TestClass31.CreateFunc<string>();

			Assert.IsNotNull( Before::TestClass31.Instance.Func );

			Hotload();

			Assert.IsNotNull( After::TestClass31.Instance.Func );

			Assert.AreEqual( 22, After::TestClass31.Instance.Func( 21 ) );
		}

		private Func<Task> CreateHelloWorldGraph( string message = "World" )
		{
			var graph = ActionGraph.CreateDelegate<Func<Task>>( Nodes );
			var start = graph.Graph.InputNode!;
			var log = graph.Graph.AddNode( LogNodes.Info );

			log.Inputs.Signal.SetLink( start.Outputs.Signal );
			log.Inputs["format"].Value = "Hello, {0}!";
			log.Inputs["args"].Value = new object[] { message };

			return graph;
		}

		/// <summary>
		/// Special handling for delegates implemented by an <see cref="ActionGraph"/>.
		/// </summary>
		[TestMethod]
		public async Task SimpleActionGraph()
		{
			Before::TestClass37.Instance = new() { Func = CreateHelloWorldGraph() };

			await Before::TestClass37.Instance.Func();

			Hotload();

			Assert.IsNotNull( After::TestClass37.Instance?.Func );

			await After::TestClass37.Instance.Func();
		}

		/// <summary>
		/// Make sure mutlicast action graph delegates are preserved.
		/// </summary>
		[TestMethod]
		public async Task MulticastActionGraph()
		{
			Before::TestClass37.Instance = new();
			Before::TestClass37.Instance.Func += CreateHelloWorldGraph( "A" );
			Before::TestClass37.Instance.Func += CreateHelloWorldGraph( "B" );
			Before::TestClass37.Instance.Func += CreateHelloWorldGraph( "C" );

			Assert.AreEqual( 3, Before::TestClass37.Instance.Func.GetInvocationList().Length );

			await Before::TestClass37.Instance.Func();

			Hotload();

			Assert.IsNotNull( After::TestClass37.Instance?.Func );

			Assert.AreEqual( 3, After::TestClass37.Instance.Func.GetInvocationList().Length );

			await After::TestClass37.Instance.Func();
		}

		/// <summary>
		/// Gracefully handle delegates with methods that still exist, but the actual target instance
		/// is of a derived type that's been removed.
		/// </summary>
		[TestMethod]
		public void DerivedTargetTypeRemoved()
		{
			new Before::TestClass45.RemovedType().Populate();

			Assert.IsNotNull( Before::TestClass45.Instance );

			var result = Hotload();

			Assert.IsNotNull( After::TestClass45.Instance );
			Assert.ThrowsException<NotImplementedException>( After::TestClass45.Instance );
		}
	}
}
