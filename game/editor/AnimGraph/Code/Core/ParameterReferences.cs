namespace Editor.AnimGraph;

internal interface IParameterReference
{
	IParameter ResolveParameter();
}

internal static class ParameterReferences
{
	internal static IParameter Resolve( IEnumerable<IParameter> parameters, string identifier, string name, AnimParameterType type )
	{
		if ( !string.IsNullOrWhiteSpace( identifier )
			&& parameters.FirstOrDefault( parameter => parameter.Identifier == identifier ) is { } identified )
			return identified;

		return parameters.FirstOrDefault( parameter => parameter.Type == type
			&& string.Equals( parameter.Name, name, StringComparison.OrdinalIgnoreCase ) );
	}

	internal static ILookup<IParameter, Node> Collect( Data graph )
		=> graph.AllNodes
			.Select( node => (Node: node, Parameter: (node as IParameterReference)?.ResolveParameter()) )
			.Where( reference => reference.Parameter is not null )
			.ToLookup( reference => reference.Parameter, reference => reference.Node );
}
