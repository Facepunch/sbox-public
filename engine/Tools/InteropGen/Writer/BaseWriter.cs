using System.Collections.Generic;
using System.IO;

namespace Facepunch.InteropGen;

/// <summary>
/// Base for the three emitters. Adds the Definition being generated, the <see cref="SkipPolicy"/>,
/// sub-file buffering, and saving the result via <see cref="FileWriter"/>.
/// </summary>
internal class BaseWriter : CodeWriter
{
	public string TargetName;
	protected Definition definitions;
	protected SkipPolicy Skip;

	public BaseWriter( Definition definitions, string targetName )
	{
		TargetName = targetName;
		this.definitions = definitions;
		Skip = new SkipPolicy( definitions );
	}

	public virtual void Generate()
	{

	}

	/// <summary>
	/// The native export table layout, shared so the managed bootstrap and the native initializer
	/// (and the array size) always agree on slot order and indices.
	/// </summary>
	protected IEnumerable<NativeSlot> NativeExportSlots()
	{
		return NativeExportTable.Slots( definitions, Skip );
	}

	public virtual void SaveToFile( string file )
	{
		FileWriter.Save( definitions.Root.FullName, file, Builder.ToString(), SubFiles );
	}

	private System.Text.StringBuilder previousBuilder;
	public Dictionary<string, string> SubFiles = [];

	/// <summary>
	/// Redirect output into a separate buffer until <see cref="EndSubFile"/>, which stores it as a
	/// named sub-file written alongside the main file.
	/// </summary>
	public void StartSubFile()
	{
		previousBuilder = Builder;
		Builder = new System.Text.StringBuilder();
	}

	public string EndSubFile( string moduleName )
	{
		string ext = Path.GetExtension( TargetName );
		string fn = Path.GetFileNameWithoutExtension( TargetName );
		string outName = $"{fn}.{moduleName}{ext}";

		SubFiles[outName] = Builder.ToString();

		Builder = previousBuilder;

		return outName;
	}
}
