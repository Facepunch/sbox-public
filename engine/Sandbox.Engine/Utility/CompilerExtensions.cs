namespace Sandbox;

public static class CompilerExtensions
{
	/// <summary>
	/// Add a reference to the "<c>base</c>" package.
	/// </summary>
	public static void AddBaseReference( this Compiler compiler )
	{
		compiler.AddReference( "package.base" );
	}

	/// <summary>
	/// Add a reference to the "<c>toolbase</c>" package.
	/// </summary>
	public static void AddToolBaseReference( this Compiler compiler )
	{
		compiler.AddReference( "package.toolbase" );
	}

	/// <summary>
	/// Add a reference to the given compiler.
	/// </summary>
	public static void AddReference( this Compiler compiler, Compiler reference )
	{
		compiler.AddReference( reference.AssemblyName );
	}

	/// <summary>
	/// Add a reference to the given package's assembly.
	/// </summary>
	public static void AddReference( this Compiler compiler, Package reference )
	{
		compiler.AddReference( reference.AssemblyName );
	}

	/// <summary>
	/// Add a reference to the given package's editor assembly.
	/// </summary>
	public static void AddEditorReference( this Compiler compiler, Package reference )
	{
		compiler.AddReference( reference.EditorAssemblyName );
	}
}
