using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Threading;

namespace Sandbox.Upgraders;

partial class DelegateUpgrader
{
	private static int _reportedLayoutMismatch;

	private bool RejectLayout( DynamicMethod method, string reason )
	{
		if ( Interlocked.Exchange( ref _reportedLayoutMismatch, 1 ) == 0 )
		{
			Log( HotloadEntryType.Warning, $"Unrecognized CoreCLR DynamicMethod layout on .NET {Environment.Version}: {reason}", method );
		}

		return false;
	}

	/// <summary>
	/// A generated method has no replacement body. Reuse it only if its metadata
	/// still describes the same code after hotload.
	/// </summary>
	private bool CanKeepGeneratedMethod( DynamicMethod method )
	{
		// DynamicMethod.GetMethodBody() is unsupported. .NET 10 CoreCLR keeps its operands in
		// the resolver's token table. If the layout changes, fall back to an error delegate.
		// Raw DynamicILInfo has its own token scope. Only ILGenerator bodies are supported.
		if ( ReadField( method, "_dynamicILInfo" ) is DynamicILInfo )
			return false;

		var ownerField = typeof( DynamicMethod ).GetField( "_typeOwner", BindingFlags.Instance | BindingFlags.NonPublic );
		if ( ownerField is null || !typeof( Type ).IsAssignableFrom( ownerField.FieldType ) )
			return RejectLayout( method, "expected a Type field named _typeOwner." );

		var resolver = ReadField( method, "_resolver" );
		if ( resolver is null )
			return RejectLayout( method, "missing _resolver." );

		var scope = ReadField( resolver, "m_scope" );
		var generator = ReadField( method, "_ilGenerator" );
		if ( scope == null || scope != ReadField( generator, "m_scope" ) )
			return RejectLayout( method, "expected _resolver and _ilGenerator to share m_scope." );

		if ( ReadField( scope, "m_tokens" ) is not List<object> tokens || tokens is not [null, byte[], ..] )
			return RejectLayout( method, "unexpected m_tokens table or signature slot." );

		if ( ReadField( resolver, "m_localSignature" ) is not byte[] locals || locals is not [0x07, _, ..] )
			return RejectLayout( method, "unexpected m_localSignature header." );

		var localTypes = locals[1] == 0 ? null : new HashSet<Type>();

		bool KeepType( Type type )
		{
			if ( type == null )
				return true;

			while ( type.HasElementType )
			{
				localTypes?.Add( type );
				type = type.GetElementType();
			}

			localTypes?.Add( type );
			return !type.IsFunctionPointer && !IsSwappedType( type );
		}

		bool KeepMember( MemberInfo member )
		{
			if ( member == null || TryGetTypeReplacement( member.Module.Assembly, out _ ) || !KeepType( member.DeclaringType ) )
				return false;

			return member switch
			{
				Type type => KeepType( type ),
				FieldInfo field => KeepType( field.FieldType ),
				MethodBase call => (call is not MethodInfo info || KeepType( info.ReturnType ))
					&& (!call.IsGenericMethod || call.GetGenericArguments().All( KeepType ))
					&& call.GetParameters().All( parameter => KeepType( parameter.ParameterType ) ),
				_ => false
			};
		}

		if ( !KeepMember( method ) || !KeepType( (Type)ownerField.GetValue( method ) ) )
			return false;

		// Slot zero is reserved. Slot one describes the method signature checked above.
		for ( var i = 2; i < tokens.Count; i++ )
		{
			var token = tokens[i];
			if ( token is string )
				continue;

			var context = ReadField( token, "m_context" ) as RuntimeTypeHandle?;
			if ( context.HasValue && !KeepType( Type.GetTypeFromHandle( context.Value ) ) )
				return false;

			if ( !KeepMember( ResolveGeneratedOperand( token, context ) ) )
				return false;
		}

		return localTypes == null || HasKnownLocals( locals, localTypes );
	}

	private static MemberInfo ResolveGeneratedOperand( object operand, RuntimeTypeHandle? context )
	{
		if ( context.HasValue )
		{
			if ( ReadField( operand, "m_methodHandle" ) is RuntimeMethodHandle method )
				return MethodBase.GetMethodFromHandle( method, context.Value );

			if ( ReadField( operand, "m_fieldHandle" ) is RuntimeFieldHandle field )
				return FieldInfo.GetFieldFromHandle( field, context.Value );
		}

		return operand switch
		{
			RuntimeTypeHandle type => Type.GetTypeFromHandle( type ),
			RuntimeMethodHandle method => MethodBase.GetMethodFromHandle( method ),
			RuntimeFieldHandle field => FieldInfo.GetFieldFromHandle( field ),
			_ => null
		};
	}

	private static bool HasKnownLocals( ReadOnlySpan<byte> signature, HashSet<Type> types )
	{
		// Local signatures can embed type handles outside the operand table. Match the
		// runtime's encoding of known types without treating bytes as native pointers.
		var encodings = types.Where( type => type != typeof( void ) ).Select( type =>
		{
			var local = SignatureHelper.GetLocalVarSigHelper();
			local.AddArgument( type );
			return local.GetSignature()[2..]; // LOCAL_SIG followed by a count of one.
		} ).ToArray();

		// The emitter writes a compressed local count and terminates the signature with END.
		var countSize = signature[1] < 0x80 ? 1 : signature[1] < 0xc0 ? 2 : 4;
		if ( signature.Length <= 1 + countSize )
			return false;

		signature = signature[(1 + countSize)..];
		while ( !signature.IsEmpty && signature[0] != 0 )
		{
			var code = (SignatureTypeCode)signature[0];
			if ( code is >= SignatureTypeCode.Boolean and <= SignatureTypeCode.String
				or SignatureTypeCode.IntPtr or SignatureTypeCode.UIntPtr or SignatureTypeCode.Object or SignatureTypeCode.Pinned
				or SignatureTypeCode.SZArray or SignatureTypeCode.ByReference or SignatureTypeCode.Pointer )
			{
				signature = signature[1..];
				continue;
			}

			int matched = 0;
			foreach ( var encoding in encodings )
			{
				if ( signature.StartsWith( encoding ) )
				{
					matched = encoding.Length;
					break;
				}
			}

			if ( matched == 0 )
				return false;

			signature = signature[matched..];
		}

		// CoreCLR can leave extra zero padding after END when the count encoding grows.
		return !signature.IsEmpty && !signature.ContainsAnyExcept( (byte)0 );
	}

	private static object ReadField( object instance, string name )
	{
		return instance?.GetType().GetField( name, BindingFlags.Instance | BindingFlags.NonPublic )?.GetValue( instance );
	}
}
