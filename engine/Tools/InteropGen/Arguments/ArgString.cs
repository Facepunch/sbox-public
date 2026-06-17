namespace Facepunch.InteropGen;

[TypeName( "string" )]
public class ArgString : Arg
{
	public override string ManagedType => "string";
	public override string ManagedDelegateType => "IntPtr";
	public override string NativeType => "const char*";
	public override bool WrapsManagedCall => true;

	public override string ReturnWrapCall( string call, Side side )
	{
		if ( side != Side.Native )
		{
			return base.ReturnWrapCall( call, side );
		}

		// A "stable" return is guaranteed by the def to outlive the call, so we can skip the
		// defensive copy into the thread local that SafeReturnString does.
		if ( HasFlag( "stable" ) )
		{
			return $"return (const char*) {call};";
		}

		return $"return (const char*) SafeReturnString( (const char *) {call} );";
	}

	public override string ToInterop( Side side, string code = null )
	{
		code ??= Name;

		return side == Side.Managed
			? IsReturn ? $"{StringTools}.GetTemporaryStringPointerForNative( {code} )" : $"_str_{code}.Pointer"
			: base.ToInterop( side, code );
	}

	public override string FromInterop( Side side, string code = null )
	{
		code ??= Name;

		return side == Side.Managed ? $"{StringTools}.GetString( {code} )" : base.ToInterop( side, code );
	}

	public override string WrapFunctionCall( string functionCall, Side side )
	{
		if ( side == Side.Managed && HasFlag( "out" ) )
		{
			return $"IntPtr _outptr_{Name} = default;\n\n" +
				$"try\n" +
				$"{{\n" +
				$"	{functionCall}\n" +
				$"}}\n" +
				$"finally\n" +
				$"{{\n" +
				$"	{Name} = {StringTools}.GetString( _outptr_{Name} );\n" +
				$"}}\n";
		}
		else if ( side == Side.Managed )
		{
			return $"var _str_{Name} = new {StringTools}.InteropString( {Name}, stackalloc byte[256] ); try {{ {functionCall} }} finally {{ _str_{Name}.Free(); }} ";
		}

		return base.WrapFunctionCall( functionCall, side );
	}
}
