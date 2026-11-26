using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using Sandbox.Upgraders;

namespace HotloadTests;

/// <summary>
/// Delegates to methods in swapped assemblies, especially lambdas, should be replaced with equivalent delegates
/// in the new assembly. The hotload finds a lambda's new method by matching the method declaring it.
/// </summary>
[TestClass]
public class DelegateTest : HotloadFixtureTestBase
{
	[DisableAutoCreation]
	private sealed class FailingTargetUpgrader( Type targetType ) : SkipUpgrader
	{
		/// <summary>
		/// Injects a migration failure for one subscriber's receiver.
		/// </summary>
		public override bool ShouldProcessType( Type type ) => type == targetType;

		protected override bool OnTryCreateNewInstance( object oldInstance, out object newInstance )
		{
			throw new InvalidOperationException( "Receiver upgrade failed" );
		}
	}

	/// <summary>
	/// A target upgrader that throws during rebinding cannot clear the other subscribers.
	/// </summary>
	[TestMethod]
	public async Task TargetUpgradeFailureIsContained()
	{
		using var fixture = await CreateFixtureAsync(
			"""
			using System;

			internal class Target
			{
				internal void Handle() { }
				internal int Read() => 1;
			}

			internal class Receiver : IHotloadTestFixture
			{
				private Action Notify;
				private Func<int> Read;
				private int Calls;
				private void Survive() => Calls++;

				void IHotloadTestFixture.Test()
				{
			-		var target = new Target();
			-		Notify = (Action)target.Handle + Survive;
			-		Read = target.Read;
			+		Notify();
			+		Assert.AreEqual( 1, Calls );
			+		Assert.ThrowsException<NotImplementedException>( () => Read() );
				}
			}
			""" );

		var targetType = fixture.OldAssembly.GetType( "Target" );
		var result = await fixture.HotloadAsync( allowErrors: true, configure: hotload => hotload.AddUpgrader( new FailingTargetUpgrader( targetType ) ) );
		Assert.IsTrue( result.Errors.Any( entry => entry.Exception is InvalidOperationException { Message: "Receiver upgrade failed" } ) );
	}

	/// <summary>
	/// Generated callbacks can retain their code while their closed target, expression captures
	/// and user-defined delegate type move to the incoming assembly.
	/// </summary>
	[TestMethod]
	public async Task GeneratedCallbacksFollowMigratedTargets()
	{
		using var fixture = await CreateFixtureAsync(
			"""
			using System;

			internal class Receiver : IHotloadTestFixture
			{
				internal delegate object ReadTarget();
				internal ReadTarget Bound;
				internal Func<object> RequiresOldType;
				internal Func<object> Captured;
				internal Func<int> Unbound;
			}
			""" );

		var original = fixture.OldInstance;

		static DynamicMethod ReadTarget( Type receiver )
		{
			var method = new DynamicMethod( "<ReadTarget>", typeof( object ), [receiver] );
			var il = method.GetILGenerator();
			il.Emit( OpCodes.Ldarg_0 );
			il.Emit( OpCodes.Ret );
			return method;
		}

		var readTarget = ReadTarget( typeof( object ) );
		SetField( original, "Bound", readTarget.CreateDelegate( original.GetType().GetNestedType( "ReadTarget", BindingFlags.NonPublic ), original ) );
		SetField( original, "RequiresOldType", ReadTarget( original.GetType() ).CreateDelegate<Func<object>>( original ) );

		var captured = Expression.Constant( original, typeof( object ) );
		SetField( original, "Captured", Expression.Lambda<Func<object>>( captured ).Compile() );

		var readInt = new DynamicMethod( "ReadInt", typeof( int ), Type.EmptyTypes );
		var il = readInt.GetILGenerator();
		il.Emit( OpCodes.Ldc_I4_7 );
		il.Emit( OpCodes.Ret );
		var unbound = readInt.CreateDelegate<Func<int>>();
		SetField( original, "Unbound", unbound );

		await fixture.HotloadAsync();

		var bound = (Delegate)GetField( fixture.Instance, "Bound" );
		Assert.AreSame( fixture.Instance.GetType().GetNestedType( "ReadTarget", BindingFlags.NonPublic ), bound.GetType() );
		Assert.AreSame( fixture.Instance, bound.DynamicInvoke() );
		var incompatible = (Func<object>)GetField( fixture.Instance, "RequiresOldType" );
		Assert.ThrowsException<NotImplementedException>( () => incompatible() );
		Assert.AreSame( fixture.Instance, ((Func<object>)GetField( fixture.Instance, "Captured" ))() );
		Assert.AreSame( unbound, GetField( fixture.Instance, "Unbound" ) );
		Assert.AreEqual( 7, unbound() );
	}

	/// <summary>
	/// An emitted call, cast or local can still reference the outgoing assembly even when the delegate signature matches.
	/// </summary>
	[TestMethod]
	public async Task GeneratedCallbacksWithOutgoingMetadata()
	{
		using var fixture = await CreateFixtureAsync(
			"""
			using System;

			internal class Receiver : IHotloadTestFixture
			{
				internal Func<int> ReadStatic;
				internal Func<int> OldLocal;
				internal Action Notify;
				internal int Calls;

			-	internal static int Generation() => 1;
			+	internal static int Generation() => 2;
				internal void Increment() => Calls++;
			}
			""" );

		var original = fixture.OldInstance;
		var generation = Expression.Call( original.GetType(), "Generation", null );
		var readStatic = Expression.Lambda<Func<int>>( generation ).Compile();
		Assert.AreEqual( 1, readStatic() );
		SetField( original, "ReadStatic", readStatic );

		var increment = original.GetType().GetMethod( "Increment", BindingFlags.Instance | BindingFlags.NonPublic );
		var notify = Expression.Lambda<Action>( Expression.Call( Expression.Constant( original ), increment ) ).Compile();
		SetField( original, "Notify", notify + increment.CreateDelegate<Action>( original ) );

		var oldLocal = new DynamicMethod( "ReadLocal", typeof( int ), Type.EmptyTypes );
		var il = oldLocal.GetILGenerator();
		il.DeclareLocal( original.GetType() );
		il.Emit( OpCodes.Ldc_I4_7 );
		il.Emit( OpCodes.Ret );
		SetField( original, "OldLocal", oldLocal.CreateDelegate<Func<int>>() );

		await fixture.HotloadAsync();

		var migratedStatic = (Func<int>)GetField( fixture.Instance, "ReadStatic" );
		Assert.ThrowsException<NotImplementedException>( () => migratedStatic() );
		var migratedLocal = (Func<int>)GetField( fixture.Instance, "OldLocal" );
		Assert.ThrowsException<NotImplementedException>( () => migratedLocal() );
		((Action)GetField( fixture.Instance, "Notify" ))();
		Assert.AreEqual( 1, GetField( fixture.Instance, "Calls" ) );
	}

	/// <summary>
	/// Open instance methods and static methods with a bound first argument both follow the new code.
	/// </summary>
	[TestMethod]
	public async Task OpenInstanceAndClosedStaticMethods()
	{
		await HotloadFixtureAsync(
			"""
			using System;

			internal class Receiver : IHotloadTestFixture
			{
				private Func<Receiver, int> Open;
				private Func<int> Closed;
				private Func<int> ClosedOverNull;

			-	private int Read() => 1;
			+	private int Read() => 2;
				private static int ReadFrom( Receiver receiver ) => receiver.Read();
			-	private static int ReadNull( Receiver receiver ) => receiver is null ? 1 : 0;
			+	private static int ReadNull( Receiver receiver ) => receiver is null ? 2 : 0;

				void IHotloadTestFixture.Test()
				{
			-		Open = ((Func<int>)Read).Method.CreateDelegate<Func<Receiver, int>>();
			-		Closed = ((Func<Receiver, int>)ReadFrom).Method.CreateDelegate<Func<int>>( this );
			-		ClosedOverNull = ((Func<Receiver, int>)ReadNull).Method.CreateDelegate<Func<int>>( null );
					Assert.AreEqual( Read(), Open( this ) );
					Assert.AreEqual( Read(), Closed() );
					Assert.AreSame( this, Closed.Target );
					Assert.AreEqual( Read(), ClosedOverNull() );
				}
			}
			""" );
	}

	/// <summary>
	/// Open lambdas use the receiver supplied by their caller. A signature edit must not bind that receiver to null.
	/// </summary>
	[TestMethod]
	public async Task OpenLambdasPreserveTheirBinding()
	{
		await HotloadFixtureAsync(
			"""
			using System;

			internal class Receiver : IHotloadTestFixture
			{
				private Delegate Open;
				private Action<Receiver> Notify;
				private int Calls;

			-	private Func<int> Read() => () => 1;
			+	private Func<int> Read() => () => 2;
			-	private Action Make() => () => Calls++;
			+	private Action<Receiver> Make() => other => Calls += other.Calls;
				private static void Survivor( Receiver other ) => other.Calls++;

				void IHotloadTestFixture.Test()
				{
					var closed = Read();
			-		var openType = typeof(Func<,>).MakeGenericType( closed.Target.GetType(), typeof(int) );
			-		Open = closed.Method.CreateDelegate( openType );
			-		Notify = Make().Method.CreateDelegate<Action<Receiver>>() + Survivor;
					Assert.AreEqual( closed(), Open.DynamicInvoke( closed.Target ) );
			+		Notify( this );
			+		Assert.AreEqual( 1, Calls );
				}
			}
			""" );
	}

	/// <summary>
	/// A previously static handler has no receiver, and an existing closure has no value for a new capture.
	/// Both failures must leave the remaining subscriber callable.
	/// </summary>
	[TestMethod]
	public async Task HandlersRequiringNewStateAreSkipped()
	{
		await HotloadFixtureAsync(
			"""
			using System;

			internal class Receiver : IHotloadTestFixture
			{
				private Action Notify;
				private int Calls;

			-	private static void Handle() { }
			+	private void Handle() => Calls++;
			-	private static void Generic<T>() { }
			+	private static void Generic<T>() where T : struct { }
			-	private class Handler<T>
			+	private class Handler<T> where T : struct
				{
					internal static void Run() { }
				}
			-	private Action Make() => () => { };
			+	private Action Make() => () => Calls++;
			-	private Action Capture( int n ) => () => Calls++;
			+	private Action Capture( int n ) => () => Calls += n;
				private void Survivor() => Calls++;

				void IHotloadTestFixture.Test()
				{
			-		Notify = Handle;
			-		Notify += Generic<string>;
			-		Notify += Handler<string>.Run;
			-		var boundToNull = Make().Method.CreateDelegate<Action>( null );
			-		boundToNull();
			-		Notify += boundToNull;
			-		Notify += Capture( 10 );
			-		Notify += Survivor;
			+		Notify();
			+		Assert.AreEqual( 1, Calls );
				}
			}
			""" );
	}

	/// <summary>
	/// Covers retaining, adding and releasing the receiver and a saved local.
	/// </summary>
	public static IEnumerable<object[]> CaptureChanges()
	{
		var states = new (string Expression, bool Receiver, bool Local)[]
		{
			("0", false, false),
			("Value", true, false),
			("saved", false, true),
			("Value + saved", true, true)
		};

		foreach ( var before in states )
			foreach ( var after in states )
			{
				bool available = (!after.Receiver || before.Receiver) && (!after.Local || before.Local);
				yield return [before.Expression, after.Expression, available];
			}
	}

	/// <summary>
	/// Lambdas and local functions follow the edited body whenever the old binding has all required captures.
	/// </summary>
	[TestMethod]
	[DynamicData( nameof( CaptureChanges ), DynamicDataSourceType.Method )]
	public async Task CaptureChangesUseAvailableState( string before, string after, bool available )
	{
		var check = available ? "Assert.AreEqual( Make( 100 ).Read(), read() );"
			: "Assert.ThrowsException<NotImplementedException>( () => read() );";

		await HotloadFixtureAsync(
			$$"""
			using System;

			internal class Receiver : IHotloadTestFixture
			{
				private int Value = 10;
				private Func<int> Lambda, Function;

				private (Func<int> Read, Func<int> Local) Make( int saved )
				{
			-		int Local() => 1 + {{before}};
			+		int Local() => 2 + {{after}};
			-		return (() => 1 + {{before}}, Local);
			+		return (() => 2 + {{after}}, Local);
				}

				void IHotloadTestFixture.Test()
				{
			-		(Lambda, Function) = Make( 100 );
					foreach ( var read in new[] { Lambda, Function } )
					{
			-			Assert.AreEqual( Make( 100 ).Read(), read() );
			+			{{check}}
					}
				}
			}
			""" );
	}

	/// <summary>
	/// Each subscriber can use the values saved in its shared closure. An edit that needs
	/// another value only invalidates the subscribers that need it.
	/// </summary>
	[TestMethod]
	public async Task SubscribersSharingEditedCaptures()
	{
		await HotloadFixtureAsync(
			"""
			using System;

			internal class Receiver : IHotloadTestFixture
			{
				private Action Notify;
				private Func<int> Read;
				private int Calls;

				private void Subscribe( string first, string second )
				{
			-		Notify = () => Calls += first.Length;
			+		Notify = () => Calls += first.Length + second.Length;
					Notify += () => Calls += first.Length;
			-		Read = () => first.Length;
			+		Read = () => second.Length;
				}

				private Action Nested( string first, string second )
				{
					Action Inner( int n )
					{
			-			return () => Calls += n + first.Length;
			+			return () => Calls += n + second.Length;
					}
					return Inner( 2 );
				}

				void IHotloadTestFixture.Test()
				{
			-		Subscribe( "abc", "hello" );
			-		Notify += Nested( "abc", "hello" );
					Calls = 0;
					Notify();
			-		Assert.AreEqual( 11, Calls );
			-		Assert.AreEqual( 3, Read() );
			+		Assert.AreEqual( 3, Calls );
			+		Assert.ThrowsException<NotImplementedException>( () => Read() );
				}
			}
			""" );
	}

	/// <summary>
	/// A nested subscriber can still read an outer capture when its sibling starts reading a new one.
	/// </summary>
	[TestMethod]
	public async Task NestedSubscribersSharingEditedCaptures()
	{
		await HotloadFixtureAsync(
			"""
			using System;

			internal class Receiver : IHotloadTestFixture
			{
				private Action Notify;
				private int Calls;

				private Action Subscribe( string first, string second )
				{
					Action Inner( int n )
					{
			-			Action changed = () => Calls += n + first.Length;
			+			Action changed = () => Calls += n + second.Length;
						return changed + (() => Calls += n + first.Length);
					}

					return Inner( 2 );
				}

				void IHotloadTestFixture.Test()
				{
			-		Notify = Subscribe( "abc", "hello" );
					Calls = 0;
					Notify();
			-		Assert.AreEqual( 10, Calls );
			+		Assert.AreEqual( 5, Calls );
				}
			}
			""" );
	}

	/// <summary>
	/// A closed callback can drop its receiver when the incoming method becomes static.
	/// </summary>
	[TestMethod]
	public async Task ClosedCallbacksCanDropTheirReceiver()
	{
		await HotloadFixtureAsync(
			"""
			using System;

			internal class Receiver : IHotloadTestFixture
			{
				private Func<int> LocalCallback;
				private Func<int> NamedCallback;
				private Func<int> ClosureCallback;
				private Func<int> GenericCallback;
				private int Value = 11;
			-	private int Named() => Value;
			+	private static int Named() => 22;

				private Func<int> Make()
				{
			-		int Local() => Value;
			+		static int Local() => 22;
					return Local;
				}

				private Func<int> Capture( int value )
				{
			-		int Local() => value;
			+		static int Local() => 22;
					return Local;
				}

				private class Owner<T>
				{
					internal static Func<int> Capture<U>( T first, U second )
					{
			-			int Local() => first is int n && second is string ? n : 0;
			+			static int Local() => typeof(T) == typeof(int) && typeof(U) == typeof(string) ? 22 : 0;
						return Local;
					}
				}

				void IHotloadTestFixture.Test()
				{
			-		LocalCallback = Make();
			-		NamedCallback = Named;
			-		ClosureCallback = Capture( Value );
			-		GenericCallback = Owner<int>.Capture( Value, "saved" );
					Assert.AreEqual( Named(), LocalCallback() );
					Assert.AreEqual( Named(), NamedCallback() );
					Assert.AreEqual( Named(), ClosureCallback() );
					Assert.AreEqual( Named(), GenericCallback() );
				}
			}
			""" );
	}

	/// <summary>
	/// Method groups for static local functions invoke the incoming body, including a constructed generic function.
	/// </summary>
	[TestMethod]
	public async Task StaticLocalFunctionMethodGroups()
	{
		await HotloadFixtureAsync(
			"""
			using System;

			internal class Receiver : IHotloadTestFixture
			{
				private Action Notify;
				private Action Bound;
				private static int Calls;

				private static Action Make()
				{
			-		static void Local() => Calls += 1;
			+		static void Local() => Calls += 2;
					static void Generic<T>() => Calls += typeof(T) == typeof(string) ? 10 : 100;
			-		static void Removed() => Calls += 1000;
			+		static void Different() => Calls += 2000;
			-		return (Action)Local + Generic<string> + Removed;
			+		return (Action)Local + Generic<string> + Different;
				}

				private static Action<Receiver> Bind()
				{
					static void Local( Receiver receiver ) => receiver.Notify();
					return Local;
				}

				void IHotloadTestFixture.Test()
				{
			-		Notify = Make();
			-		Bound = Bind().Method.CreateDelegate<Action>( this );
					Calls = 0;
					Assert.AreSame( this, Bound.Target );
					Bound();
			-		Assert.AreEqual( 1011, Calls );
			+		Assert.AreEqual( 12, Calls );
				}
			}
			""" );
	}

	/// <summary>
	/// Generated callbacks retain primitive locals on both sides of a compressed signature count boundary.
	/// </summary>
	[TestMethod]
	public async Task GeneratedCallbacksAtLocalCountBoundary()
	{
		using var fixture = await CreateFixtureAsync(
			"""
			using System;
			internal class Receiver : IHotloadTestFixture
			{
				internal Func<int>[] Readers;
			}
			""" );

		var readers = new Func<int>[3];
		for ( var i = 0; i < readers.Length; i++ )
		{
			var method = new DynamicMethod( "Read", typeof( int ), Type.EmptyTypes );
			var il = method.GetILGenerator();
			for ( var j = 0; j < 126 + i; j++ )
			{
				il.DeclareLocal( typeof( int ) );
			}

			il.Emit( OpCodes.Ldc_I4_7 );
			il.Emit( OpCodes.Ret );
			readers[i] = method.CreateDelegate<Func<int>>();
		}

		SetField( fixture.OldInstance, "Readers", readers );
		await fixture.HotloadAsync();

		foreach ( var read in (Func<int>[])GetField( fixture.Instance, "Readers" ) )
		{
			Assert.AreEqual( 7, read() );
		}
	}

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
	/// A removed action is skipped so another subscriber to the same event still runs.
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
			-		ActionSimple += SurvivingHandler;
			-
					Assert.AreEqual( 0, IntField );
			+
			+		ActionSimple();
			+		Assert.AreEqual( 1, IntField );
				}

				private void SurvivingHandler() => IntField++;
			}
			""",
			allowErrors: true );

		Assert.AreEqual( 1, result.Warnings.Count() );
	}

	/// <summary>
	/// An open delegate takes the receiver as an argument even though the removed lambda has no parameters.
	/// </summary>
	[TestMethod]
	public async Task RemovedOpenInstanceLambda()
	{
		await HotloadFixtureAsync(
			"""
			using System;

			public class Receiver : IHotloadTestFixture
			{
				private int Calls;
				private Action<Receiver> Callback;

			-	private Action CaptureReceiver() => () => Calls++;

				void IHotloadTestFixture.Test()
				{
			-		Callback = CaptureReceiver().Method.CreateDelegate<Action<Receiver>>();
					Callback( this );
					Assert.AreEqual( 1, Calls );
				}
			}
			""" );
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
	/// Exceptions raised by the surviving callback reach its caller after hotload.
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
					var exception = Assert.ThrowsException<NotImplementedException>( () => ActionSimple() );
					Assert.AreEqual( "This isn't implemented!", exception.Message );
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
				private int Calls;

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
				private Action Capture( BaseType target ) => () => Calls += target.Message.Length;
				private Action CaptureNull( BaseType target ) => () => Calls += target is null ? 10 : 0;

				void IHotloadTestFixture.Test()
				{
			-		Callback = new RemovedType().Populate();
			-		Callback += Capture( new RemovedType() );
			-		Callback += CaptureNull( null );
			-		Callback += SurvivingHandler;
			-
					Assert.IsNotNull( Callback );
			+		Callback();
			+		Assert.AreEqual( 11, Calls );
				}

				private void SurvivingHandler() => Calls++;
			}
			""" );
	}

	/// <summary>
	/// A missing handler that supplies an output value must still report the binding failure.
	/// </summary>
	[TestMethod]
	public async Task RemovedCallbackWithOutput()
	{
		await HotloadFixtureAsync(
			"""
			internal class Producer : IHotloadTestFixture
			{
				private delegate void Produce( out int value );
				private Produce Callback;

			-	private void Read( out int value ) => value = 42;

				void IHotloadTestFixture.Test()
				{
			-		Callback = Read;
			+		Assert.ThrowsException<System.NotImplementedException>( () => Callback( out _ ) );
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
