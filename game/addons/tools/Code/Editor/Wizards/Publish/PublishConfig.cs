namespace Editor.Wizards;

public class PublishConfig
{
	/// <summary>
	/// Assemblies can reference asset packages. This is a list
	/// of packages that the compiled code references.
	/// </summary>
	public HashSet<string> CodePackages = new();

	/// <summary>
	/// If the compile process created any assemblies
	/// </summary>
	public Dictionary<string, object> AssemblyFiles { get; set; }
	public CompilerOutput[] CompilerOutput { get; set; }
	public ProjectPublisher Publisher { get; internal set; }

	/// <summary>
	/// The Steam Workshop item, thumbnail, visibility and tags picked on the first page. Null if it doesn't go to the workshop.
	/// </summary>
	internal PublishWizard.WorkshopChoices Workshop { get; set; }
}
