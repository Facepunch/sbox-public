using Sandbox.Internal;

namespace Sandbox;

/// <summary>
/// Describes a method. We use this class to wrap and return <see cref="MethodInfo">MethodInfo</see>'s that are safe to interact with.
///
/// Returned by <see cref="Internal.TypeLibrary"/> and <see cref="Sandbox.TypeDescription"/>.
/// </summary>
public sealed class MethodDescription : MemberDescription
{
	/// <summary>
	/// Returns true - because this is a method
	/// </summary>
	public override bool IsMethod => true;

	/// <inheritdoc cref="MethodBase.IsSpecialName"/>
	public bool IsSpecialName { get; private set; }

	/// <inheritdoc cref="MethodBase.IsVirtual"/>
	public bool IsVirtual { get; private set; }

	private MethodInfo methodInfo => MemberInfo as MethodInfo;

	/// <summary>
	/// Gets the return type of this method.
	/// </summary>
	public Type ReturnType => methodInfo?.ReturnType;

	/// <summary>
	/// Gets a list of parameters expected by this method
	/// </summary>
	public ref readonly ParameterInfo[] Parameters => ref parameters;

	ParameterInfo[] parameters;

	internal static MethodDescription Create( MethodInfo i, TypeDescription td, MemberDescription previous )
	{
		MethodDescription o = previous as MethodDescription;

		o ??= new MethodDescription( td.library );
		o.TypeDescription = td;
		o.InitMethod( i );

		return o;
	}

	internal MethodDescription( TypeLibrary tl ) : base( tl )
	{
	}

	private void InitMethod( MethodInfo x )
	{
		base.Init( x );

		IsStatic = x.IsStatic;
		IsPublic = x.IsPublic;
		IsFamily = x.IsFamily;
		IsVirtual = x.IsVirtual;
		IsSpecialName = x.IsSpecialName;

		parameters = methodInfo.GetParameters();
	}

	internal override void Dispose()
	{
		base.Dispose();

		parameters = null;
	}

	private string GetIdentityHashString()
	{
		// Needs to keep in sync with Sandbox.Generator.CodeGen.GetUniqueMethodIdentityString()

		// TODO: this will have conflicts for generic types with different numbers of type params

		var returnTypeName = ReturnType.Name.Split( "`" ).FirstOrDefault();
		var declaringTypeName = TypeDescription?.FullName.Split( "`" ).FirstOrDefault()?.Replace( "+", "." );
		var methodName = Name;
		var parameterTypes = string.Join( ",", methodInfo.GetParameters()
			.Select( p => p.ParameterType.Name.Split( "`" ).FirstOrDefault() ) );

		return $"{returnTypeName}.{declaringTypeName}.{methodName}.{parameterTypes}";
	}

	/// <inheritdoc />
	protected override int GetIdentityHash()
	{
		return GetIdentityHashString().FastHash();
	}

	/// <summary>
	/// Invokes this method.
	/// </summary>
	/// <param name="targetObject">Should be null if this is static, otherwise should be the object this is a member of.</param>
	/// <param name="parameters">An array of parameters to pass. Should be the same length as Parameters</param>
	public void Invoke( object targetObject, object[] parameters = null )
	{
		var methodParameters = methodInfo.GetParameters();
		var args = new object[methodParameters.Length];

		for ( var i = 0; i < methodParameters.Length; i++ )
		{
			args[i] = (parameters != null && i < parameters.Length)
				? parameters[i]
				: methodParameters[i].HasDefaultValue
					? methodParameters[i].DefaultValue
					: throw new ArgumentException(
						$"No value provided for parameter '{methodParameters[i].Name}' and it has no default value." );
		}

		methodInfo.Invoke( targetObject, args );
	}

	/// <summary>
	/// Invokes this method and returns a value.
	/// </summary>
	/// <param name="targetObject">Should be null if this is static, otherwise should be the object this is a member of.</param>
	/// <param name="parameters">An array of parameters to pass. Should be the same length as Parameters</param>
	public T InvokeWithReturn<T>( object targetObject, object[] parameters = null )
	{
		return (T)methodInfo.Invoke( targetObject, parameters );
	}

	/// <summary>
	/// Creates a delegate bound to this method.
	/// </summary>
	/// <typeparam name="T">Delegate type</typeparam>
	public T CreateDelegate<T>()
		where T : Delegate
	{
		return (T)Delegate.CreateDelegate( typeof( T ), methodInfo, true );
	}

	/// <summary>
	/// Creates a delegate bound to this method.
	/// </summary>
	/// <typeparam name="T">Delegate type</typeparam>
	/// <param name="target">Value for the first parameter / target object</param>
	public T CreateDelegate<T>( object target )
		where T : Delegate
	{
		return (T)Delegate.CreateDelegate( typeof( T ), target, methodInfo, true );
	}

	/// <summary>
	/// Creates a delegate bound to this method.
	/// </summary>
	/// <param name="delegateType">Delegate type to create</param>
	public Delegate CreateDelegate( Type delegateType )
	{
		return Delegate.CreateDelegate( delegateType, methodInfo, true );
	}

	/// <summary>
	/// Creates a delegate bound to this method.
	/// </summary>
	/// <param name="delegateType">Delegate type to create</param>
	/// <param name="target">Value for the first parameter / target object</param>
	public Delegate CreateDelegate( Type delegateType, object target )
	{
		return Delegate.CreateDelegate( delegateType, target, methodInfo, true );
	}
}
