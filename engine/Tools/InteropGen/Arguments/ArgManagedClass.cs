namespace Facepunch.InteropGen;

/// <summary>
/// A managed class passed across the boundary as a uint object id (resolved via InteropSystem).
/// </summary>
public class ArgManagedClass : Arg
{
	public Class Class { get; set; }


	public ArgManagedClass( Class c, string name, string[] flags )
	{
		Class = c;
		Name = name;
		Flags = flags;
	}

	public override string ManagedType => "global::" + Class.ManagedNameWithNamespace;
	public override string ManagedDelegateType => "uint";
	public override string NativeType => "uint";
	public override string NativeDelegateType => "uint";

	public override string FromInterop( Side side, string code = null )
	{
		code ??= Name;

		//
		// Passing a managed class to native. Incoming is a pointer - so we crea
		// a new instance of the class. In reality we should only be calling this
		// once per class to save a new instance.
		//
		if ( side == Side.Native )
		{
			return code;
		}

		//
		// Getting a managed class back from interop. We use Facepunch.Interop.NativePointer's built in
		// function to try to convert it from a GHandle to a Facepunch.Interop.NativePointer, then to the
		// target class.
		//
		return $"Sandbox.InteropSystem.Get<{Class.ManagedNameWithNamespace}>( {code} )";
	}

	public override string ToInterop( Side side, string code = null )
	{
		code ??= Name;

		//
		// Passing a managed class to native - we just use the .NativePointer property
		// Which should be using the NativePointer class to create a GCHandle.
		//
		return side == Side.Managed ? $" Sandbox.InteropSystem.GetAddress( {code}, true )" : $"{code}";
	}

}
