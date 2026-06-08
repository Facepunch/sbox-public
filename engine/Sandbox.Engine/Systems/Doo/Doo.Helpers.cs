using Sandbox.Engine;

namespace Sandbox;

public partial class Doo
{
	/// <summary>
	/// Utility methods for the Doo scripting system.
	/// </summary>
	public static partial class Helpers
	{
		/// <summary>
		/// Finds a method by its fully qualified path (e.g. "TypeName.MethodName").
		/// Returns null if the type or method cannot be found.
		/// </summary>
		public static MethodDescription FindMethod( string methodPath )
		{
			var lastDot = methodPath?.LastIndexOf( '.' ) ?? -1;

			// not found
			if ( lastDot < 0 )
				return default;

			var typeName = methodPath.Substring( 0, lastDot );
			var methodName = methodPath.Substring( lastDot + 1 );

			var t = GlobalContext.Current.TypeLibrary.GetType( typeName );
			return t?.Methods.FirstOrDefault( x => x.Name == methodName );
		}

		/// <summary>
		/// Finds a property by its fully qualified path (e.g. "TypeName.PropertyName").
		/// Returns null if the type or property cannot be found.
		/// </summary>
		public static PropertyDescription FindProperty( string propertyPath )
		{
			var lastDot = propertyPath?.LastIndexOf( '.' ) ?? -1;

			if ( lastDot < 0 )
				return default;

			var typeName = propertyPath.Substring( 0, lastDot );
			var propertyName = propertyPath.Substring( lastDot + 1 );

			var t = GlobalContext.Current.TypeLibrary.GetType( typeName );
			return t?.Members.OfType<PropertyDescription>().FirstOrDefault( x => x.Name == propertyName );
		}
	}
}
