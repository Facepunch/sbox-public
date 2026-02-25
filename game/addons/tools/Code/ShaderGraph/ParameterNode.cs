using System.Text.Json.Serialization;

namespace Editor.ShaderGraph;

public interface IParameterNode
{
	Guid ParameterIdentifier { get; set; }
	string Name { get; }
}

public abstract class ParameterNode<T, Y> : ShaderNode, IParameterNode where Y : BlackboardParameter
{
	[Hide]
	public override string Title => string.IsNullOrWhiteSpace( Name ) ?
		$"{DisplayInfo.For( this ).Name}" :
		$"{DisplayInfo.For( this ).Name} {Name}";

	[Hide]
	public Guid ParameterIdentifier { get; set; }

	[Hide]
	public string Name => GetParameter().Name;

	[Hide, JsonIgnore]
	public T Value
	{
		get => (T)GetParameter().GetValue();
		set
		{
			if ( Graph is ShaderGraph graph )
			{
				graph.UpdateParameterValue( ParameterIdentifier, value );

				Update();
				IsDirty = true;
			}
		}
	}

	protected Y GetParameter()
	{
		if ( Graph is ShaderGraph graph )
		{
			return graph.FindParameter<Y>( ParameterIdentifier );
		}

		return null;
	}

	protected NodeResult Component( string component, float value, GraphCompiler compiler )
	{
		if ( compiler.IsPreview )
			return compiler.ResultValue( value );

		var result = compiler.Result( new NodeInput { Identifier = Identifier, Output = nameof( Result ) } );
		return new( NodeResultType.Float, $"{result}.{component}", true );
	}
}
