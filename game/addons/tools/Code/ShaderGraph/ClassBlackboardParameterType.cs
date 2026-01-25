using Editor.ShaderGraph;

internal class ClassBlackboardParameterType : IBlackboardParameterType
{
	public virtual string Identifier => Type.FullName;
	public TypeDescription Type { get; }

	public ClassBlackboardParameterType( TypeDescription type )
	{
		Type = type;
	}

	public virtual IBlackboardParameter CreateParameter( ShaderGraph graph, string name = "" )
	{
		if ( EditorTypeLibrary.Create( Type.Name, Type.TargetType ) is BlackboardParameter parameter )
		{
			parameter.Name = name;

			return parameter;
		}
		else
		{
			throw new Exception( $"Failed to create parameter instance of type \"{Type.Name}\"" );
		}
	}
}
