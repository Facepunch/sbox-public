using System.Linq;

namespace Facepunch.InteropGen;

/// <summary>
/// The default wrapper applied to every non-array arg. Applies the per-argument flags (out/ref/asref/
/// cref/cast/CastTo[...]/boxed) on top of the wrapped arg's marshalling.
/// </summary>
public class ArgFlagsWrapper : ArgWrapper
{
	public ArgFlagsWrapper( Arg val )
	{
		Base = val;
		Name = val.Name;
		Flags = val.Flags;
	}

	/// <summary>
	/// out/ref/asref params are passed to native as a pointer to the value.
	/// </summary>
	private string AsNativePointer( string type )
	{
		return HasFlag( "out" ) || HasFlag( "ref" ) || HasFlag( "asref" ) ? type + "*" : type;
	}

	/// <summary>
	/// out/ref params get the matching C# keyword in front of the type.
	/// </summary>
	private string WithManagedRefKeyword( string type )
	{
		if ( HasFlag( "out" ) )
		{
			type = "out " + type;
		}

		if ( HasFlag( "ref" ) )
		{
			type = "ref " + type;
		}

		return type;
	}

	/// <summary>
	/// The target type of a "CastTo[X]" flag, or null if there isn't one.
	/// </summary>
	private string CastToType()
	{
		string cast = Flags?.FirstOrDefault( x => x.StartsWith( "CastTo[" ) && x.EndsWith( "]" ) );
		return cast?[7..^1];
	}

	public override string NativeType => AsNativePointer( Base.NativeType );
	public override string NativeDelegateType => AsNativePointer( Base.NativeDelegateType );

	public override string ManagedType => WithManagedRefKeyword( Base.ManagedType );

	public override string ManagedDelegateType
	{
		get
		{
			string t = WithManagedRefKeyword( Base.ManagedDelegateType );
			return HasFlag( "asref" ) ? "IntPtr" : t;
		}
	}

	public override string DelegateType( Side side, Dir dir )
	{
		if ( side == Side.Native )
		{
			return AsNativePointer( Base.DelegateType( Side.Native, dir ) );
		}

		string t = WithManagedRefKeyword( Base.DelegateType( Side.Managed, dir ) );
		return HasFlag( "asref" ) ? "IntPtr" : t;
	}

	public override string FromInterop( Side side, string code = null )
	{
		if ( Flags == null )
		{
			return Base.FromInterop( side, code );
		}

		string name = code ?? Name;

		if ( side == Side.Managed && HasFlag( "asref" ) )
		{
			return $"ref Unsafe.AsRef<{Base.ManagedType}>( (void*) {name} )";
		}

		if ( side == Side.Native )
		{
			if ( HasFlag( "cref" ) )
			{
				return $"*{name}";
			}

			string cast = CastToType();
			if ( cast != null )
			{
				return $"/*CastTo*/ ({cast}) {name}";
			}
		}

		return HasFlag( "ref" ) ? name : Base.FromInterop( side, code );
	}

	public override string ToInterop( Side side, string code = null )
	{
		if ( Flags == null )
		{
			return Base.ToInterop( side, code );
		}

		string name = code ?? Name;

		if ( side == Side.Managed && HasFlag( "out" ) && Base is ArgString )
		{
			return $"out _outptr_{name}";
		}

		if ( side == Side.Managed && HasFlag( "out" ) )
		{
			return $"out {name}";
		}

		if ( side == Side.Managed && HasFlag( "ref" ) )
		{
			return $"ref {name}";
		}

		if ( side == Side.Native && HasFlag( "cref" ) )
		{
			return $"&{name}";
		}

		//
		// Returning a class, we want to cast it from one thing to this type
		//
		if ( side == Side.Native && HasFlag( "cast" ) )
		{
			return $"({NativeType}) {name}";
		}

		return Base.ToInterop( side, code );
	}

	public override string ReturnWrapCall( string functionCall, Side side )
	{
		if ( side == Side.Native )
		{
			string cast = CastToType();
			if ( cast != null )
			{
				functionCall = $"/*CastTo*/ {cast} {functionCall}";
			}
		}

		return Base.ReturnWrapCall( functionCall, side );
	}

	public override string WrapFunctionCall( string functionCall, Side side )
	{
		return Base.WrapFunctionCall( functionCall, side );
	}

	public override string DefaultValue => Base.DefaultValue;
}
