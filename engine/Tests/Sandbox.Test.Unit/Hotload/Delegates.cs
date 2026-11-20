using System;

namespace HotloadTests;

/// <summary>
/// Delegates to methods in swapped assemblies, especially lambdas, should be replaced with equivalent delegates
/// in the new assembly. The hotload finds a lambda's new method by matching the method declaring it.
/// </summary>
[TestClass]
public class DelegateTest : HotloadFixtureTestBase
{
	/// <summary>
	/// Test a lambda declared in a swapped assembly that involves a compiler-generated display class.
	/// </summary>
	[TestMethod]
	public async Task CapturingLambda()
	{
		using var fixture = await CreateFixtureAsync(
			"""
			using System;

			public class CapturingLambda : IHotloadTestFixture
			{
				public int IntField;
				public Action ActionSimple;

				public Action CreateAction1( int foo )
				{
					return () =>
					{
						IntField = foo;
					};
				}

				void IHotloadTestFixture.Test()
				{
			-		ActionSimple = CreateAction1( 50 );
			-
					Assert.AreEqual( 0, IntField );
			+
			+		ActionSimple();
			+
			+		Assert.AreEqual( 50, IntField );
				}
			}
			""" );

		await fixture.HotloadAsync();

		// The new delegate should target the new instance

		Assert.AreEqual( 0, GetField( fixture.OldInstance, "IntField" ) );
	}

	/// <summary>
	/// Same as <see cref="CapturingLambda"/>, but with a body that references a static method.
	/// </summary>
	[TestMethod]
	public async Task CapturingLambdaCallingStatic()
	{
		using var fixture = await CreateFixtureAsync(
			"""
			using System;

			public class CapturingLambdaCallingStatic : IHotloadTestFixture
			{
				public int IntField;
				public Action ActionSimple;

			-	public static int SecretValue() => 1;
			+	public static int SecretValue() => 2;

				public Action CreateAction2( int foo )
				{
					return () =>
					{
						IntField = SecretValue() + foo;
					};
				}

				void IHotloadTestFixture.Test()
				{
			-		ActionSimple = CreateAction2( 10 );
			-
					Assert.AreEqual( 0, IntField );
			+
			+		ActionSimple();
			+
			+		Assert.AreEqual( 12, IntField );
				}
			}
			""" );

		await fixture.HotloadAsync();

		Assert.AreEqual( 0, GetField( fixture.OldInstance, "IntField" ) );
	}

	/// <summary>
	/// Tests handling the removal of a lambda definition. At the moment this should replace
	/// the delegate with one that throws an exception explaining that the definition is gone.
	/// </summary>
	[TestMethod]
	public async Task RemovedLambda()
	{
		var result = await HotloadFixtureAsync(
			"""
			using System;

			public class RemovedLambda : IHotloadTestFixture
			{
				public int IntField;
				public Action ActionSimple;

			-	public Action CreateAction3( int foo )
			-	{
			-		return () =>
			-		{
			-			IntField = foo;
			-		};
			-	}
			-
				void IHotloadTestFixture.Test()
				{
			-		ActionSimple = CreateAction3( 10 );
			-
					Assert.AreEqual( 0, IntField );
			+
			+		// TODO: maybe we can emit a valid version of the old method body
			+		Assert.ThrowsException<NotImplementedException>( ActionSimple );
				}
			}
			""",
			allowErrors: true );

		Assert.AreEqual( 1, result.Warnings.Count() );
	}

	/// <summary>
	/// Tests lambdas declared in a removed scope method. This should throw when trying to call the lambda.
	/// </summary>
	[TestMethod]
	public async Task RemovedScopeMethod()
	{
		var result = await HotloadFixtureAsync(
			"""
			using System;

			public class RemovedScopeMethod : IHotloadTestFixture
			{
				public Func<int> Func;

			-	public void RemovedMethod()
			-	{
			-		var counter = 0;
			-
			-		Func = () => counter++;
			-	}
			-
				void IHotloadTestFixture.Test()
				{
			-		RemovedMethod();
			-
			-		Assert.AreEqual( 0, Func() );
			-		Assert.AreEqual( 1, Func() );
			+		Assert.ThrowsException<NotImplementedException>( () => Func() );
				}
			}
			""" );

		Assert.AreEqual( 1, result.Warnings.Count() );
	}

	/// <summary>
	/// Tests delegates compiled at runtime, for example created by DelegateUpgrader.CreateErrorDelegate().
	/// </summary>
	[TestMethod]
	public async Task CompiledExpressionDelegate()
	{
		await HotloadFixtureAsync(
			"""
			using System;
			using System.Linq.Expressions;

			public class CompiledExpressionDelegate : IHotloadTestFixture
			{
				public Action ActionSimple;

				public Action CreateDynamic()
				{
					var throwExpr = Expression.Throw( Expression.Constant( new NotImplementedException( "This isn't implemented!" ) ), typeof( void ) );
					var lambda = Expression.Lambda<Action>( throwExpr );

					return lambda.Compile();
				}

				void IHotloadTestFixture.Test()
				{
			-		ActionSimple = CreateDynamic();
			-
					Assert.ThrowsException<NotImplementedException>( () => ActionSimple() );
				}
			}
			""",
			allowErrors: true );
	}

	/// <summary>
	/// Tests lambdas capturing other lambdas.
	/// </summary>
	[TestMethod]
	public async Task LambdaCapturingLambda()
	{
		using var fixture = await CreateFixtureAsync(
			"""
			using System;

			public class Basic
			{
				public int IntField;
				public int IntProperty { get; set; }
				public Action ActionSimple;

				public Action<bool> CreateAction4( int field, int property )
				{
					return value =>
					{
						if ( value ) IntField = field;
						else IntProperty = property;
					};
				}
			}

			public class Nested
			{
				public Action ActionSimple;

				public Action CreateAction1( Action<bool> inner )
				{
					return () =>
					{
						inner( true );
						inner( false );
					};
				}
			}

			public class LambdaCapturingLambda : IHotloadTestFixture
			{
				public Basic Basic = new();
				public Nested Nested = new();

				void IHotloadTestFixture.Test()
				{
			-		Basic.ActionSimple = Nested.CreateAction1( Basic.CreateAction4( 10, 20 ) );
			-		Nested.ActionSimple = Basic.ActionSimple;
			-
					Assert.AreEqual( 0, Basic.IntField );
					Assert.AreEqual( 0, Basic.IntProperty );
			+
			+		Assert.AreEqual( Basic.ActionSimple, Nested.ActionSimple );
			+
			+		Nested.ActionSimple();
			+
			+		Assert.AreEqual( 10, Basic.IntField );
			+		Assert.AreEqual( 20, Basic.IntProperty );
				}
			}
			""" );

		await fixture.HotloadAsync();

		var oldBasic = GetField( fixture.OldInstance, "Basic" );

		Assert.AreEqual( 0, GetField( oldBasic, "IntField" ) );
	}

	/// <summary>
	/// A lambda declared in a constructor, captured by another lambda.
	/// </summary>
	[TestMethod]
	public async Task CtorLambdaCapturingLambda()
	{
		await HotloadFixtureAsync(
			"""
			using System;

			public class Nested
			{
				public Action ActionSimple;

				public Action CreateAction1( Action<bool> inner )
				{
					return () =>
					{
						inner( true );
						inner( false );
					};
				}
			}

			public class CtorCapture
			{
				public int IntField;
				public int IntProperty { get; set; }

				public CtorCapture( Nested nested, int field, int property )
				{
					nested.ActionSimple = nested.CreateAction1( value =>
					{
						if ( value ) IntField = field;
						else IntProperty = property;
					} );
				}
			}

			public class CtorLambdaCapturingLambda : IHotloadTestFixture
			{
				public Nested Nested = new();
				public CtorCapture Capture;

				void IHotloadTestFixture.Test()
				{
			-		Capture = new CtorCapture( Nested, 2, 3 );
			-
					Assert.AreEqual( 0, Capture.IntField );
					Assert.AreEqual( 0, Capture.IntProperty );
			+
			+		Nested.ActionSimple();
			+
			+		Assert.AreEqual( 2, Capture.IntField );
			+		Assert.AreEqual( 3, Capture.IntProperty );
				}
			}
			""" );
	}

	/// <summary>
	/// Tests another more complex nested lambda case.
	/// </summary>
	[TestMethod]
	public async Task LambdaInsideLambda()
	{
		await HotloadFixtureAsync(
			"""
			using System;
			using System.Linq;

			public class LambdaInsideLambda : IHotloadTestFixture
			{
				public Func<int> Func;

				public void LambdaInLambda()
				{
					var counter = 0;

					Func = () => Enumerable.Range( 1, counter++ ).Sum( x => Enumerable.Range( 1, x ).Sum( y => y * x ) );
				}

				void IHotloadTestFixture.Test()
				{
			-		LambdaInLambda();
			-
			-		Assert.AreEqual( 0, Func() );
			-		Assert.AreEqual( 1, Func() );
			+		Assert.AreEqual( 7, Func() );
			+		Assert.AreEqual( 25, Func() );
				}
			}
			""" );
	}

	/// <summary>
	/// Tests a lambda that goes from capturing the declaring scope type to just capturing
	/// a local value.
	/// </summary>
	[TestMethod]
	public async Task CaptureDropsThis()
	{
		await HotloadFixtureAsync(
			"""
			using System;

			public class CaptureDropsThis : IHotloadTestFixture
			{
				public int IntField;
				public Func<int> Delegate;

				public Func<int> Foo( int x )
				{
			-		return () => x + IntField;
			+		return () => x;
				}

				void IHotloadTestFixture.Test()
				{
			-		IntField = 5;
			-		Delegate = Foo( 10 );
			-
			-		Assert.AreEqual( 15, Delegate() );
			+		Assert.AreEqual( 10, Delegate() );
				}
			}
			""" );
	}

	/// <summary>
	/// Tests a lambda that goes from capturing a local value and <c>this</c> to capturing only <c>this</c>.
	/// </summary>
	[TestMethod]
	public async Task CaptureDropsLocal()
	{
		await HotloadFixtureAsync(
			"""
			using System;

			public class CaptureDropsLocal : IHotloadTestFixture
			{
				public int IntField;
				public Func<int> Delegate;

				public Func<int> Bar( int x )
				{
					if ( x == 0 )
					{
			-			return () => IntField + x + 1;
			+			return () => 1;
					}

			-		return () => x + IntField;
			+		return () => IntField;
				}

				void IHotloadTestFixture.Test()
				{
			-		IntField = 20;
			-		Delegate = Bar( 6 );
			-
			-		Assert.AreEqual( 26, Delegate() );
			+		Assert.AreEqual( 20, Delegate() );
				}
			}
			""" );
	}

	/// <summary>
	/// Tests a lambda changing argument type(s).
	/// </summary>
	[TestMethod]
	public async Task LambdaParameterTypeChanged()
	{
		var result = await HotloadFixtureAsync(
			"""
			using System;
			using System.Reflection;

			public class LambdaParameterTypeChanged : IHotloadTestFixture
			{
				public Delegate Delegate { get; set; }

				public void AssignDelegate()
				{
			-		Delegate = ( int x ) => x + 1;
			+		Delegate = ( float x ) => x + 1f;
				}

				void IHotloadTestFixture.Test()
				{
			-		AssignDelegate();
			-
			-		Assert.IsNotNull( Delegate );
			+		Assert.ThrowsException<NotImplementedException>( () =>
			+		{
			+			try
			+			{
			+				Delegate.DynamicInvoke( 1 );
			+			}
			+			catch ( TargetInvocationException e )
			+			{
			+				throw e.InnerException;
			+			}
			+		} );
				}
			}
			""" );

		Assert.IsTrue( result.Warnings.Any( x => x.Path?.ToString().Contains( "LambdaParameterTypeChanged" ) ?? false ) );
	}

	/// <summary>
	/// Tests a multicast delegate combining a field initializer lambda and a static method group.
	/// </summary>
	[TestMethod]
	public async Task MulticastLambdaAndMethodGroup()
	{
		await HotloadFixtureAsync(
			"""
			using System;

			public class MulticastLambdaAndMethodGroup : IHotloadTestFixture
			{
				public static bool Bool1;
				public static bool Bool2;

				public Action<MulticastLambdaAndMethodGroup> Action = instance => Bool1 = true;

				public MulticastLambdaAndMethodGroup()
				{
					Action += ActionHandler;
				}

				private static void ActionHandler( MulticastLambdaAndMethodGroup instance )
				{
					Bool2 = true;
				}

				void IHotloadTestFixture.Test()
				{
					Assert.IsFalse( Bool1 );
					Assert.IsFalse( Bool2 );
			+
			+		Action( this );
			+
			+		Assert.IsTrue( Bool1 );
			+		Assert.IsTrue( Bool2 );
				}
			}
			""" );
	}

	/// <summary>
	/// Tests lambdas declared in a scope method with an ambiguous name.
	/// </summary>
	[TestMethod]
	public async Task OverloadedScopeMethods()
	{
		using var fixture = await CreateFixtureAsync(
			"""
			using System;

			public class OverloadedScopeMethods : IHotloadTestFixture
			{
				public Action Action1;
				public Action Action2;

				public int IntField;

			+	public void DifferentNameMethod()
			+	{
			+		Action1 = () => IntField = 3;
			+	}
			+
				public void SameNameMethod()
				{
					Action1 = () => IntField = 1;
				}

				public void SameNameMethod( int value )
				{
					Action2 = () => IntField = 2;
				}

				void IHotloadTestFixture.Test()
				{
			-		SameNameMethod();
			-		SameNameMethod( 0 );
			-
					Assert.AreEqual( 0, IntField );
			+
			+		Action1();
			+
			+		Assert.AreEqual( 1, IntField );
			+
			+		Action2();
			+
			+		Assert.AreEqual( 2, IntField );
				}
			}
			""" );

		await fixture.HotloadAsync();

		Assert.AreEqual( 0, GetField( fixture.OldInstance, "IntField" ) );
	}

	/// <summary>
	/// Tests lambdas declared in a scope method implemented as a generator state machine.
	/// </summary>
	[TestMethod]
	public async Task IteratorScope()
	{
		await HotloadFixtureAsync(
			"""
			using System;
			using System.Collections.Generic;
			using System.Linq;

			public class IteratorScope : IHotloadTestFixture
			{
				public Func<int> Func;

				public IEnumerable<int> GeneratorMethod()
				{
					var counter = 0;

					Func = () => counter++;

					yield return 1;
				}

				void IHotloadTestFixture.Test()
				{
			-		_ = GeneratorMethod().ToArray();
			-
			-		Assert.AreEqual( 0, Func() );
			-		Assert.AreEqual( 1, Func() );
			+		Assert.AreEqual( 2, Func() );
			+		Assert.AreEqual( 3, Func() );
				}
			}
			""" );
	}

	/// <summary>
	/// Tests lambdas declared in a scope method implemented as an async state machine.
	/// </summary>
	[TestMethod]
	public async Task AsyncScope()
	{
		await HotloadFixtureAsync(
			"""
			using System;
			using System.Threading.Tasks;

			public class AsyncScope : IHotloadTestFixture
			{
				public Func<int> Func;

				public async Task AsyncMethod()
				{
					var counter = 0;

					Func = () => counter++;

					await Task.Yield();
				}

				void IHotloadTestFixture.Test()
				{
			-		AsyncMethod().Wait();
			-
			-		Assert.AreEqual( 0, Func() );
			-		Assert.AreEqual( 1, Func() );
			+		Assert.AreEqual( 2, Func() );
			+		Assert.AreEqual( 3, Func() );
				}
			}
			""" );
	}

	/// <summary>
	/// Tests lambdas declared in a scope method involving complex parameter types.
	/// </summary>
	[TestMethod]
	public async Task TupleArrayParamScope()
	{
		await HotloadFixtureAsync(
			"""
			using System;

			public class TupleArrayParamScope : IHotloadTestFixture
			{
				public Func<int> Func;

				public void ComplexParameterMethod( (int A, string B)[] arg )
				{
					var counter = 0;

					Func = () => counter++;
				}

				void IHotloadTestFixture.Test()
				{
			-		ComplexParameterMethod( null );
			-
			-		Assert.AreEqual( 0, Func() );
			-		Assert.AreEqual( 1, Func() );
			+		Assert.AreEqual( 2, Func() );
			+		Assert.AreEqual( 3, Func() );
				}
			}
			""" );
	}

	/// <summary>
	/// Tests lambdas declared in a scope method involving a multi-dimensional array parameter.
	/// </summary>
	[TestMethod]
	public async Task MultiDimArrayParamScope()
	{
		await HotloadFixtureAsync(
			"""
			using System;

			public class MultiDimArrayParamScope : IHotloadTestFixture
			{
				public Func<int> Func;

				public void MultiDimArrayMethod( int[,] arg )
				{
					var counter = 0;

					Func = () => counter++;
				}

				void IHotloadTestFixture.Test()
				{
			-		MultiDimArrayMethod( null );
			-
			-		Assert.AreEqual( 0, Func() );
			-		Assert.AreEqual( 1, Func() );
			+		Assert.AreEqual( 2, Func() );
			+		Assert.AreEqual( 3, Func() );
				}
			}
			""" );
	}

	/// <summary>
	/// Tests lambdas declared in a scope method involving a ref parameter.
	/// </summary>
	[TestMethod]
	public async Task RefParamScope()
	{
		await HotloadFixtureAsync(
			"""
			using System;

			public class RefParamScope : IHotloadTestFixture
			{
				public Func<int> Func;

				public void ByRefMethod( ref int arg )
				{
					var counter = 0;

					Func = () => counter++;
				}

				void IHotloadTestFixture.Test()
				{
			-		var foo = 0;
			-		ByRefMethod( ref foo );
			-
			-		Assert.AreEqual( 0, Func() );
			-		Assert.AreEqual( 1, Func() );
			+		Assert.AreEqual( 2, Func() );
			+		Assert.AreEqual( 3, Func() );
				}
			}
			""" );
	}

	/// <summary>
	/// Tests lambdas declared in a scope method involving a namespaceless parameter type.
	/// </summary>
	[TestMethod]
	public async Task NoNamespaceParamScope()
	{
		await HotloadFixtureAsync(
			"""
			using System;

			public class NoNamespaceClass
			{
				public int IntField;
			}

			public class NoNamespaceParamScope : IHotloadTestFixture
			{
				public Func<int> Func;

				public void NoNamespaceTypeMethod( NoNamespaceClass inst )
				{
					var counter = inst.IntField;

					Func = () => counter++;
				}

				void IHotloadTestFixture.Test()
				{
			-		NoNamespaceTypeMethod( new NoNamespaceClass { IntField = 7 } );
			-
			-		Assert.AreEqual( 7, Func() );
			-		Assert.AreEqual( 8, Func() );
			+		Assert.AreEqual( 9, Func() );
			+		Assert.AreEqual( 10, Func() );
				}
			}
			""" );
	}

	/// <summary>
	/// Tests lambdas declared in a generic scope method.
	/// </summary>
	[TestMethod]
	public async Task GenericMethodScope()
	{
		await HotloadFixtureAsync(
			"""
			using System;

			public class GenericMethodScope : IHotloadTestFixture
			{
				public Func<int> Func;

				public void GenericMethod<T>( T arg )
				{
					var counter = 0;

					Func = () => counter++;
				}

				void IHotloadTestFixture.Test()
				{
			-		GenericMethod<bool>( false );
			-
			-		Assert.AreEqual( 0, Func() );
			-		Assert.AreEqual( 1, Func() );
			+		Assert.AreEqual( 2, Func() );
			+		Assert.AreEqual( 3, Func() );
				}
			}
			""" );
	}

	/// <summary>
	/// Tests generic lambdas declared in a generic scope method.
	/// </summary>
	[TestMethod]
	public async Task GenericMethodScopeCapturingGenerics()
	{
		await HotloadFixtureAsync(
			"""
			using System;

			public class GenericMethodScopeCapturingGenerics : IHotloadTestFixture
			{
				public Func<int> Func;

				public void GenericMethod<T>( Func<T, int> func, T value )
				{
					Func = () => func( value );
				}

				void IHotloadTestFixture.Test()
				{
			-		GenericMethod( NonSwappedHelpers.Identity, 81 );
			-
					Assert.AreEqual( 81, Func() );
				}
			}
			""" );
	}

	/// <summary>
	/// Tests generic lambdas declared in a non-swapped generic scope method, but with swapped type parameters.
	/// </summary>
	[TestMethod]
	public async Task NonSwappedGenericMethodScope()
	{
		await HotloadFixtureAsync(
			"""
			using System;

			public class NonSwappedGenericMethodScope : IHotloadTestFixture
			{
				public Func<int> Func;

				public void ExternalGenericMethod<T>( T a, T b )
				{
					var inner = NonSwappedHelpers.GenericMethod( a );
					Func = () => inner( b ) ? 1 : 0;
				}

				void IHotloadTestFixture.Test()
				{
			-		ExternalGenericMethod( this, this );
			-
					Assert.IsNotNull( Func );
			+		Assert.AreEqual( 1, Func() );
				}
			}
			""" );
	}

	[TestMethod]
	public async Task GenericTypeScope()
	{
		await GenericScopeTestAsync( "LambdaScopeMethod( this, 3 )", "Str" );
	}

	[TestMethod]
	public async Task GenericMethodInGenericTypeScope()
	{
		await GenericScopeTestAsync( "GenericLambdaScopeMethod1<int>( this, 3 )", "StrInt" );
	}

	[TestMethod]
	public async Task ShadowedTypeParameterScope()
	{
		await GenericScopeTestAsync( "GenericLambdaScopeMethod2<int>( this, 3 )", "Int" );
	}

	/// <summary>
	/// Calls <paramref name="call"/> on <c>GenericScopeClass&lt;string&gt;</c>, which stores a lambda returning
	/// <paramref name="expected"/>. After the hotload it must be a new lambda, still returning that.
	/// </summary>
	private static async Task GenericScopeTestAsync( string call, string expected, [System.Runtime.CompilerServices.CallerMemberName] string testName = "" )
	{
		using var fixture = await CreateFixtureAsync(
			$$"""
			using System;

			public class GenericScopeClass<T1>
			{
				public static void LambdaScopeMethod( GenericScope target, int param )
				{
					target.Lambda = () => typeof( T1 ).Name.Substring( 0, param );
				}

				public static void GenericLambdaScopeMethod1<T2>( GenericScope target, int param )
				{
					target.Lambda = () => typeof( T1 ).Name.Substring( 0, param ) + typeof( T2 ).Name.Substring( 0, param );
				}

			#pragma warning disable CS0693
				public static void GenericLambdaScopeMethod2<T1>( GenericScope target, int param )
			#pragma warning restore CS0693
				{
					target.Lambda = () => typeof( T1 ).Name.Substring( 0, param );
				}
			}

			public class GenericScope : IHotloadTestFixture
			{
				public Func<string> Lambda;

				void IHotloadTestFixture.Test()
				{
			-		GenericScopeClass<string>.{{call}};
			-
					Assert.IsNotNull( Lambda );
					Assert.AreEqual( "{{expected}}", Lambda() );
				}
			}
			""", testName: testName );

		await fixture.HotloadAsync();

		Assert.AreNotEqual( GetField( fixture.OldInstance, "Lambda" ), GetField( fixture.Instance, "Lambda" ) );
	}

	/// <summary>
	/// Tests lambdas declared in a static scope method.
	/// </summary>
	[TestMethod]
	public async Task StaticMethodScope()
	{
		await HotloadFixtureAsync(
			"""
			using System;

			public class StaticMethodScope : IHotloadTestFixture
			{
				public static StaticMethodScope Instance;

				public Func<int> Func;

				public static void StaticMethod()
				{
					var counter = 0;

					Instance.Func = () => counter++;
				}

				void IHotloadTestFixture.Test()
				{
			-		Instance = this;
			-		StaticMethod();
			-
			-		Assert.AreEqual( 0, Func() );
			-		Assert.AreEqual( 1, Func() );
			+		Assert.AreEqual( 2, Func() );
			+		Assert.AreEqual( 3, Func() );
				}
			}
			""" );
	}

	/// <summary>
	/// Tests a lambda returned from a local function, capturing only that function's parameter.
	/// </summary>
	[TestMethod]
	public async Task LambdaInLocalFunction()
	{
		await HotloadFixtureAsync(
			"""
			using System;

			public class LambdaInLocalFunction : IHotloadTestFixture
			{
				public Func<int> Func1;

				public void ScopeMethod1()
				{
					Func<int> NestedMethod1( int y ) => () => y;

					Func1 = NestedMethod1( 4 );
				}

				void IHotloadTestFixture.Test()
				{
			-		ScopeMethod1();
			-
					Assert.AreEqual( 4, Func1() );
				}
			}
			""" );
	}

	/// <summary>
	/// Tests a lambda returned from a local function, capturing both that function's parameter
	/// and a parameter of the enclosing method.
	/// </summary>
	[TestMethod]
	public async Task LambdaInLocalFunctionCapturingOuter()
	{
		await HotloadFixtureAsync(
			"""
			using System;

			public class LambdaInLocalFunctionCapturingOuter : IHotloadTestFixture
			{
				public Func<int> Func2;

				public void ScopeMethod2( int x )
				{
					Func<int> NestedMethod2( int y ) => () => x + y;

					Func2 = NestedMethod2( 4 );
				}

				void IHotloadTestFixture.Test()
				{
			-		ScopeMethod2( 1 );
			-
					Assert.AreEqual( 5, Func2() );
				}
			}
			""" );
	}

	/// <summary>
	/// Tests lambdas declared in a static constructor scope.
	/// </summary>
	[TestMethod]
	public async Task StaticCtorLambda()
	{
		await HotloadFixtureAsync(
			"""
			using System;

			public static class StaticCtorClass
			{
				public static Func<int> Func;

				static StaticCtorClass()
				{
					Func = () => 1;
				}
			}

			public class StaticCtorLambda : IHotloadTestFixture
			{
				void IHotloadTestFixture.Test()
				{
					Assert.AreEqual( 1, StaticCtorClass.Func() );
				}
			}
			""" );
	}

	/// <summary>
	/// Tests an event handler lambda subscribed in a static constructor.
	/// </summary>
	[TestMethod]
	public async Task StaticCtorEventHandler()
	{
		await HotloadFixtureAsync(
			"""
			using System;

			public static class StaticCtorClass
			{
				public static event Action OnEvent;

				public static int Counter;

				static StaticCtorClass()
				{
					OnEvent += () => Counter++;
				}

				public static void InvokeEvent()
				{
					OnEvent?.Invoke();
				}
			}

			public class StaticCtorEventHandler : IHotloadTestFixture
			{
				void IHotloadTestFixture.Test()
				{
			-		StaticCtorClass.InvokeEvent();
			-		Assert.AreEqual( 1, StaticCtorClass.Counter );
			-
			-		StaticCtorClass.InvokeEvent();
			-		Assert.AreEqual( 2, StaticCtorClass.Counter );
			+		StaticCtorClass.InvokeEvent();
			+		Assert.AreEqual( 3, StaticCtorClass.Counter );
			+
			+		StaticCtorClass.InvokeEvent();
			+		Assert.AreEqual( 4, StaticCtorClass.Counter );
				}
			}
			""" );
	}

	/// <summary>
	/// Tests a lambda calling a local function that captures a local variable.
	/// </summary>
	[TestMethod]
	public async Task LambdaCallingLocalFunction()
	{
		await HotloadFixtureAsync(
			"""
			using System;

			public class LambdaCallingLocalFunction : IHotloadTestFixture
			{
				public object FuncTarget;

				public void NestedMethod1()
				{
					var counter = 0;

					int Example()
					{
						return counter++;
					}

					Func<int, int> func = x => Example() + x;

					FuncTarget = func.Target;
				}

				void IHotloadTestFixture.Test()
				{
			-		Assert.IsNull( FuncTarget );
			-		NestedMethod1();
			-
					Assert.IsNotNull( FuncTarget );
				}
			}
			""" );
	}

	/// <summary>
	/// Tests a lambda calling a local function that only uses <c>this</c>.
	/// </summary>
	[TestMethod]
	public async Task LambdaCallingLocalFunctionUsingThis()
	{
		await HotloadFixtureAsync(
			"""
			using System;

			public class LambdaCallingLocalFunctionUsingThis : IHotloadTestFixture
			{
				public object FuncTarget;
				public int Counter;

				public void NestedMethod2()
				{
					int Example()
					{
						return Counter++;
					}

					Func<int, int> func = x => Example() + x;

					FuncTarget = func.Target;
				}

				void IHotloadTestFixture.Test()
				{
			-		Assert.IsNull( FuncTarget );
			-		NestedMethod2();
			-
					Assert.IsNotNull( FuncTarget );
				}
			}
			""" );
	}

	/// <summary>
	/// Odd case that was causing a stack overflow.
	/// </summary>
	[TestMethod]
	public async Task InParamGenericStruct()
	{
		await HotloadFixtureAsync(
			"""
			using System;

			public class InParamGenericStruct : IHotloadTestFixture
			{
				public readonly struct ValueChangedEvent<T>
				{
					public ValueChangedEvent( T value, T old )
					{
						New = value;
						Old = old;
					}

					public T New { get; }
					public T Old { get; }
				}

				public Delegate Delegate { get; set; }

				public void OnStringChanged( in ValueChangedEvent<string> ev )
				{
					Console.WriteLine( $"Old: {ev.Old}" );
					Console.WriteLine( $"New: {ev.New}" );
				}

				// The compiler generates a delegate type for this, because of the in parameter. Both versions need it.
				public void StoreDelegate()
				{
					Delegate = OnStringChanged;
				}

				void IHotloadTestFixture.Test()
				{
			-		StoreDelegate();
			-
					Assert.IsNotNull( Delegate );
			+
			+		Delegate.DynamicInvoke( new ValueChangedEvent<string>( "Hello", "World " ) );
				}
			}
			""" );
	}

	/// <summary>
	/// Static lambda methods should try to hotload if unchanged.
	/// </summary>
	[TestMethod]
	public async Task StaticLambdaInGenericMethod()
	{
		await HotloadFixtureAsync(
			"""
			using System;

			public class StaticLambdaInGenericMethod : IHotloadTestFixture
			{
				public static StaticLambdaInGenericMethod Instance;

				public Func<int, int> Func { get; set; }

				public static void CreateFunc<T>()
				{
					Instance.Func = static value => value + 1;
				}

				void IHotloadTestFixture.Test()
				{
			-		Instance = this;
			-		CreateFunc<string>();
			-
					Assert.IsNotNull( Func );
					Assert.AreEqual( 22, Func( 21 ) );
				}
			}
			""" );
	}

	/// <summary>
	/// Gracefully handle delegates with methods that still exist, but the actual target instance
	/// is of a derived type that's been removed.
	/// </summary>
	[TestMethod]
	public async Task DerivedTargetTypeRemoved()
	{
		await HotloadFixtureAsync(
			"""
			using System;

			public class DerivedTargetTypeRemoved : IHotloadTestFixture
			{
				public Action Callback;

				public class BaseType
				{
					public string Message => "Hello";

					public Action Populate()
					{
						return () =>
						{
							Console.WriteLine( Message );
						};
					}
				}

			-	public class RemovedType : BaseType;
			-
				void IHotloadTestFixture.Test()
				{
			-		Callback = new RemovedType().Populate();
			-
					Assert.IsNotNull( Callback );
			+		Assert.ThrowsException<NotImplementedException>( Callback );
				}
			}
			""" );
	}

	/// <summary>
	/// An async method removed while suspended across a hotload can't resume in the new code. The thread pool runs its
	/// continuation with nothing to catch an exception, so it must not throw (SBOX-NATIVE-TJ6).
	/// </summary>
	[TestMethod]
	public async Task SuspendedAsyncContinuation()
	{
		var result = await HotloadFixtureAsync(
			"""
			using System;
			using System.Runtime.CompilerServices;
			using System.Threading.Tasks;

			public class SuspendedAsyncContinuation : IHotloadTestFixture
			{
				// Where the thread pool would keep the continuation
				public static Action Continuation;

				public static int Resumed;

				public readonly struct CaptureAwaiter : INotifyCompletion
				{
					public bool IsCompleted => false;
					public void OnCompleted( Action continuation ) => Continuation = continuation;
					public void GetResult() { }
					public CaptureAwaiter GetAwaiter() => this;
				}

			-	// Removed in the hotload, as when its author renames or deletes it while it's suspended
			-	public static async Task Run()
			-	{
			-		await new CaptureAwaiter();
			-		Resumed++;
			-	}
			-
				void IHotloadTestFixture.Test()
				{
			-		_ = Run();
			-
					Assert.IsNotNull( Continuation );
			+
			+		// Dropped: runs without throwing, and the removed method doesn't resume
			+		Continuation();
			+		Assert.AreEqual( 0, Resumed );
				}
			}
			""",
			allowErrors: true );

		Assert.AreEqual( 1, result.Warnings.Count() );
	}

	/// <summary>
	/// A timer callback removed by a hotload: the thread pool runs it with nothing to catch an exception, so it
	/// must not throw (SBOX-NATIVE-TJ6).
	/// </summary>
	[TestMethod]
	public async Task RemovedTimerCallback()
	{
		var result = await HotloadFixtureAsync(
			"""
			using System.Threading;

			public class RemovedTimerCallback : IHotloadTestFixture
			{
				public static TimerCallback Callback;

			-	public static void Tick( object state ) { }
			-
				void IHotloadTestFixture.Test()
				{
			-		Callback = Tick;
			-
					Assert.IsNotNull( Callback );
			+
			+		Callback( null );
				}
			}
			""",
			allowErrors: true );

		Assert.AreEqual( 1, result.Warnings.Count() );
	}
}
