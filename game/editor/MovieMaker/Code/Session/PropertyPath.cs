using Sandbox.MovieMaker.Properties;

namespace Editor.MovieMaker;

#nullable enable

/// <summary>
/// Splits dotted property paths like <c>"Animation.Parameters.aim_eyes"</c> one name at a time.
/// </summary>
internal static class PropertyPath
{
	/// <summary>
	/// Removes the first property name from <paramref name="propertyPath"/> and returns it. Anim graph parameter
	/// names can include periods, so inside a parameters or triggers track the whole remaining path is one name.
	/// </summary>
	public static string PopName( Type parentType, ref string propertyPath )
	{
		if ( IsAnimGraphParameters( parentType ) || propertyPath.IndexOf( '.' ) is not (var index and > -1) )
		{
			var name = propertyPath;
			propertyPath = string.Empty;
			return name;
		}

		var first = propertyPath[..index];
		propertyPath = propertyPath[(index + 1)..];
		return first;
	}

#pragma warning disable CS0618
	private static bool IsAnimGraphParameters( Type type ) =>
		type == typeof( Sandbox.Animation.GraphParameters ) || type == typeof( GraphTriggerAccessor )
		|| type == typeof( SkinnedModelRenderer.ParameterAccessor );
#pragma warning restore CS0618
}
