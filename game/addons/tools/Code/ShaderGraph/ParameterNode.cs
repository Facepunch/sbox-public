namespace Editor.ShaderGraph;

public interface IParameterNode
{
	string Name { get; }

	Guid BlackboardParameterIdentifier { get; set; }

	bool IsAttribute { get; }

	IParameterUI UI { get; }
}

public interface ITextureParameterNode
{
	string Image { get; set; }
	TextureInput UI { get; set; }
}

public abstract class ParameterNode<T, Y> : ShaderNode, IParameterNode, IErroringNode where Y : BlackboardParameter
{
	private record SharedMaterialParameterData( string Name, object Value, IParameterUI ParameterUI, bool IsAttribute );

	[Hide]
	public override string Title => string.IsNullOrWhiteSpace( Name ) ?
		$"{DisplayInfo.For( this ).Name}" :
		$"{DisplayInfo.For( this ).Name} {Name}";

	[Hide]
	public Guid BlackboardParameterIdentifier { get; set; }

	[Hide]
	public T Value
	{
		get => (T)GetSharedParameterData().Value;
		set
		{
			if ( Graph is ShaderGraph graph )
			{
				graph.UpdateParameterValue( BlackboardParameterIdentifier, value );

				Update();
				IsDirty = true;
			}
		}
	}

	[Hide]
	public string Name => GetSharedParameterData().Name;

	[Hide]
	public bool IsAttribute => GetSharedParameterData().IsAttribute;

	[Hide]
	public IParameterUI UI => GetSharedParameterData().ParameterUI;

	private SharedMaterialParameterData GetSharedParameterData()
	{
		if ( Graph is ShaderGraph graph )
		{
			var parameter = graph.FindParameter( BlackboardParameterIdentifier );

			switch ( parameter )
			{
				case BoolBlackboardParameter v:
					return new SharedMaterialParameterData( v.Name, v.Value, v.UI, v.IsAttribute );
				case IntBlackboardParameter v:
					return new SharedMaterialParameterData( v.Name, v.Value, v.UI, v.IsAttribute );
				case FloatBlackboardParameter v:
					return new SharedMaterialParameterData( v.Name, v.Value, v.UI, v.IsAttribute );
				case Float2BlackboardParameter v:
					return new SharedMaterialParameterData( v.Name, v.Value, v.UI, v.IsAttribute );
				case Float3BlackboardParameter v:
					return new SharedMaterialParameterData( v.Name, v.Value, v.UI, v.IsAttribute );
				case Float4BlackboardParameter v:
					return new SharedMaterialParameterData( v.Name, v.Value, v.UI, v.IsAttribute );
				case ColorBlackboardParameter v:
					return new SharedMaterialParameterData( v.Name, v.Value, v.UI, v.IsAttribute );
				default:
					throw new NotImplementedException();
			}
		}

		return default;
	}

	protected NodeResult Component( string component, float value, GraphCompiler compiler )
	{
		if ( compiler.IsPreview )
			return compiler.ResultValue( value );

		var result = compiler.Result( new NodeInput { Identifier = Identifier, Output = nameof( Result ) } );
		return new( NodeResultType.Float, $"{result}.{component}", true );
	}

	protected Y GetParameter()
	{
		if ( Graph is ShaderGraph graph )
		{
			return (Y)graph.FindParameter( BlackboardParameterIdentifier );
		}

		return null;
	}

	public List<string> GetErrors()
	{
		var errors = new List<string>();

		return errors;
	}
}

