namespace Editor.ShaderGraph;

public interface IConstantNode
{
	string Identifier { get; set; }
}

public abstract class ConstantNode<T> : ShaderNode, IConstantNode
{
	public T Value { get; set; }

	protected NodeResult Component( string component, float value, GraphCompiler compiler )
	{
		if ( compiler.IsPreview )
		{
			return compiler.ResultValue( value );
		}

		var result = compiler.Result( new NodeInput { Identifier = Identifier, Output = nameof( Result ) } );

		return new( NodeResultType.Float, $"{result}.{component}", true );
	}
}
