namespace Facepunch.InteropGen;

/// <summary>
/// Qt defines some things as regular classes that contain nothing but a smart pointer to the
/// real data. Marking a class as [SharedDataPointer] will:
///
/// 1. Always return as a new Class()
/// 2. Pass to native as a pointer to that class
/// 3. Pass to the native function as a instance to that class (*) ptr
/// </summary>
public class ArgSharedDataPointer : ArgDefinedClass
{

	public ArgSharedDataPointer( Class c, string name, string[] flags ) : base( c, name, flags )
	{

	}

	public override string FromInterop( Side side, string code = null )
	{
		return side == Side.Native ? "*" + (code ?? Name) : base.FromInterop( side, code );
	}

	public override string ToInterop( Side side, string code = null )
	{
		return side == Side.Managed ? $"{code ?? Name}.GetPointerAssertIfNull()" : base.ToInterop( side, code );
	}

	public override string ReturnWrapCall( string functionCall, Side side )
	{
		return side == Side.Native
			? $"return new {Class.NativeNameWithNamespace}( {functionCall} );"
			: $"return new {Class.ManagedNameWithNamespace}( {functionCall} );";
	}

}
