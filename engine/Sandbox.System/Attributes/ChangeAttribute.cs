namespace Sandbox
{
	/// <summary>
	/// This will invoke a method when the property changes. It can be used with any property but is especially useful
	/// when combined with [Sync] or [ConVar].
	/// <br/><br/>
	/// If no name is provided, we will try to call On[PropertyName]Changed. The callback should have 2 arguments - oldValue and newValue, both of the same type as the property itself.
	/// </summary>
	[AttributeUsage( AttributeTargets.Property )]
	[CodeGenerator( CodeGeneratorFlags.Instance | CodeGeneratorFlags.Static | CodeGeneratorFlags.WrapPropertySet, "Sandbox.ChangeCallback.OnPropertySet", 10 )]
	public class ChangeAttribute : Attribute
	{
		/// <summary>
		/// Name of the method to call on change. If no name is provided, we will try to call On[PropertyName]Changed.
		/// </summary>
		public string Name { get; set; }

		public ChangeAttribute( string name = null )
		{
			Name = name;
		}
	}
}
