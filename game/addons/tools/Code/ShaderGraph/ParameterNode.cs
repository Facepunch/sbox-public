using System.Text.Json.Serialization;

namespace Editor.ShaderGraph;

public interface IParameterNode
{
	string Name { get; set; }
	bool IsAttribute { get; set; }

	object GetValue();
	void SetValue( object val );
	Vector4 GetRangeMin();
	Vector4 GetRangeMax();
}

public interface IParameterUINode<T> : IParameterNode where T : IParameterUI
{
	T UI { get; set; }
}

public interface ITextureParameterNode
{
	string Image { get; set; }
	TextureInput UI { get; set; }
}

public abstract class ParameterNode<T,Y> : ShaderNode, IParameterNode, IParameterUINode<Y>, IErroringNode where Y : IParameterUI
{
	[Hide]
	protected bool IsSubgraph => (Graph is ShaderGraph shaderGraph && shaderGraph.IsSubgraph);

	[Hide]
	public override string Title => string.IsNullOrWhiteSpace( Name ) ?
		$"{DisplayInfo.For( this ).Name}" :
		$"{DisplayInfo.For( this ).Name} {Name}";

	public T Value { get; set; }

	public string Name { get; set; } = "";

	/// <summary>
	/// If true, this parameter can be modified with <see cref="RenderAttributes"/>.
	/// </summary>
	[HideIf( nameof( IsSubgraph ), true )]
	public bool IsAttribute { get; set; }

	/// <summary>
	/// If true, this parameter can be modified directly on the subgraph node.
	/// </summary>
	[JsonIgnore, ShowIf( nameof( IsSubgraph ), true )]
	protected bool IsRequiredInput
	{
		get => IsAttribute;
		set => IsAttribute = value;
	}

	[InlineEditor( Label = false ), Group( "UI" )]
	public Y UI { get; set; }

	protected NodeResult Component( string component, float value, GraphCompiler compiler )
	{
		if ( compiler.IsPreview )
			return compiler.ResultValue( value );

		var result = compiler.Result( new NodeInput { Identifier = Identifier, Output = nameof( Result ) } );
		return new( 1, $"{result}.{component}", true );
	}

	public virtual object GetDefaultValue()
	{
		return default( T );
	}

	public object GetValue()
	{
		return Value;
	}

	public void SetValue( object val )
	{
		Value = (T)val;
	}

	public virtual Vector4 GetRangeMin()
	{
		return Vector4.Zero;
	}

	public virtual Vector4 GetRangeMax()
	{
		return Vector4.Zero;
	}

	public List<string> GetErrors()
	{
		var errors = new List<string>();

		foreach ( var parameterNode in Graph.Nodes )
		{
			if ( parameterNode == this )
				continue;
			if ( !string.IsNullOrWhiteSpace( Name ) && parameterNode is IParameterNode pn && pn.Name == Name )
			{
				errors.Add( $"Duplicate name \"{Name}\" on {this.DisplayInfo.Name}" );
				break;
			}
		}

		return errors;
	}
}
