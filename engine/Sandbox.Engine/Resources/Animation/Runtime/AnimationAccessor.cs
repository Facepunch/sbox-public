using System.Text.Json.Nodes;

namespace Sandbox.Animation;

public enum GraphValueKind : byte
{
	Unknown,
	Bool,
	Id,
	Float,
	Vector,
	Target,
	BoneMask,
	Pose
}

/// <summary>
/// Animation assignment and runtime access for a model. Handles are resolved on each call, including after model replacement or reload.
/// </summary>
[Expose]
public sealed class AnimationAccessor : IJsonPopulator
{
	private readonly SceneModel _model;
	private readonly SkinnedModelRenderer _renderer;
	private GraphParameters _parameters;
	private GraphRecording _recording;
	private GraphDebug _debug;
	private GraphExternalSlots _externalGraphs;

	internal SceneModel Model => _renderer is null ? _model : _renderer.SceneModel;
	internal NativeEngine.AnimationGraph2Controller Controller => Model?.animGraph2Native ?? default;

	internal AnimationAccessor( SceneModel model ) => _model = model;

	internal AnimationAccessor( SkinnedModelRenderer renderer ) => _renderer = renderer;

	[Hide]
	public bool IsValid => !Controller.IsNull;

	/// <summary>
	/// The graph resource in use, falling back to the model's default. Set to null to clear the override.
	/// </summary>
	[Property, Title( "Animation Graph" )]
	public Graph Graph
	{
		get => _renderer is null ? _model.GraphDefinition : _renderer.GraphDefinition;
		set
		{
			if ( _renderer is null )
				_model.GraphDefinition = value;
			else
				_renderer.GraphDefinition = value;
		}
	}

	/// <summary>Live graph parameters and saved inspector values.</summary>
	[Property, Title( "Parameters" ), ShowIf( nameof( ShouldShowParameters ), true )]
	public GraphParameters Parameters => _parameters ??= new( this );

	[Hide]
	public bool ShouldShowParameters => Model is { UseAnimGraph: true } && Graph is { Parameters.Count: > 0 };

	internal int StoredParameterCount => _parameters?.StoredCount ?? 0;
	internal void ApplyStoredParameters() => _parameters?.ApplyStoredValues();

	[Hide]
	public GraphRecording Recording => _recording ??= new( this );
	[Hide]
	public GraphDebug Debug => _debug ??= new( this );

	/// <summary>Named External Graph slots for runtime-selected animation behavior.</summary>
	[Hide]
	public GraphExternalSlots ExternalGraphs => _externalGraphs ??= new( this );

	/// <summary>Evaluation time of the last graph update.</summary>
	[Hide]
	public TimeSpan EvaluationTime => Model is { animNative.IsNull: false } model
		? TimeSpan.FromMilliseconds( model.animNative.GetAnimGraph2UpdateTimeMs() )
		: TimeSpan.Zero;

	/// <summary>Whether a tag is active in the current graph pose. Returns false without a running graph.</summary>
	public bool IsTagActive( string tag )
	{
		var controller = Controller;
		return !controller.IsNull && controller.IsTagActive( tag );
	}

	JsonNode IJsonPopulator.Serialize()
	{
		return new JsonObject
		{
			["Graph"] = Json.ToNode( _renderer is null ? _model.GraphOverride : _renderer.GraphOverride ),
			["Parameters"] = ((IJsonPopulator)Parameters).Serialize()
		};
	}

	void IJsonPopulator.Deserialize( JsonNode node )
	{
		if ( node is null )
		{
			_parameters?.ClearStoredValues();
			Graph = null;
			return;
		}

		if ( node is not JsonObject obj )
			throw new ArgumentException( "Animation settings must be a JSON object.", nameof( node ) );

		_parameters?.ClearStoredValues();
		Graph = Json.FromNode<Graph>( obj["Graph"] );
		((IJsonPopulator)Parameters).Deserialize( obj["Parameters"] );
	}
}
