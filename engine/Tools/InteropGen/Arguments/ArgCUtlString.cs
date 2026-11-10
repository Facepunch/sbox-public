namespace Facepunch.InteropGen;

[TypeName( "CUtlString" )]
public class ArgCUtlString : Arg
{
	public override string ManagedType => "string";
	public override string ManagedDelegateType => "IntPtr";
	public override string NativeType => "CUtlString";
	public override string NativeDelegateType => "const char *";
	public override bool WrapsManagedCall => true;

	public override string ReturnWrapCall( string call, Side side )
	{
		return side == Side.Native ? $"return (const char*) SafeReturnString( (const char *) {call} );" : base.ReturnWrapCall( call, side );
	}

	public override string ToInterop( Side side, string code = null )
	{
		code ??= Name;

		return side == Side.Managed ? $"_str_{code}.Pointer" : $"{code}.String()";
	}

	public override string FromInterop( Side side, string code = null )
	{
		code ??= Name;

		return side == Side.Managed ? $"{StringTools}.GetString( {code} )" : code;
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
