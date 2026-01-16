using Facepunch.ActionGraphs;
using Facepunch.ActionGraphs.Compilation;
using System.Linq.Expressions;
using System.Reflection;
using Sandbox.Engine;

#nullable enable

namespace Sandbox.ActionGraphs;

public interface IActionGraphEvents
{
	void SceneReferenceTriggered( SceneReferenceTriggeredEvent ev ) { }
}

public readonly record struct SceneReferenceTriggeredEvent(
	GameObject Source,
	IValid Target,
	Node Node );

[NodeDefinition]
internal partial class SceneRefNodeDefinition : NodeDefinition
{
	public const string Ident = "scene.ref";

	private InputDefinition TargetInput { get; } = InputDefinition.Target( typeof( GameObject ) );

	private PropertyDefinition ComponentProperty { get; } = new( "component", typeof( ComponentReference ),
		PropertyFlags.Required, new Facepunch.ActionGraphs.DisplayInfo( "Component", "Component this node will output.", Hidden: true ) );

	private PropertyDefinition GameObjectProperty { get; } = new( "gameobject", typeof( GameObjectReference ),
		PropertyFlags.Required, new Facepunch.ActionGraphs.DisplayInfo( "Game Object", "Game Object this node will output.", Hidden: true ) );

	private OutputDefinition Output { get; } = new( ParameterNames.Result, typeof( Either<GameObject, Component> ),
		0, new Facepunch.ActionGraphs.DisplayInfo( "Result", "The referenced value." ) );

	private NodeBinding DefaultBinding { get; }

	private NodeBinding ComponentBinding { get; }
	private NodeBinding GameObjectBinding { get; }

	public override Facepunch.ActionGraphs.DisplayInfo DisplayInfo { get; }

	#region Debug

	private static MethodInfo DispatchTriggered_Method { get; } =
		typeof( SceneRefNodeDefinition ).GetMethod( nameof( DispatchTriggered ),
			BindingFlags.NonPublic | BindingFlags.Static )!;

	private static void DispatchTriggered( GameObject source, IValid target, Node node )
	{
		IToolsDll.Current?.RunEvent<IActionGraphEvents>( x => x.SceneReferenceTriggered( new SceneReferenceTriggeredEvent( source, target, node ) ) );
	}

	private Expression? BuildDispatchTriggeredExpression( Expression sourceExpr, Expression targetExpr, Node node )
	{
		if ( sourceExpr.Type != typeof( GameObject ) )
		{
			return null;
		}

		if ( IToolsDll.Current is null )
		{
			return null;
		}

		return Expression.Call( DispatchTriggered_Method, sourceExpr, targetExpr, Expression.Constant( node ) );
	}

	#endregion

	public SceneRefNodeDefinition( NodeLibrary nodeLibrary )
		: base( nodeLibrary, Ident )
	{
		DisplayInfo = new Facepunch.ActionGraphs.DisplayInfo( "Scene Reference",
			Description: "References a GameObject or Component from the scene this graph is embedded in.",
			Group: "Scene",
			Icon: "location_searching",
			Hidden: true );

		DefaultBinding = NodeBinding.Create( DisplayInfo,
			inputs: [TargetInput],
			properties: [ComponentProperty, GameObjectProperty, LegacyTypeProperty, LegacyValueProperty],
			outputs: [Output] );

		ComponentBinding = NodeBinding.Create(
			DisplayInfo with
			{
				Title = "Component Reference",
				Description = "References a Component from the scene this graph is embedded in."
			},
			inputs: [TargetInput],
			properties: [ComponentProperty],
			outputs:
			[
				Output with
				{
					Type = typeof(Component),
					Display = Output.Display with { Description = "The referenced Component." }
				}
			] );

		GameObjectBinding = NodeBinding.Create(
			DisplayInfo with
			{
				Title = "Game Object Reference",
				Description = "References a Game Object from the scene this graph is embedded in."
			},
			inputs: [TargetInput],
			properties: [GameObjectProperty],
			outputs:
			[
				Output with
			{
				Type = typeof(GameObject),
				Display = Output.Display with
				{
					Description = "The referenced Game Object."
				}
			}
			] );

		InitLegacy();
	}

	private record Target(
		GameObjectReference GameObjectRef,
		ComponentReference? ComponentRef = null,
		GameObject? GameObject = null,
		Component? Component = null,
		bool IsLegacy = false )
	{
		public string? Name => IsComponent && Component is { } comp ? $"{comp.GameObject?.Name} \u2192 {comp.GetType().Name}" : GameObject?.Name;

		public bool IsGameObject => ComponentRef is null;
		public bool IsComponent => ComponentRef is not null;
	}

	private (NodeBinding Binding, Target? Target) BindTarget( BindingSurface surface )
	{
		if ( surface.Properties.TryGetValue( ComponentProperty.Name, out var compRefObj ) && compRefObj is ComponentReference compRef )
		{
			var compType = compRef.ResolveComponentType() ?? typeof( Component );
			var typeDesc = Game.TypeLibrary.GetType( compType );

			var desc = $"References a {typeDesc.Title} from the scene this graph is embedded in.";

			if ( !string.IsNullOrWhiteSpace( typeDesc.Description ) )
			{
				desc = $"{desc}<br/><br/>{typeDesc.Description}";
			}

			return (ComponentBinding with
			{
				DisplayInfo = ComponentBinding.DisplayInfo with
				{
					Title = $"{typeDesc.Title} Reference",
					Description = desc,
					Icon = typeDesc.Icon ?? ComponentBinding.DisplayInfo.Icon
				}

			}, new Target( (GameObjectReference)compRef, ComponentRef: compRef ));
		}

		if ( surface.Properties.TryGetValue( GameObjectProperty.Name, out var goRefObj ) && goRefObj is GameObjectReference goRef )
		{
			var binding = GameObjectBinding;

			if ( !string.IsNullOrWhiteSpace( goRef.PrefabPath ) )
			{
				binding = binding with
				{
					DisplayInfo = binding.DisplayInfo with
					{
						Title = "Prefab Reference",
						Description = "References a Game Object from a prefab file.",
						Icon = "ballot"
					}
				};
			}

			return (binding, new Target( goRef ));
		}

		if ( BindTargetLegacy( surface ) is { } legacyBinding )
		{
			return legacyBinding;
		}

		return (DefaultBinding, null);
	}

	protected override NodeBinding OnBind( BindingSurface surface )
	{
		var (binding, target) = BindTarget( surface );

		if ( target is null )
		{
			return binding;
		}

		if ( surface.ActionGraph?.GetEmbeddedTarget() is GameObject graphTarget )
		{
			try
			{
				target = target.ComponentRef is { } compRef
					? target with { Component = compRef.Resolve( graphTarget.Scene ) ?? throw new Exception( "Component not found in the same scene as this graph." ) }
					: target with { GameObject = target.GameObjectRef.Resolve( graphTarget.Scene ) ?? throw new Exception( "GameObject not found in the same scene as this graph." ) };
			}
			catch ( Exception ex )
			{
				binding = binding with
				{
					Messages = [new NodeBinding.ValidationMessage( null, MessageLevel.Warning, ex.Message )]
				};
			}
		}

		var refType = target.IsComponent
			? target.ComponentRef!.Value.ResolveComponentType() ?? typeof( Component )
			: typeof( GameObject );

		return binding with
		{
			DisplayInfo = binding.DisplayInfo with
			{
				Title = target.Name ?? binding.DisplayInfo.Title
			},
			Outputs = [binding.Outputs.First() with { Type = refType }],
			Target = target
		};
	}

	private Expression ResolveReferenceExpression( INodeExpressionBuilder builder, Target target )
	{
		var getRef = builder.GetPropertyValue( target.IsGameObject ? GameObjectProperty : ComponentProperty );
		var resolveMethod = getRef.Type.GetMethod( nameof( GameObjectReference.Resolve ),
			BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes )!;
		return Expression.Call( getRef, resolveMethod );
	}

	protected override Expression OnBuildExpression( INodeExpressionBuilder builder )
	{
		var target = builder.GetBindingTarget<Target>();
		var resolveReference = target.IsLegacy
			? ResolveReferenceExpressionLegacy( builder, target )
			: ResolveReferenceExpression( builder, target );

		var assign = builder.GetOutputValue().Assign( target.IsComponent
			? Expression.Convert( resolveReference, builder.Node.Outputs.Values.First().Type )
			: resolveReference );

		return BuildDispatchTriggeredExpression( builder.GetInputValue( TargetInput ), resolveReference, builder.Node ) is { } trigger
			? Expression.Block( trigger, assign )
			: assign;
	}
}
