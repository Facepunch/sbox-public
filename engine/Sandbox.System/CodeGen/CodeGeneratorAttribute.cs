namespace Sandbox;

/// <summary>
/// An attribute that can be added to a custom <see cref="Attribute"/> class for special code generation behavior.
/// They'll then be applied to methods and properties when they are decorated with <i>that</i> attribute.
/// </summary>
[AttributeUsage( AttributeTargets.Class, AllowMultiple = true )]
public class CodeGeneratorAttribute : Attribute
{
	/// <summary>
	/// Attributes with a higher priority will wrap the target first. The default priority is 0.
	/// </summary>
	public int Priority { get; init; } = 0;

	/// <summary>
	/// The name of the callback method. This can be a fully qualified static method callback or a simple callback to invoke
	/// on the target object if the method or property target is not static.
	/// </summary>
	public string CallbackName { get; init; }

	/// <summary>
	/// The type of code generation you want to do.
	/// You will need to specify whether it should apply to instance or static methods and properties using the <see cref="CodeGeneratorFlags.Instance"/>
	/// and <see cref="CodeGeneratorFlags.Static"/> flags.
	/// </summary>
	public CodeGeneratorFlags Type { get; init; }

	/// <summary>
	/// Perform code generation for a method or property.
	/// </summary>
	/// <param name="type">
	/// The type of code generation you want to do.
	/// You will need to specify whether it should apply to instance or static methods and properties using the <see cref="CodeGeneratorFlags.Instance"/>
	/// and <see cref="CodeGeneratorFlags.Static"/> flags.
	/// </param>
	/// <param name="callbackName">
	/// The name of the callback method. This can be a fully qualified static method callback or a simple callback to invoke
	/// on the target object if the method or property target is not static.
	/// </param>
	/// <param name="priority">
	/// Attributes with a higher priority will wrap the target first. The default priority is 0.
	/// </param>
	public CodeGeneratorAttribute( CodeGeneratorFlags type, string callbackName, int priority = 0 )
	{
		CallbackName = callbackName;
		Priority = priority;
		Type = type;
	}
}
