namespace Facepunch.InteropGen;

//
// Note: Only supporting passing string to native right now
//
[TypeName( "stringtoken" )]
public class ArgStringToken : Arg
{
	public override string ManagedType => "Sandbox.StringToken";
	public override string NativeType => "uint32";

	public override string FromInterop( Side side, string code = null )
	{
		code ??= Name;

		return side == Side.Native ? $"StringTokenFromHashCode( {code} )" : base.ToInterop( side, code );
	}
}
