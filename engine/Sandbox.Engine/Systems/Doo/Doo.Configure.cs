namespace Sandbox;

public partial class Doo
{
	/// <summary>
	/// Provides configuration for a Doo run, such as setting initial argument values.
	/// </summary>
	public readonly struct Configure
	{
		private readonly RunContext _context;

		internal Configure( RunContext context )
		{
			_context = context;
		}

		/// <summary>
		/// Sets a local variable for this Doo run.
		/// </summary>
		public void SetArgument( string name, object value )
		{
			_context.LocalVariables[name] = value;
		}
	}
}
