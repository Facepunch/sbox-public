namespace Sandbox;

public partial class Doo
{
	/// <summary>
	/// Marks a static method as callable from within a Doo script.
	/// </summary>
	[AttributeUsage( AttributeTargets.Method )]
	public sealed class MethodAttribute : System.Attribute
	{
		/// <summary>
		/// The fully qualified method path (e.g. "Log.Info").
		/// </summary>
		public string Path { get; set; }

		/// <summary>
		/// The category portion of the path, derived from the text before the first dot.
		/// </summary>
		public string CategoryName { get; init; }

		/// <summary>
		/// Creates a new <see cref="MethodAttribute"/> with the given method path.
		/// </summary>
		public MethodAttribute( string path )
		{
			Path = path;

			var paths = Path.Split( '.' );
			CategoryName = paths.FirstOrDefault();
		}
	}

	/// <summary>
	/// Specify a hint on a Doo explaining that we're going to be passing in an expected argument when calling it.
	/// </summary>
	[AttributeUsage( AttributeTargets.Property, AllowMultiple = true )]
	public class ArgumentHintAttribute : System.Attribute
	{
		/// <summary>
		/// The argument name shown in the editor.
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// A help string describing what this argument is for.
		/// </summary>
		public string Help { get; set; }

		/// <summary>
		/// The expected type of this argument.
		/// </summary>
		public Type Hint { get; set; }
	}

	/// <summary>
	/// Specify a hint on a Doo explaining that we're going to be passing in an expected argument when calling it.
	/// </summary>
	public sealed class ArgumentHintAttribute<T> : ArgumentHintAttribute
	{
		/// <summary>
		/// Creates a new <see cref="ArgumentHintAttribute{T}"/> with the given name and type hint.
		/// </summary>
		public ArgumentHintAttribute( string name )
		{
			Name = name;
			Hint = typeof( T );
		}
	}
}
