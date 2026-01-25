using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Editor.ShaderGraph;

public interface IParameterNodeBase
{
	string Name { get; set; }

	Guid BlackboardParameterIdentifier { get; set; }

	void UpdateFromBlackboard( IBlackboardParameter parameter );
}

public interface IParameterNode<T> where T : IParameterUI
{
	string Name { get; set; }

	bool IsAttribute { get; set; }

	T UI { get; set; }
}

public interface ITextureParameterNode
{
	string Image { get; set; }
	TextureInput UI { get; set; }
}

public abstract class ParameterNode<T, PUI, BP> : ShaderNode, IParameterNodeBase, IParameterNode<PUI>, IErroringNode
	where PUI : IParameterUI
	where BP : IBlackboardParameter
{
	[Hide]
	protected bool IsSubgraph => (Graph is ShaderGraph shaderGraph && shaderGraph.IsSubgraph);

	[Hide]
	public override string Title => string.IsNullOrWhiteSpace( Name ) ?
		$"{DisplayInfo.For( this ).Name}" :
		$"{DisplayInfo.For( this ).Name} {Name}";

	[Hide, Browsable( false )]
	public Guid BlackboardParameterIdentifier { get; set; }

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
	public PUI UI { get; set; }

	protected NodeResult Component( string component, float value, GraphCompiler compiler )
	{
		if ( compiler.IsPreview )
			return compiler.ResultValue( value );

		var result = compiler.Result( new NodeInput { Identifier = Identifier, Output = nameof( Result ) } );
		return new( NodeResultType.Float, $"{result}.{component}", true );
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

	protected virtual void UpdateFromBlackboardParameter( BP parameter )
	{
	}

	public void UpdateFromBlackboard( IBlackboardParameter parameter )
	{
		UpdateFromBlackboardParameter( (BP)parameter );
	}

	public List<string> GetErrors()
	{
		var errors = new List<string>();

		return errors;
	}
}
