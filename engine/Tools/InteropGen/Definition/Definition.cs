using System.Collections.Generic;
using System.Linq;

namespace Facepunch.InteropGen;

/// <summary>
/// The parsed, resolved contents of a .def file: its config plus the classes and structs to bind.
/// Built by <see cref="InteropPipeline"/> and consumed by the emitters.
/// </summary>
public class Definition
{
	/// <summary>
	/// The root folder
	/// </summary>
	public System.IO.DirectoryInfo Root { get; internal set; }
	public string Ident { get; internal set; } = "!!!NO IDENT!!!";
	public string NativeDll { get; internal set; } = "!!!NO NativeDll!!!";
	public string Filename { get; internal set; }
	public string ExceptionHandlerName { get; internal set; }
	public string SaveFileCpp { get; internal set; }
	public string SaveFileCppH { get; internal set; }
	public string SaveFileCs { get; internal set; }
	public string ManagedNamespace { get; internal set; } = "ManagedNamespace";
	public string PrecompiledHeader { get; set; }
	public string FullText { get; set; }
	public int Hash { get; internal set; }

	public List<Class> Classes = [];
	public List<Struct> Structs = [];

	/// <summary>
	/// Classes implemented natively, which we import into managed.
	/// </summary>
	public IEnumerable<Class> NativeClasses => Classes.Where( x => x.Native );

	/// <summary>
	/// Classes implemented in managed, which we export to native.
	/// </summary>
	public IEnumerable<Class> ManagedClasses => Classes.Where( x => !x.Native );

	public List<string> Includes = [];
	public List<string> Delegates = [];
	public List<Definition> IncludedDefinitions = [];
	public List<Definition> SkipAll = [];
}
