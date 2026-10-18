using Sandbox.Internal;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Sandbox;

/// <summary>
/// Calls one RPC method with arguments read straight from its message: a delegate to the method and typed reads,
/// no boxed argument array and no reflection invoke. Methods it can't handle (generic, more than six parameters,
/// by-ref parameters) return null from <see cref="Get"/> and go through <see cref="MethodBase.Invoke(object, object[])"/>.
/// </summary>
internal abstract class RpcInvoker
{
	public abstract void Invoke( object target, TypeLibrary library, ref ByteStream bs );

	public int ParameterCount { get; private set; }

	// Keyed by the method, so invokers for hotloaded-away code go with it
	static readonly ConditionalWeakTable<MethodInfo, RpcInvoker> Cache = new();
	static readonly RpcInvoker Unsupported = new UnsupportedInvoker();

	public static RpcInvoker Get( MethodInfo method )
	{
		var invoker = Cache.GetValue( method, Create );
		return invoker == Unsupported ? null : invoker;
	}

	static RpcInvoker Create( MethodInfo method )
	{
		try
		{
			return TryCreate( method ) ?? Unsupported;
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Couldn't make an RPC invoker for {method.DeclaringType?.Name}.{method.Name}, using reflection" );
			return Unsupported;
		}
	}

	static RpcInvoker TryCreate( MethodInfo method )
	{
		if ( method.IsGenericMethodDefinition || method.ContainsGenericParameters || method.DeclaringType is null )
			return null;

		var parameters = method.GetParameters();
		if ( parameters.Length > 6 || parameters.Any( p => p.ParameterType.IsByRef || p.ParameterType.IsPointer ) )
			return null;

		var types = parameters.Select( p => p.ParameterType ).ToList();

		Type invokerType;
		Type delegateType;

		if ( method.IsStatic )
		{
			invokerType = StaticInvokers[types.Count];
			delegateType = types.Count == 0 ? typeof( Action ) : System.Linq.Expressions.Expression.GetActionType( types.ToArray() );
		}
		else
		{
			if ( method.DeclaringType.IsValueType )
				return null;

			types.Insert( 0, method.DeclaringType );
			invokerType = InstanceInvokers[parameters.Length];
			delegateType = System.Linq.Expressions.Expression.GetActionType( types.ToArray() );
		}

		if ( types.Count > 0 )
			invokerType = invokerType.MakeGenericType( types.ToArray() );

		var callback = method.CreateDelegate( delegateType );
		var invoker = (RpcInvoker)Activator.CreateInstance( invokerType, callback );
		invoker.ParameterCount = parameters.Length;
		return invoker;
	}

	static readonly Type[] StaticInvokers =
	[
		typeof( StaticInvoker ), typeof( StaticInvoker<> ), typeof( StaticInvoker<,> ), typeof( StaticInvoker<,,> ),
		typeof( StaticInvoker<,,,> ), typeof( StaticInvoker<,,,,> ), typeof( StaticInvoker<,,,,,> )
	];

	static readonly Type[] InstanceInvokers =
	[
		typeof( InstanceInvoker<> ), typeof( InstanceInvoker<,> ), typeof( InstanceInvoker<,,> ), typeof( InstanceInvoker<,,,> ),
		typeof( InstanceInvoker<,,,,> ), typeof( InstanceInvoker<,,,,,> ), typeof( InstanceInvoker<,,,,,,> )
	];

	sealed class UnsupportedInvoker : RpcInvoker
	{
		public override void Invoke( object target, TypeLibrary library, ref ByteStream bs ) => throw new NotSupportedException();
	}

	sealed class StaticInvoker( Action f ) : RpcInvoker
	{
		public override void Invoke( object target, TypeLibrary l, ref ByteStream bs ) => f();
	}

	sealed class StaticInvoker<T1>( Action<T1> f ) : RpcInvoker
	{
		public override void Invoke( object target, TypeLibrary l, ref ByteStream bs ) => f( l.FromBytes<T1>( ref bs ) );
	}

	sealed class StaticInvoker<T1, T2>( Action<T1, T2> f ) : RpcInvoker
	{
		public override void Invoke( object target, TypeLibrary l, ref ByteStream bs )
		{
			var a1 = l.FromBytes<T1>( ref bs );
			var a2 = l.FromBytes<T2>( ref bs );
			f( a1, a2 );
		}
	}

	sealed class StaticInvoker<T1, T2, T3>( Action<T1, T2, T3> f ) : RpcInvoker
	{
		public override void Invoke( object target, TypeLibrary l, ref ByteStream bs )
		{
			var a1 = l.FromBytes<T1>( ref bs );
			var a2 = l.FromBytes<T2>( ref bs );
			var a3 = l.FromBytes<T3>( ref bs );
			f( a1, a2, a3 );
		}
	}

	sealed class StaticInvoker<T1, T2, T3, T4>( Action<T1, T2, T3, T4> f ) : RpcInvoker
	{
		public override void Invoke( object target, TypeLibrary l, ref ByteStream bs )
		{
			var a1 = l.FromBytes<T1>( ref bs );
			var a2 = l.FromBytes<T2>( ref bs );
			var a3 = l.FromBytes<T3>( ref bs );
			var a4 = l.FromBytes<T4>( ref bs );
			f( a1, a2, a3, a4 );
		}
	}

	sealed class StaticInvoker<T1, T2, T3, T4, T5>( Action<T1, T2, T3, T4, T5> f ) : RpcInvoker
	{
		public override void Invoke( object target, TypeLibrary l, ref ByteStream bs )
		{
			var a1 = l.FromBytes<T1>( ref bs );
			var a2 = l.FromBytes<T2>( ref bs );
			var a3 = l.FromBytes<T3>( ref bs );
			var a4 = l.FromBytes<T4>( ref bs );
			var a5 = l.FromBytes<T5>( ref bs );
			f( a1, a2, a3, a4, a5 );
		}
	}

	sealed class StaticInvoker<T1, T2, T3, T4, T5, T6>( Action<T1, T2, T3, T4, T5, T6> f ) : RpcInvoker
	{
		public override void Invoke( object target, TypeLibrary l, ref ByteStream bs )
		{
			var a1 = l.FromBytes<T1>( ref bs );
			var a2 = l.FromBytes<T2>( ref bs );
			var a3 = l.FromBytes<T3>( ref bs );
			var a4 = l.FromBytes<T4>( ref bs );
			var a5 = l.FromBytes<T5>( ref bs );
			var a6 = l.FromBytes<T6>( ref bs );
			f( a1, a2, a3, a4, a5, a6 );
		}
	}

	sealed class InstanceInvoker<TTarget>( Action<TTarget> f ) : RpcInvoker
	{
		public override void Invoke( object target, TypeLibrary l, ref ByteStream bs ) => f( (TTarget)target );
	}

	sealed class InstanceInvoker<TTarget, T1>( Action<TTarget, T1> f ) : RpcInvoker
	{
		public override void Invoke( object target, TypeLibrary l, ref ByteStream bs ) => f( (TTarget)target, l.FromBytes<T1>( ref bs ) );
	}

	sealed class InstanceInvoker<TTarget, T1, T2>( Action<TTarget, T1, T2> f ) : RpcInvoker
	{
		public override void Invoke( object target, TypeLibrary l, ref ByteStream bs )
		{
			var a1 = l.FromBytes<T1>( ref bs );
			var a2 = l.FromBytes<T2>( ref bs );
			f( (TTarget)target, a1, a2 );
		}
	}

	sealed class InstanceInvoker<TTarget, T1, T2, T3>( Action<TTarget, T1, T2, T3> f ) : RpcInvoker
	{
		public override void Invoke( object target, TypeLibrary l, ref ByteStream bs )
		{
			var a1 = l.FromBytes<T1>( ref bs );
			var a2 = l.FromBytes<T2>( ref bs );
			var a3 = l.FromBytes<T3>( ref bs );
			f( (TTarget)target, a1, a2, a3 );
		}
	}

	sealed class InstanceInvoker<TTarget, T1, T2, T3, T4>( Action<TTarget, T1, T2, T3, T4> f ) : RpcInvoker
	{
		public override void Invoke( object target, TypeLibrary l, ref ByteStream bs )
		{
			var a1 = l.FromBytes<T1>( ref bs );
			var a2 = l.FromBytes<T2>( ref bs );
			var a3 = l.FromBytes<T3>( ref bs );
			var a4 = l.FromBytes<T4>( ref bs );
			f( (TTarget)target, a1, a2, a3, a4 );
		}
	}

	sealed class InstanceInvoker<TTarget, T1, T2, T3, T4, T5>( Action<TTarget, T1, T2, T3, T4, T5> f ) : RpcInvoker
	{
		public override void Invoke( object target, TypeLibrary l, ref ByteStream bs )
		{
			var a1 = l.FromBytes<T1>( ref bs );
			var a2 = l.FromBytes<T2>( ref bs );
			var a3 = l.FromBytes<T3>( ref bs );
			var a4 = l.FromBytes<T4>( ref bs );
			var a5 = l.FromBytes<T5>( ref bs );
			f( (TTarget)target, a1, a2, a3, a4, a5 );
		}
	}

	sealed class InstanceInvoker<TTarget, T1, T2, T3, T4, T5, T6>( Action<TTarget, T1, T2, T3, T4, T5, T6> f ) : RpcInvoker
	{
		public override void Invoke( object target, TypeLibrary l, ref ByteStream bs )
		{
			var a1 = l.FromBytes<T1>( ref bs );
			var a2 = l.FromBytes<T2>( ref bs );
			var a3 = l.FromBytes<T3>( ref bs );
			var a4 = l.FromBytes<T4>( ref bs );
			var a5 = l.FromBytes<T5>( ref bs );
			var a6 = l.FromBytes<T6>( ref bs );
			f( (TTarget)target, a1, a2, a3, a4, a5, a6 );
		}
	}
}
