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
		public void NonSwappedLambda()
		{
			var testString = "";

			Before::BasicMembersClass.Instance = new Before::BasicMembersClass();
			Before::BasicMembersClass.Instance.ActionSimple = () => testString = "Action Simple!";

			Assert.IsNotNull( Before::BasicMembersClass.Instance.ActionSimple );
			Assert.IsNull( After::BasicMembersClass.Instance );

			Hotload();

			Assert.IsNotNull( Before::BasicMembersClass.Instance.ActionSimple );
			Assert.IsNotNull( After::BasicMembersClass.Instance.ActionSimple );

			// Delegate was created in a non-hotloaded assembly, so should be the same instance after
			Assert.AreEqual( Before::BasicMembersClass.Instance.ActionSimple, After::BasicMembersClass.Instance.ActionSimple );

			Assert.AreEqual( "", testString );

			After::BasicMembersClass.Instance.ActionSimple();

			Assert.AreEqual( "Action Simple!", testString );
		}

		/// <summary>
		/// Test a lambda declared in a swapped assembly that involves a compiler-generated display class.
		/// </summary>
		[TestMethod]
		public void CapturingLambda()
		{
			Before::BasicMembersClass.Instance = new Before::BasicMembersClass();
			Before::BasicMembersClass.Instance.ActionSimple = Before::BasicMembersClass.Instance.CreateAction1( 50 );

			Assert.IsNotNull( Before::BasicMembersClass.Instance.ActionSimple );

			Assert.AreEqual( 0, Before::BasicMembersClass.Instance.IntField );
			Assert.IsNull( After::BasicMembersClass.Instance );

			Hotload();

			Assert.AreEqual( 0, Before::BasicMembersClass.Instance.IntField );
			Assert.AreEqual( 0, After::BasicMembersClass.Instance.IntField );

			Assert.IsNotNull( After::BasicMembersClass.Instance.ActionSimple );

			After::BasicMembersClass.Instance.ActionSimple();

			Assert.AreEqual( 0, Before::BasicMembersClass.Instance.IntField );
			Assert.AreEqual( 50, After::BasicMembersClass.Instance.IntField );
		}

		/// <summary>
		/// Same as <see cref="CapturingLambda"/>, but with a body that references a static method.
		/// </summary>
		[TestMethod]
		public void CapturingLambdaCallingStatic()
		{
			Before::BasicMembersClass.Instance = new Before::BasicMembersClass();
			Before::BasicMembersClass.Instance.ActionSimple = Before::BasicMembersClass.Instance.CreateAction2( 10 );

			Assert.IsNotNull( Before::BasicMembersClass.Instance.ActionSimple );

			Assert.AreEqual( 0, Before::BasicMembersClass.Instance.IntField );
			Assert.IsNull( After::BasicMembersClass.Instance );

			Hotload();

			Assert.AreEqual( 0, Before::BasicMembersClass.Instance.IntField );
			Assert.AreEqual( 0, After::BasicMembersClass.Instance.IntField );

			Assert.IsNotNull( After::BasicMembersClass.Instance.ActionSimple );

			After::BasicMembersClass.Instance.ActionSimple();

			Assert.AreEqual( 0, Before::BasicMembersClass.Instance.IntField );
			Assert.AreEqual( 12, After::BasicMembersClass.Instance.IntField );
		}

		/// <summary>
		/// Tests handling the removal of a lambda definition. At the moment this should replace
		/// the delegate with one that throws an exception explaining that the definition is gone.
		/// </summary>
		[TestMethod]
		public void RemovedLambda()
		{
			Before::BasicMembersClass.Instance = new Before::BasicMembersClass();
			Before::BasicMembersClass.Instance.ActionSimple = Before::BasicMembersClass.Instance.CreateAction3( 10 );

			Assert.IsNotNull( Before::BasicMembersClass.Instance.ActionSimple );

			Assert.AreEqual( 0, Before::BasicMembersClass.Instance.IntField );
			Assert.IsNull( After::BasicMembersClass.Instance );

			var result = Hotload( true );

			Assert.AreEqual( 0, Before::BasicMembersClass.Instance.IntField );
			Assert.AreEqual( 0, After::BasicMembersClass.Instance.IntField );

			// TODO: maybe we can emit a valid version of the old method body
			Assert.ThrowsException<NotImplementedException>( After::BasicMembersClass.Instance.ActionSimple );

			Assert.AreEqual( 1, result.Warnings.Count() );
		}

		/// <summary>
		/// Tests lambdas declared in a removed scope method. This should throw when trying to call the lambda.
		/// </summary>
		[TestMethod]
		public void RemovedScopeMethod()
		{
			Before::LambdaScopeClass.Instance = new Before::LambdaScopeClass();

			Before::LambdaScopeClass.Instance.RemovedMethod();

			Assert.AreEqual( 0, Before::LambdaScopeClass.Instance.Func() );
			Assert.AreEqual( 1, Before::LambdaScopeClass.Instance.Func() );

			var result = Hotload();

			Assert.AreEqual( 1, result.Warnings.Count() );
			Assert.ThrowsException<NotImplementedException>( () => After::LambdaScopeClass.Instance.Func() );
		}

		/// <summary>
		/// Tests lambdas defined dynamically, for example created by DelegateUpgrader.CreateErrorDelegate().
		/// </summary>
		[TestMethod]
		public void CompiledExpressionDelegate()
		{
			Before::BasicMembersClass.Instance = new Before::BasicMembersClass();
			Before::BasicMembersClass.Instance.ActionSimple = Before::BasicMembersClass.Instance.CreateDynamic();

			Assert.ThrowsException<NotImplementedException>( () => Before::BasicMembersClass.Instance.ActionSimple() );

			var result = Hotload( true );

			Assert.ThrowsException<NotImplementedException>( () => After::BasicMembersClass.Instance.ActionSimple() );
		}

		/// <summary>
		/// Tests lambdas capturing other lambdas.
		/// </summary>
		[TestMethod]
		public void LambdaCapturingLambda()
		{
			Before::BasicMembersClass.Instance = new Before::BasicMembersClass();
			Before::NestedActionClass.Instance = new Before::NestedActionClass();
			var inner = Before::BasicMembersClass.Instance.CreateAction4( 10, 20 );

			Before::BasicMembersClass.Instance.ActionSimple = Before::NestedActionClass.Instance.CreateAction1( inner );
			Before::NestedActionClass.Instance.ActionSimple = Before::BasicMembersClass.Instance.ActionSimple;

			Assert.IsNotNull( Before::BasicMembersClass.Instance.ActionSimple );
			Assert.IsNotNull( Before::NestedActionClass.Instance.ActionSimple );

			Assert.AreEqual( 0, Before::BasicMembersClass.Instance.IntField );
			Assert.AreEqual( 0, Before::BasicMembersClass.Instance.IntProperty );

			Assert.IsNull( After::BasicMembersClass.Instance );
			Assert.IsNull( After::NestedActionClass.Instance );

			Hotload();

			Assert.AreEqual( 0, Before::BasicMembersClass.Instance.IntField );
			Assert.AreEqual( 0, Before::BasicMembersClass.Instance.IntProperty );
			Assert.AreEqual( 0, After::BasicMembersClass.Instance.IntField );
			Assert.AreEqual( 0, After::BasicMembersClass.Instance.IntProperty );

			Assert.IsNotNull( After::BasicMembersClass.Instance.ActionSimple );
			Assert.IsNotNull( After::NestedActionClass.Instance.ActionSimple );

			Assert.AreEqual( After::BasicMembersClass.Instance.ActionSimple, After::NestedActionClass.Instance.ActionSimple );

			After::NestedActionClass.Instance.ActionSimple();

			Assert.AreEqual( 0, Before::BasicMembersClass.Instance.IntField );
			Assert.AreEqual( 0, Before::BasicMembersClass.Instance.IntProperty );
			Assert.AreEqual( 10, After::BasicMembersClass.Instance.IntField );
			Assert.AreEqual( 20, After::BasicMembersClass.Instance.IntProperty );
		}

		/// <summary>
		/// Testing another case for nested lambdas.
		/// </summary>
		[TestMethod]
		public void CtorLambdaCapturingLambda()
		{
			Before::NestedActionClass.Instance = new Before::NestedActionClass();
			Before::CtorCaptureClass.Instance = new Before::CtorCaptureClass( 2, 3 );

			Assert.IsNotNull( Before::NestedActionClass.Instance.ActionSimple );

			Assert.AreEqual( 0, Before::CtorCaptureClass.Instance.IntField );
			Assert.AreEqual( 0, Before::CtorCaptureClass.Instance.IntProperty );

			Assert.IsNull( After::NestedActionClass.Instance );
			Assert.IsNull( After::CtorCaptureClass.Instance );

			Hotload();

			Assert.IsNotNull( After::NestedActionClass.Instance.ActionSimple );

			Assert.AreEqual( 0, Before::CtorCaptureClass.Instance.IntField );
			Assert.AreEqual( 0, Before::CtorCaptureClass.Instance.IntProperty );
			Assert.AreEqual( 0, After::CtorCaptureClass.Instance.IntField );
			Assert.AreEqual( 0, After::CtorCaptureClass.Instance.IntProperty );

			After::NestedActionClass.Instance.ActionSimple();

			Assert.AreEqual( 0, Before::CtorCaptureClass.Instance.IntField );
			Assert.AreEqual( 0, Before::CtorCaptureClass.Instance.IntProperty );
			Assert.AreEqual( 2, After::CtorCaptureClass.Instance.IntField );
			Assert.AreEqual( 3, After::CtorCaptureClass.Instance.IntProperty );
		}

		/// <summary>
		/// Tests another more complex nested lambda case.
		/// </summary>
		[TestMethod]
		public void LambdaInsideLambda()
		{
			Before::LambdaScopeClass.Instance = new Before::LambdaScopeClass();

			Before::LambdaScopeClass.Instance.LambdaInLambda();

			Assert.AreEqual( 0, Before::LambdaScopeClass.Instance.Func() );
			Assert.AreEqual( 1, Before::LambdaScopeClass.Instance.Func() );

			Hotload();

			Assert.AreEqual( 7, After::LambdaScopeClass.Instance.Func() );
			Assert.AreEqual( 25, After::LambdaScopeClass.Instance.Func() );
		}

		/// <summary>
		/// Tests a lambda that goes from capturing the declaring scope type to just capturing
		/// a local value.
		/// </summary>
		[TestMethod]
		public void CaptureDropsThis()
		{
			Before::CaptureChangeClass.Instance = new Before::CaptureChangeClass();

			Before::CaptureChangeClass.Instance.IntField = 5;
			Before::CaptureChangeClass.Instance.Delegate = Before::CaptureChangeClass.Instance.Foo( 10 );

			Assert.AreEqual( 15, Before::CaptureChangeClass.Instance.Delegate() );

			Hotload();

			Assert.AreEqual( 10, After::CaptureChangeClass.Instance.Delegate() );
		}

		/// <summary>
		/// Tests a lambda that goes from capturing a local value and <c>this</c> to capturing only <c>this</c>.
		/// </summary>
		[TestMethod]
		public void CaptureDropsLocal()
		{
			Before::CaptureChangeClass.Instance = new Before::CaptureChangeClass();

			Before::CaptureChangeClass.Instance.IntField = 20;
			Before::CaptureChangeClass.Instance.Delegate = Before::CaptureChangeClass.Instance.Bar( 6 );

			Assert.AreEqual( 26, Before::CaptureChangeClass.Instance.Delegate() );

			Hotload();

			Assert.AreEqual( 20, After::CaptureChangeClass.Instance.Delegate() );
		}

		/// <summary>
		/// Tests a lambda changing argument type(s).
		/// </summary>
		[TestMethod]
		public void LambdaParameterTypeChanged()
		{
			Before::SignatureChangeClass.Instance = new Before::SignatureChangeClass();

			Before::SignatureChangeClass.Instance.AssignDelegate();

			Assert.IsNotNull( Before::SignatureChangeClass.Instance.Delegate );
			Assert.IsNull( After::SignatureChangeClass.Instance );

			var result = Hotload();

			Assert.IsTrue( result.Warnings.Any( x => x.Path?.ToString().Contains( "SignatureChangeClass" ) ?? false ) );
			Assert.ThrowsException<NotImplementedException>( () =>
			{
				try
				{
					After::SignatureChangeClass.Instance.Delegate.DynamicInvoke( 1 );
				}
				catch ( TargetInvocationException e )
				{
					throw e.InnerException;
				}
			} );
		}

		/// <summary>
		/// Tests a multicast delegate combining a field initializer lambda and a static method group.
		/// </summary>
		[TestMethod]
		public void MulticastLambdaAndMethodGroup()
		{
			Before::MulticastClass.Instance = new Before::MulticastClass();

			Assert.IsNull( After::MulticastClass.Instance );

			Assert.AreEqual( false, Before::MulticastClass.Bool1 );
			Assert.AreEqual( false, Before::MulticastClass.Bool2 );

			Assert.AreEqual( false, After::MulticastClass.Bool1 );
			Assert.AreEqual( false, After::MulticastClass.Bool2 );

			Hotload();

			Assert.AreEqual( false, After::MulticastClass.Bool1 );
			Assert.AreEqual( false, After::MulticastClass.Bool2 );

			After::MulticastClass.Instance.Action( After::MulticastClass.Instance );

			Assert.AreEqual( true, After::MulticastClass.Bool1 );
			Assert.AreEqual( true, After::MulticastClass.Bool2 );
		}

		/// <summary>
		/// Tests a lambda defined in a non-swapped assembly that captures a swapped method.
		/// </summary>
		[TestMethod]
		public void NonSwappedLambdaCapturingMethodGroup()
		{
			Before::MethodGroupClass.Instance = new Before::MethodGroupClass();

			Action action = Before::MethodGroupClass.Instance.TestMethod;

			Assert.IsNull( After::MethodGroupClass.Instance );

			Before::MethodGroupClass.Instance.Action = () => action();

			Hotload();

			Assert.AreEqual( 0, Before::MethodGroupClass.Instance.IntValue );
			Assert.AreEqual( 0, After::MethodGroupClass.Instance.IntValue );

			After::MethodGroupClass.Instance.Action();

			Assert.AreEqual( 0, Before::MethodGroupClass.Instance.IntValue );
			Assert.AreEqual( 2, After::MethodGroupClass.Instance.IntValue );
		}

		/// <summary>
		/// Tests lambdas declared in a scope method with an ambiguous name.
		/// </summary>
		[TestMethod]
		public void OverloadedScopeMethods()
		{
			Before::SameNameMethodClass.Instance = new Before::SameNameMethodClass();

			Before::SameNameMethodClass.Instance.SameNameMethod();
			Before::SameNameMethodClass.Instance.SameNameMethod( 0 );

			Hotload();

			Assert.AreEqual( 0, Before::SameNameMethodClass.Instance.IntField );
			Assert.AreEqual( 0, After::SameNameMethodClass.Instance.IntField );

			After::SameNameMethodClass.Instance.Action1();

			Assert.AreEqual( 0, Before::SameNameMethodClass.Instance.IntField );
			Assert.AreEqual( 1, After::SameNameMethodClass.Instance.IntField );

			After::SameNameMethodClass.Instance.Action2();

			Assert.AreEqual( 0, Before::SameNameMethodClass.Instance.IntField );
			Assert.AreEqual( 2, After::SameNameMethodClass.Instance.IntField );
		}

		/// <summary>
		/// Tests lambdas declared in a scope method implemented as a generator state machine.
		/// </summary>
		[TestMethod]
		public void IteratorScope()
		{
			Before::LambdaScopeClass.Instance = new Before::LambdaScopeClass();

			var array = Before::LambdaScopeClass.Instance.GeneratorMethod().ToArray();

			Assert.AreEqual( 0, Before::LambdaScopeClass.Instance.Func() );
			Assert.AreEqual( 1, Before::LambdaScopeClass.Instance.Func() );

			Hotload();

			Assert.AreEqual( 2, After::LambdaScopeClass.Instance.Func() );
			Assert.AreEqual( 3, After::LambdaScopeClass.Instance.Func() );
		}

		/// <summary>
		/// Tests lambdas declared in a scope method implemented as an async state machine.
		/// </summary>
		[TestMethod]
		public void AsyncScope()
		{
			Before::LambdaScopeClass.Instance = new Before::LambdaScopeClass();

			Before::LambdaScopeClass.Instance.AsyncMethod().Wait();

			Assert.AreEqual( 0, Before::LambdaScopeClass.Instance.Func() );
			Assert.AreEqual( 1, Before::LambdaScopeClass.Instance.Func() );

			Hotload();

			Assert.AreEqual( 2, After::LambdaScopeClass.Instance.Func() );
			Assert.AreEqual( 3, After::LambdaScopeClass.Instance.Func() );
		}

		/// <summary>
		/// Tests lambdas declared in a scope method involving complex parameter types.
		/// </summary>
		[TestMethod]
		public void TupleArrayParamScope()
		{
			Before::LambdaScopeClass.Instance = new Before::LambdaScopeClass();

			Before::LambdaScopeClass.Instance.ComplexParameterMethod( null );

			Assert.AreEqual( 0, Before::LambdaScopeClass.Instance.Func() );
			Assert.AreEqual( 1, Before::LambdaScopeClass.Instance.Func() );

			Hotload();

			Assert.AreEqual( 2, After::LambdaScopeClass.Instance.Func() );
			Assert.AreEqual( 3, After::LambdaScopeClass.Instance.Func() );
		}

		/// <summary>
		/// Tests lambdas declared in a scope method involving a multi-dimensional array parameter.
		/// </summary>
		[TestMethod]
		public void MultiDimArrayParamScope()
		{
			Before::LambdaScopeClass.Instance = new Before::LambdaScopeClass();

			Before::LambdaScopeClass.Instance.MultiDimArrayMethod( null );

			Assert.AreEqual( 0, Before::LambdaScopeClass.Instance.Func() );
			Assert.AreEqual( 1, Before::LambdaScopeClass.Instance.Func() );

			Hotload();

			Assert.AreEqual( 2, After::LambdaScopeClass.Instance.Func() );
			Assert.AreEqual( 3, After::LambdaScopeClass.Instance.Func() );
		}

		/// <summary>
		/// Tests lambdas declared in a scope method involving a ref parameter.
		/// </summary>
		[TestMethod]
		public void RefParamScope()
		{
			Before::LambdaScopeClass.Instance = new Before::LambdaScopeClass();

			var foo = 0;

			Before::LambdaScopeClass.Instance.ByRefMethod( ref foo );

			Assert.AreEqual( 0, Before::LambdaScopeClass.Instance.Func() );
			Assert.AreEqual( 1, Before::LambdaScopeClass.Instance.Func() );

			Hotload();

			Assert.AreEqual( 2, After::LambdaScopeClass.Instance.Func() );
			Assert.AreEqual( 3, After::LambdaScopeClass.Instance.Func() );
		}

		/// <summary>
		/// Tests lambdas declared in a scope method involving a namespaceless parameter type.
		/// </summary>
		[TestMethod]
		public void NoNamespaceParamScope()
		{
			Before::LambdaScopeClass.Instance = new Before::LambdaScopeClass();
			Before::LambdaScopeClass.Instance.NoNamespaceTypeMethod( new Before::BasicMembersClass { IntField = 7 } );

			Assert.AreEqual( 7, Before::LambdaScopeClass.Instance.Func() );
			Assert.AreEqual( 8, Before::LambdaScopeClass.Instance.Func() );

			Hotload();

			Assert.AreEqual( 9, After::LambdaScopeClass.Instance.Func() );
			Assert.AreEqual( 10, After::LambdaScopeClass.Instance.Func() );
		}

		/// <summary>
		/// Tests lambdas declared in a generic scope method.
		/// </summary>
		[TestMethod]
		public void GenericMethodScope()
		{
			Before::LambdaScopeClass.Instance = new Before::LambdaScopeClass();

			Before::LambdaScopeClass.Instance.GenericMethod<bool>( false );

			Assert.AreEqual( 0, Before::LambdaScopeClass.Instance.Func() );
			Assert.AreEqual( 1, Before::LambdaScopeClass.Instance.Func() );

			Hotload();

			Assert.AreEqual( 2, After::LambdaScopeClass.Instance.Func() );
			Assert.AreEqual( 3, After::LambdaScopeClass.Instance.Func() );
		}

		/// <summary>
		/// Tests generic lambdas declared in a generic scope method.
		/// </summary>
		[TestMethod]
		public void GenericMethodScopeCapturingGenerics()
		{
			Before::LambdaScopeClass.Instance = new Before::LambdaScopeClass();

			Assert.IsNull( Before::LambdaScopeClass.Instance.FuncTarget );

			Before::LambdaScopeClass.Instance.GenericMethod( x => x, 81 );

			Assert.AreEqual( 81, Before::LambdaScopeClass.Instance.Func() );

			Hotload();

			Assert.AreEqual( 81, After::LambdaScopeClass.Instance.Func() );
		}

		/// <summary>
		/// Tests generic lambdas declared in a non-swapped generic scope method, but with swapped type parameters.
		/// </summary>
		[TestMethod]
		public void NonSwappedGenericMethodScope()
		{
			Before::LambdaScopeClass.Instance = new Before::LambdaScopeClass();

			Before::LambdaScopeClass.Instance.ExternalGenericMethod( Before::LambdaScopeClass.Instance, Before::LambdaScopeClass.Instance );

			Assert.IsNotNull( Before::LambdaScopeClass.Instance.Func );

			Hotload();

			Assert.IsNotNull( After::LambdaScopeClass.Instance.Func );
			Assert.AreEqual( 1, After::LambdaScopeClass.Instance.Func() );
		}

		[TestMethod]
		public void GenericTypeScope()
		{
			Assert.IsNull( Before::GenericLambdaClass.Lambda );
			Assert.IsNull( After::GenericLambdaClass.Lambda );

			Before::GenericScopeClass<string>.LambdaScopeMethod( 3 );

			Assert.IsNotNull( Before::GenericLambdaClass.Lambda );

			Assert.AreEqual( "Str", Before::GenericLambdaClass.Lambda() );

			Hotload();

			Assert.IsNotNull( After::GenericLambdaClass.Lambda );

			Assert.AreEqual( "Str", After::GenericLambdaClass.Lambda() );

			Assert.AreNotEqual( Before::GenericLambdaClass.Lambda, After::GenericLambdaClass.Lambda );
		}

		[TestMethod]
		public void GenericMethodInGenericTypeScope()
		{
			Assert.IsNull( Before::GenericLambdaClass.Lambda );
			Assert.IsNull( After::GenericLambdaClass.Lambda );

			Before::GenericScopeClass<string>.GenericLambdaScopeMethod1<int>( 3 );

			Assert.IsNotNull( Before::GenericLambdaClass.Lambda );

			Assert.AreEqual( "StrInt", Before::GenericLambdaClass.Lambda() );

			Hotload();

			Assert.IsNotNull( After::GenericLambdaClass.Lambda );

			Assert.AreEqual( "StrInt", After::GenericLambdaClass.Lambda() );

			Assert.AreNotEqual( Before::GenericLambdaClass.Lambda, After::GenericLambdaClass.Lambda );
		}

		[TestMethod]
		public void ShadowedTypeParameterScope()
		{
			Assert.IsNull( Before::GenericLambdaClass.Lambda );
			Assert.IsNull( After::GenericLambdaClass.Lambda );

			Before::GenericScopeClass<string>.GenericLambdaScopeMethod2<int>( 3 );

			Assert.IsNotNull( Before::GenericLambdaClass.Lambda );

			Assert.AreEqual( "Int", Before::GenericLambdaClass.Lambda() );

			Hotload();

			Assert.IsNotNull( After::GenericLambdaClass.Lambda );

			Assert.AreEqual( "Int", After::GenericLambdaClass.Lambda() );

			Assert.AreNotEqual( Before::GenericLambdaClass.Lambda, After::GenericLambdaClass.Lambda );
		}

		/// <summary>
		/// Tests lambdas declared in a static scope method.
		/// </summary>
		[TestMethod]
		public void StaticMethodScope()
		{
			Before::LambdaScopeClass.Instance = new Before::LambdaScopeClass();

			Before::LambdaScopeClass.StaticMethod();

			Assert.AreEqual( 0, Before::LambdaScopeClass.Instance.Func() );
			Assert.AreEqual( 1, Before::LambdaScopeClass.Instance.Func() );

			Hotload();

			Assert.AreEqual( 2, After::LambdaScopeClass.Instance.Func() );
			Assert.AreEqual( 3, After::LambdaScopeClass.Instance.Func() );
		}

		/// <summary>
		/// Tests a lambda returned from a local function, capturing only that function's parameter.
		/// </summary>
		[TestMethod]
		public void LambdaInLocalFunction()
		{
			Before::NestedScopeClass.Instance = new Before::NestedScopeClass();

			Before::NestedScopeClass.Instance.ScopeMethod1();

			Assert.AreEqual( 4, Before::NestedScopeClass.Instance.Func1() );

			Hotload();

			Assert.AreEqual( 4, After::NestedScopeClass.Instance.Func1() );
		}

		/// <summary>
		/// Tests a lambda returned from a local function, capturing both that function's parameter
		/// and a parameter of the enclosing method.
		/// </summary>
		[TestMethod]
		public void LambdaInLocalFunctionCapturingOuter()
		{
			Before::NestedScopeClass.Instance = new Before::NestedScopeClass();

			Before::NestedScopeClass.Instance.ScopeMethod2( 1 );

			Assert.AreEqual( 5, Before::NestedScopeClass.Instance.Func2() );

			Hotload();

			Assert.AreEqual( 5, After::NestedScopeClass.Instance.Func2() );
		}

		/// <summary>
		/// Tests lambdas declared in a static constructor scope.
		/// </summary>
		[TestMethod]
		public void StaticCtorLambda()
		{
			Assert.AreEqual( 1, Before::StaticCtorClass.Func() );

			Hotload();

			Assert.AreEqual( 1, After::StaticCtorClass.Func() );
		}

		/// <summary>
		/// Tests an event handler lambda subscribed in a static constructor.
		/// </summary>
		[TestMethod]
		public void StaticCtorEventHandler()
		{
			Before::StaticCtorClass.Counter = 0;
			Before::StaticCtorClass.InvokeEvent();

			Assert.AreEqual( 1, Before::StaticCtorClass.Counter );

			Before::StaticCtorClass.InvokeEvent();

			Assert.AreEqual( 2, Before::StaticCtorClass.Counter );

			Hotload();

			After::StaticCtorClass.InvokeEvent();

			Assert.AreEqual( 3, After::StaticCtorClass.Counter );

			After::StaticCtorClass.InvokeEvent();

			Assert.AreEqual( 4, After::StaticCtorClass.Counter );
		}

		/// <summary>
		/// Tests a lambda calling a local function that captures a local variable.
		/// </summary>
		[TestMethod]
		public void LambdaCallingLocalFunction()
		{
			Before::LambdaScopeClass.Instance = new Before::LambdaScopeClass();

			Assert.IsNull( Before::LambdaScopeClass.Instance.FuncTarget );

			Before::LambdaScopeClass.Instance.NestedMethod1();

			Assert.IsNotNull( Before::LambdaScopeClass.Instance.FuncTarget );

			Hotload();

			Assert.IsNotNull( After::LambdaScopeClass.Instance.FuncTarget );
		}

		/// <summary>
		/// Tests a lambda calling a local function that only uses <c>this</c>.
		/// </summary>
		[TestMethod]
		public void LambdaCallingLocalFunctionUsingThis()
		{
			Before::LambdaScopeClass.Instance = new Before::LambdaScopeClass();

			Assert.IsNull( Before::LambdaScopeClass.Instance.FuncTarget );

			Before::LambdaScopeClass.Instance.NestedMethod2();

			Assert.IsNotNull( Before::LambdaScopeClass.Instance.FuncTarget );

			Hotload();

			Assert.IsNotNull( After::LambdaScopeClass.Instance.FuncTarget );
		}

		/// <summary>
		/// Odd case that was causing a stack overflow.
		/// </summary>
		[TestMethod]
		public void InParamGenericStruct()
		{
			Before::GenericStructParamClass.Instance = new Before::GenericStructParamClass();

			Assert.IsNull( After::GenericStructParamClass.Instance );

			Before::GenericStructParamClass.Instance.StoreDelegate();

			Assert.IsNotNull( Before::GenericStructParamClass.Instance.Delegate );

			Hotload();

			Assert.IsNotNull( After::GenericStructParamClass.Instance.Delegate );

			After::GenericStructParamClass.Instance.Delegate.DynamicInvoke( new After::GenericStructParamClass.ValueChangedEvent<string>( "Hello", "World " ) );
		}

		/// <summary>
		/// Static lambda methods should try to hotload if unchanged.
		/// </summary>
		[TestMethod]
		public void StaticLambdaInGenericMethod()
		{
			Before::StaticLambdaClass.Instance = new Before::StaticLambdaClass();

			Assert.IsNull( After::StaticLambdaClass.Instance );

			Before::StaticLambdaClass.CreateFunc<string>();

			Assert.IsNotNull( Before::StaticLambdaClass.Instance.Func );

			Hotload();

			Assert.IsNotNull( After::StaticLambdaClass.Instance.Func );

			Assert.AreEqual( 22, After::StaticLambdaClass.Instance.Func( 21 ) );
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
			Before::ActionGraphClass.Instance = new() { Func = CreateHelloWorldGraph() };

			await Before::ActionGraphClass.Instance.Func();

			Hotload();

			Assert.IsNotNull( After::ActionGraphClass.Instance?.Func );

			await After::ActionGraphClass.Instance.Func();
		}

		/// <summary>
		/// Make sure mutlicast action graph delegates are preserved.
		/// </summary>
		[TestMethod]
		public async Task MulticastActionGraph()
		{
			Before::ActionGraphClass.Instance = new();
			Before::ActionGraphClass.Instance.Func += CreateHelloWorldGraph( "A" );
			Before::ActionGraphClass.Instance.Func += CreateHelloWorldGraph( "B" );
			Before::ActionGraphClass.Instance.Func += CreateHelloWorldGraph( "C" );

			Assert.AreEqual( 3, Before::ActionGraphClass.Instance.Func.GetInvocationList().Length );

			await Before::ActionGraphClass.Instance.Func();

			Hotload();

			Assert.IsNotNull( After::ActionGraphClass.Instance?.Func );

			Assert.AreEqual( 3, After::ActionGraphClass.Instance.Func.GetInvocationList().Length );

			await After::ActionGraphClass.Instance.Func();
		}

		/// <summary>
		/// Gracefully handle delegates with methods that still exist, but the actual target instance
		/// is of a derived type that's been removed.
		/// </summary>
		[TestMethod]
		public void DerivedTargetTypeRemoved()
		{
			new Before::DerivedTargetClass.RemovedType().Populate();

			Assert.IsNotNull( Before::DerivedTargetClass.Instance );

			var result = Hotload();

			Assert.IsNotNull( After::DerivedTargetClass.Instance );
			Assert.ThrowsException<NotImplementedException>( After::DerivedTargetClass.Instance );
		}

		/// <summary>
		/// An async method removed while suspended across a hotload can't resume in the new code. The thread pool runs its
		/// continuation with nothing to catch an exception, so it must not throw (SBOX-NATIVE-TJ6).
		/// </summary>
		[TestMethod]
		public void SuspendedAsyncContinuation()
		{
			_ = Before::SuspendedAsyncClass.Run();

			Assert.IsNotNull( Before::SuspendedAsyncClass.Continuation );

			var result = Hotload( true );

			Assert.AreEqual( 1, result.Warnings.Count() );
			Assert.IsNotNull( After::SuspendedAsyncClass.Continuation );

			// Dropped: runs without throwing, and the removed method doesn't resume
			After::SuspendedAsyncClass.Continuation();
			Assert.AreEqual( 0, After::SuspendedAsyncClass.Resumed );
		}
		/// <summary>
		/// A timer callback removed by a hotload: the thread pool runs it with nothing to catch an exception, so it
		/// must not throw (SBOX-NATIVE-TJ6).
		/// </summary>
		[TestMethod]
		public void RemovedTimerCallback()
		{
			Before::TimerCallbackClass.Callback = Before::TimerCallbackClass.Tick;

			var result = Hotload( true );

			Assert.AreEqual( 1, result.Warnings.Count() );
			Assert.IsNotNull( After::TimerCallbackClass.Callback );

			After::TimerCallbackClass.Callback( null );
		}
	}
}
