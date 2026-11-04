using Sandbox.Internal;

namespace Sandbox;

/// <summary>
/// Describes a method. We use this class to wrap and return <see cref="MethodInfo">MethodInfo</see>'s that are safe to interact with.
///
/// Returned by <see cref="Internal.TypeLibrary"/> and <see cref="Sandbox.TypeDescription"/>.
/// </summary>
public sealed partial class MethodDescription : MemberDescription
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
		IsStatic = x.IsStatic;
		IsPublic = x.IsPublic;
		IsFamily = x.IsFamily;
		IsVirtual = x.IsVirtual;
		IsSpecialName = x.IsSpecialName;
		OperatorKind = ClassifyOperator( x );
		parameters = x.GetParameters();

		base.Init( x );
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
		var parameterTypes = string.Join( ",", Parameters.Select( p => p.ParameterType.Name.Split( "`" ).FirstOrDefault() ) );

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
	public object Invoke( object targetObject, object[] parameters = null )
	{
		var args = new object[Parameters.Length];

		for ( var i = 0; i < Parameters.Length; i++ )
		{
			args[i] = (parameters != null && i < parameters.Length)
				? parameters[i]
				: Parameters[i].HasDefaultValue
					? Parameters[i].DefaultValue
					: throw new ArgumentException( $"No value provided for parameter '{Parameters[i].Name}' and it has no default value." );
		}

		return methodInfo.Invoke( targetObject, args );
	}

	/// <summary>
	/// Invokes this method and returns a value.
	/// </summary>
	/// <param name="targetObject">Should be null if this is static, otherwise should be the object this is a member of.</param>
	/// <param name="parameters">An array of parameters to pass. Should be the same length as Parameters</param>
	public T InvokeWithReturn<T>( object targetObject, object[] parameters = null )
	{
		return (T)Invoke( targetObject, parameters );
	}

	/// <summary>
	/// Creates a delegate bound to this method.
	/// </summary>
	/// <typeparam name="T">Delegate type</typeparam>
	public T CreateDelegate<T>()
		where T : Delegate
	{
		ValidateDelegateSignature( typeof( T ), methodInfo );
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
		ValidateDelegateSignature( typeof( T ), methodInfo );
		return (T)Delegate.CreateDelegate( typeof( T ), target, methodInfo, true );
	}

	/// <summary>
	/// Creates a delegate bound to this method.
	/// </summary>
	/// <param name="delegateType">Delegate type to create</param>
	public Delegate CreateDelegate( Type delegateType )
	{
		ValidateDelegateSignature( delegateType, methodInfo );
		return Delegate.CreateDelegate( delegateType, methodInfo, true );
	}

	/// <summary>
	/// Creates a delegate bound to this method.
	/// </summary>
	/// <param name="delegateType">Delegate type to create</param>
	/// <param name="target">Value for the first parameter / target object</param>
	public Delegate CreateDelegate( Type delegateType, object target )
	{
		ValidateDelegateSignature( delegateType, methodInfo );
		return Delegate.CreateDelegate( delegateType, target, methodInfo, true );
	}

	// The runtime only matches T& types, so ref kinds and lifetime annotations must agree exactly.
	private static void ValidateDelegateSignature( Type delegateType, MethodInfo method )
	{
		if ( delegateType is null || method is null || !delegateType.IsSubclassOf( typeof( Delegate ) ) )
			return;

		var invoke = delegateType.GetMethod( "Invoke" );
		if ( invoke is null )
			return;

		var delegateParams = invoke.GetParameters();
		var methodParams = method.GetParameters();
		var thisCount = method.IsStatic ? 0 : 1;
		var skip = methodParams.Length + thisCount - delegateParams.Length;

		if ( skip is not (0 or 1) )
			throw new ArgumentException( $"Delegate {delegateType} does not match the parameters of {method.Name}" );

		var usesRefs = IsRefLike( invoke.ReturnType ) || IsRefLike( method.ReturnType );

		if ( GetRefKind( invoke.ReturnParameter ) != GetRefKind( method.ReturnParameter ) )
			throw new ArgumentException( $"Delegate {delegateType} return ref kind does not match {method.Name}" );

		for ( var i = 0; i < delegateParams.Length; i++ )
		{
			var delegateParam = delegateParams[i];
			var index = i + skip - thisCount;
			usesRefs |= IsRefLike( delegateParam.ParameterType );

			if ( index < 0 )
			{
				var thisByRef = method.DeclaringType.IsValueType;
				usesRefs |= thisByRef;

				if ( GetRefKind( delegateParam ) != (thisByRef, false, false, false, false, false, false) )
					throw new ArgumentException( $"Delegate {delegateType} instance parameter ref kind does not match {method.Name}" );

				continue;
			}

			var methodParam = methodParams[index];
			usesRefs |= IsRefLike( methodParam.ParameterType );

			if ( GetRefKind( delegateParam ) != GetRefKind( methodParam ) )
				throw new ArgumentException( $"Delegate {delegateType} parameter '{delegateParam.Name}' ref kind does not match {method.Name}" );
		}

		if ( usesRefs && GetRefSafetyRules( invoke.Module ) != GetRefSafetyRules( method.Module ) )
			throw new ArgumentException( $"Delegate {delegateType} was compiled with different ref safety rules than {method.Name}" );
	}

	private static bool IsRefLike( Type type ) => type.IsByRef || type.IsByRefLike;

	private static (bool ByRef, bool In, bool Out, bool ReadOnly, bool RequiresLocation, bool Scoped, bool Unscoped) GetRefKind( ParameterInfo parameter )
	{
		var byRef = parameter.ParameterType.IsByRef;
		bool readOnly = false, requiresLocation = false, scoped = false, unscoped = false;

		foreach ( var attribute in parameter.CustomAttributes )
		{
			switch ( attribute.AttributeType.FullName )
			{
				case "System.Runtime.CompilerServices.IsReadOnlyAttribute": readOnly = true; break;
				case "System.Runtime.CompilerServices.RequiresLocationAttribute": requiresLocation = true; break;
				case "System.Runtime.CompilerServices.ScopedRefAttribute": scoped = true; break;
				case "System.Diagnostics.CodeAnalysis.UnscopedRefAttribute": unscoped = true; break;
			}
		}

		return (byRef, byRef && parameter.IsIn, byRef && parameter.IsOut, readOnly, requiresLocation, scoped, unscoped);
	}

	private static int GetRefSafetyRules( Module module )
	{
		foreach ( var attribute in module.CustomAttributes )
		{
			if ( attribute.AttributeType.FullName == "System.Runtime.CompilerServices.RefSafetyRulesAttribute"
				&& attribute.ConstructorArguments.Count == 1
				&& attribute.ConstructorArguments[0].Value is int version )
			{
				return version;
			}
		}

		return 0;
	}
}
