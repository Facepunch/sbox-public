using Editor.NodeEditor;
using Editor.ShaderGraph;

public class ClassBlackboardParameterType : IBlackboardParameterType
{
	public virtual string Identifier => Type.FullName;
	public TypeDescription Type { get; }

	public ClassBlackboardParameterType( TypeDescription type )
	{
		Type = type;
	}

	public virtual IBlackboardParameter CreateParameter( IGraph graph, string name = "" )
	{
		var sg = graph as ShaderGraph;

		if ( string.IsNullOrWhiteSpace( name ) )
		{
			var baseName = $"{(sg.IsSubgraph ? "SubgraphInput" : "MaterialParameter")}";
			var id = 0;
			while ( sg.HasParameterWithName( $"{baseName}{id}" ) )
			{
				id++;
			}

			name = $"{baseName}{id}";
		}

		if ( EditorTypeLibrary.Create( Type.Name, Type.TargetType ) is BlackboardParameter parameter )
		{
			parameter.Name = name;
			parameter.Graph = graph;

			return parameter;
		}
		else
		{
			throw new Exception( $"Failed to create parameter instance of type \"{Type.Name}\"" );
		}
	}
}
