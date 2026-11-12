using System.Runtime.CompilerServices;

namespace Sandbox.Animation;

/// <summary>
/// Provides the ability to generate <see cref="Graph"/>s at runtime.
/// A new instance of this class is available at <see cref="Graph.Builder"/>.
/// </summary>
/// <example>
/// <code>
/// var builder = Graph.Builder;
/// builder.Skeleton = "models/character/character.skel";
///
/// var speed = builder.FloatParameter( "speed" );
/// var blend = builder.Blend( builder.Clip( "idle" ), builder.Clip( "run" ), speed );
///
/// var graph = builder.Create( blend );
/// </code>
/// </example>
public sealed partial class GraphBuilder
{
	/// <summary>
	/// The kind of value a <see cref="Node"/> produces. Used to fail fast when a
	/// node is wired into an input that expects a different value type.
	/// </summary>
	[Expose]
	public enum NodeKind
	{
		Pose,
		Bool,
		Float,
		Id,
		Vector,
		Target,
		BoneMask,
	}

	/// <summary>
	/// A node in the graph being built. Feed these into other nodes to wire the graph together.
	/// </summary>
	public sealed class Node
	{
		internal readonly GraphBuilder Owner;
		internal readonly NodeKind Kind;
		internal readonly int Index;
		internal readonly bool IsClip;

		internal Node( GraphBuilder owner, int index, NodeKind kind, bool isClip )
		{
			Owner = owner;
			Index = index;
			Kind = kind;
			IsClip = isClip;
		}
	}

	private sealed record ParameterInfo( string Name, NodeKind Kind, float Min, float Max, IReadOnlyList<string> Options, bool Describe );

	/// <summary>
	/// Optional settings for <see cref="AddControlParameter"/>.
	/// </summary>
	public sealed class ControlParameterSettings
	{
		/// <summary>
		/// Whether the parameter is described to tools and listed in <see cref="Graph.Parameters"/>.
		/// It's still settable at runtime either way.
		/// </summary>
		public bool Describe { get; init; } = true;

		/// <summary>
		/// Lower bound of a float parameter's range. Only describes the parameter to tools, values aren't clamped.
		/// </summary>
		public float Min { get; init; }

		/// <summary>
		/// Upper bound of a float parameter's range. A range only exists when this is greater than <see cref="Min"/>.
		/// </summary>
		public float Max { get; init; }

		/// <summary>
		/// The values an ID parameter is expected to take. Only describes the parameter to tools.
		/// </summary>
		public IReadOnlyList<string> Options { get; init; } = [];

		public float DefaultFloat { get; init; }
		public bool DefaultBool { get; init; }
		public string DefaultID { get; init; } = "";
		public Vector3 DefaultVector { get; init; }
	}

	/// <summary>
	/// Resource path of the skeleton the graph's clips are authored against. A graph without
	/// one can't bind its clips, so nothing it plays will pose the model.
	/// Assigning this path does not load resources, so graph definitions can be prepared off the main thread.
	/// Use <see cref="WithSkeleton"/> to supply a runtime skeleton directly.
	/// </summary>
	public string Skeleton
	{
		get => SkeletonReference.Path;
		set => SkeletonReference = value;
	}

	internal ResourceId SkeletonReference
	{
		get;
		set
		{
			field = value;
			runtimeSkeleton = null;
		}
	}

	private Skeleton runtimeSkeleton;

	public GraphBuilder WithSkeleton( Skeleton skeleton )
	{
		if ( !skeleton.IsValid() )
			throw new ArgumentException( "A valid skeleton is required.", nameof( skeleton ) );
		SkeletonReference = ResourceId.Get( skeleton );
		runtimeSkeleton = skeleton;
		return this;
	}

	private readonly List<ParameterInfo> parameterInfos = new();
	private readonly List<Func<NativeEngine.AnimationGraphBuilder, NativeEngine.AnimationGraphNodeDefinition>> parameters = new();
	private readonly List<Func<NativeEngine.AnimationGraphBuilder, NativeEngine.AnimationGraphNodeDefinition>> nodes = new();
	private readonly List<(ResourceId Reference, Clip Resource)> animationSlots = new();
	internal IEnumerable<string> ResourceReferences => animationSlots.Select( slot => slot.Reference ).Concat( referencedGraphs.Select( reference => reference.Graph ) ).Append( SkeletonReference ).Select( id => id.ResolvePath() ).Distinct();

	// Parameter names are shared between control and virtual parameters and matched case-insensitively
	private readonly HashSet<string> parameterNames = new( StringComparer.OrdinalIgnoreCase );

	private readonly List<string> virtualParameterNames = new();
	private readonly List<Node> virtualParameterNodes = new();
	private readonly List<(ResourceId Graph, string Variation, Graph Resource)> referencedGraphs = new();
	private readonly List<(string Slot, Node Node)> externalGraphSlots = new();
	private readonly List<(string Slot, Node Node)> externalPoseSlots = new();
	private readonly HashSet<Node> persistentNodes = new();

	// Index-only resolve, called from deferred node closures. Inputs are validated
	// eagerly (Require/Optional) at wiring time, so this only computes the index.
	private int Resolve( Node node )
	{
		if ( node is null )
			throw new InvalidOperationException( "A required node input was not provided." );

		return node.Index;
	}

	// Optional inputs use -1 when not provided
	private int ResolveOptional( Node node ) => node?.Index ?? -1;

	/// <summary>
	/// Validate a required node input: it must be set, owned by this builder, and
	/// produce the expected value kind. Throws an actionable exception otherwise.
	/// </summary>
	private Node Require( Node node, NodeKind kind, [CallerArgumentExpression( nameof( node ) )] string arg = null )
	{
		if ( node is null )
			throw new ArgumentException( $"The '{arg}' node input is required but was not provided.", arg );

		ValidateOwner( node, arg );
		ValidateKind( node, kind, arg );
		return node;
	}

	private Node RequirePose( Node node, [CallerArgumentExpression( nameof( node ) )] string arg = null ) => Require( node, NodeKind.Pose, arg );

	private Node RequireClip( Node node, [CallerArgumentExpression( nameof( node ) )] string arg = null )
	{
		Require( node, NodeKind.Pose, arg );

		if ( !node.IsClip )
			throw new ArgumentException( $"The '{arg}' node must be an animation clip node (for example from Clip(...)).", arg );

		return node;
	}

	/// <summary>
	/// Validate an optional node input. Unset nodes are allowed (omission); set
	/// nodes must be owned by this builder and produce the expected value kind.
	/// </summary>
	private Node Optional( Node node, NodeKind kind, [CallerArgumentExpression( nameof( node ) )] string arg = null )
	{
		if ( node is null )
			return node;

		ValidateOwner( node, arg );
		ValidateKind( node, kind, arg );
		return node;
	}

	private void ValidateOwner( Node node, string arg )
	{
		if ( !ReferenceEquals( node.Owner, this ) )
			throw new ArgumentException( $"The '{arg}' node was created by a different GraphBuilder.", arg );
	}

	private static void ValidateKind( Node node, NodeKind kind, string arg )
	{
		if ( node.Kind != kind )
			throw new ArgumentException( $"The '{arg}' node produces a {node.Kind} value, but a {kind} value was expected.", arg );
	}

	private Node AddParameter( string name, NodeKind kind, float min = 0f, float max = 0f, IReadOnlyList<string> options = null )
	{
		return AddControlParameter( name, kind, new ControlParameterSettings
		{
			Min = min,
			Max = max,
			Options = options ?? [],
		} );
	}

	/// <summary>
	/// Add a control parameter of any kind, settable by name at runtime. Prefer the typed
	/// helpers (<see cref="FloatParameter"/>, <see cref="BoolParameter"/> and friends).
	/// </summary>
	public Node AddControlParameter( string name, NodeKind kind, ControlParameterSettings settings = null )
	{
		settings ??= new();
		if ( nodes.Count > 0 )
			throw new InvalidOperationException( "Control parameters must be added before graph nodes." );
		if ( kind is not (NodeKind.Bool or NodeKind.Id or NodeKind.Float or NodeKind.Vector or NodeKind.Target) )
			throw new ArgumentOutOfRangeException( nameof( kind ) );

		ValidateParameterName( name );

		parameterNames.Add( name );
		parameterInfos.Add( new ParameterInfo( name, kind, settings.Min, settings.Max, settings.Options ?? [], settings.Describe ) );
		parameters.Add( native =>
		{
			switch ( kind )
			{
				case NodeKind.Float:
					{
						var definition = NativeEngine.GraphControlParameterFloatNode.Add( native );
						definition.m_defaultValue = settings.DefaultFloat;
						return definition;
					}
				case NodeKind.Bool:
					{
						var definition = NativeEngine.GraphControlParameterBoolNode.Add( native );
						definition.m_defaultValue = settings.DefaultBool;
						return definition;
					}
				case NodeKind.Id:
					{
						var definition = NativeEngine.GraphControlParameterIDNode.Add( native );
						definition.SetDefaultValue( native.InternID( IdString( settings.DefaultID ) ) );
						return definition;
					}
				case NodeKind.Vector:
					{
						var definition = NativeEngine.GraphControlParameterVectorNode.Add( native );
						definition.m_defaultValue = settings.DefaultVector;
						return definition;
					}
				case NodeKind.Target:
					return NativeEngine.GraphControlParameterTargetNode.Add( native );
				default:
					throw new ArgumentOutOfRangeException( nameof( kind ) );
			}
		} );
		return new Node( this, parameters.Count - 1, kind, isClip: false );
	}

	private Node AddNode( NodeKind kind, Func<NativeEngine.AnimationGraphBuilder, NativeEngine.AnimationGraphNodeDefinition> create, bool isClip = false )
	{
		nodes.Add( create );
		return new Node( this, parameters.Count + nodes.Count - 1, kind, isClip );
	}

	private void ValidateParameterName( string name )
	{
		if ( string.IsNullOrWhiteSpace( name ) )
			throw new ArgumentException( "Parameter name must not be null or blank.", nameof( name ) );

		if ( parameterNames.Contains( name ) )
			throw new ArgumentException( $"A parameter named '{name}' already exists (parameter names are case-insensitive).", nameof( name ) );
	}

	private int GetAnimationSlot( ResourceId animation )
	{
		if ( animation.Guid == Guid.Empty )
			animation.Guid = null;

		if ( animation.Guid is null && string.IsNullOrWhiteSpace( animation.Path ) )
			throw new ArgumentException( "Animation name must not be null or blank.", nameof( animation ) );

		var slot = animationSlots.FindIndex( existing => existing.Resource is null && (animation.Guid is Guid guid
			? existing.Reference.Guid == guid
			: existing.Reference.Guid is null && existing.Reference.Path == animation.Path) );
		if ( slot >= 0 )
			return slot;

		animationSlots.Add( (animation, null) );
		return animationSlots.Count - 1;
	}

	private int GetAnimationSlot( Clip animation )
	{
		if ( !animation.IsValid() )
			throw new ArgumentException( "A valid animation clip is required.", nameof( animation ) );

		var slot = animationSlots.FindIndex( x => ReferenceEquals( x.Resource, animation ) );
		if ( slot >= 0 ) return slot;

		animationSlots.Add( (ResourceId.Get( animation ), animation) );
		return animationSlots.Count - 1;
	}

	/// <summary>
	/// Add a float control parameter, settable by name at runtime. Provide
	/// <paramref name="min"/> &lt; <paramref name="max"/> to describe a bounded range for tools.
	/// </summary>
	public Node FloatParameter( string name, float min = 0f, float max = 0f )
	{
		return AddParameter( name, NodeKind.Float, min, max );
	}

	/// <summary>
	/// Add a bool control parameter, settable by name at runtime.
	/// </summary>
	public Node BoolParameter( string name )
	{
		return AddParameter( name, NodeKind.Bool );
	}

	/// <summary>
	/// Add an ID (tag) control parameter, settable by name at runtime. IDs are
	/// matched by name against events, selectors and comparisons. Optionally list
	/// the <paramref name="options"/> this parameter is expected to take, for tools.
	/// </summary>
	public Node IdParameter( string name, params string[] options )
	{
		return AddParameter( name, NodeKind.Id, options: options );
	}

	/// <summary>
	/// Add a vector control parameter, settable by name at runtime.
	/// </summary>
	public Node VectorParameter( string name )
	{
		return AddParameter( name, NodeKind.Vector );
	}

	/// <summary>
	/// Add a target control parameter - a transform in space, settable by name
	/// at runtime. Used as input to IK and warp nodes.
	/// </summary>
	public Node TargetParameter( string name )
	{
		return AddParameter( name, NodeKind.Target );
	}

	private static string IdString( string id ) => string.IsNullOrWhiteSpace( id ) ? "" : id;

	internal byte[] Compile( Node root, string model, string variation )
	{
		var native = BuildNative( root );
		try
		{
			using var buffer = native.Compile( root.Index, model ?? "", variation );
			if ( buffer.IsNull )
				throw new InvalidOperationException( native.GetErrorString() );
			return buffer.ToArray();
		}
		finally
		{
			native.DeleteThis();
		}
	}

	private NativeEngine.AnimationGraphBuilder BuildNative( Node root, bool bindResources = false )
	{
		RequirePose( root );
		if ( parameters.Count + nodes.Count > short.MaxValue )
			throw new InvalidOperationException( "Graph exceeds the node index limit." );
		var native = NativeEngine.AnimationGraphBuilder.Create();
		try
		{
			var skeletonPath = SkeletonReference.ResolvePath() ?? "";
			native.SetSkeleton( skeletonPath, SkeletonReference.Guid ?? Guid.Empty );
			if ( bindResources )
			{
				var skeleton = runtimeSkeleton;
				if ( skeleton is null && Mounting.MountUtility.IsMountPath( skeletonPath ) )
					skeleton = Animation.Skeleton.Load( SkeletonReference );
				if ( runtimeSkeleton is not null && !skeleton.IsValid() )
					throw new InvalidOperationException( "The runtime skeleton has been disposed." );
				if ( skeleton.IsValid() )
					native.SetSkeletonResource( skeleton.native );
			}
			foreach ( var parameter in parameters )
				Check( parameter( native ) );
			foreach ( var node in nodes )
				Check( node( native ) );

			var persistent = new SortedSet<int>( Enumerable.Range( 0, parameters.Count ) );
			foreach ( var node in persistentNodes )
				persistent.Add( node.Index );
			foreach ( var index in persistent )
				native.AddPersistentNode( index );
			foreach ( var parameter in parameterInfos )
			{
				native.AddControlParameter( parameter.Name );
				if ( !parameter.Describe )
					continue;
				var type = parameter.Kind switch
				{
					NodeKind.Bool => GraphValueKind.Bool,
					NodeKind.Id => GraphValueKind.Id,
					NodeKind.Float => GraphValueKind.Float,
					NodeKind.Vector => GraphValueKind.Vector,
					NodeKind.Target => GraphValueKind.Target,
					_ => throw new ArgumentOutOfRangeException( nameof( parameter.Kind ) ),
				};
				var hasRange = parameter.Kind == NodeKind.Float && parameter.Max > parameter.Min;
				native.AddParameterDescription( parameter.Name, type, hasRange ? parameter.Min : 0f, hasRange ? parameter.Max : 0f );
				if ( parameter.Kind == NodeKind.Id )
					foreach ( var option in parameter.Options )
						native.AddParameterOption( option );
			}
			for ( var i = 0; i < virtualParameterNames.Count; i++ )
				native.AddVirtualParameter( virtualParameterNames[i], virtualParameterNodes[i].Index );
			foreach ( var slot in externalGraphSlots )
				native.AddExternalGraphSlot( slot.Slot, slot.Node.Index );
			foreach ( var slot in externalPoseSlots )
				native.AddExternalPoseSlot( slot.Slot, slot.Node.Index );
			foreach ( var slot in animationSlots )
			{
				var index = native.AddAnimation( slot.Reference.ResolvePath() ?? "", slot.Reference.Guid ?? Guid.Empty );
				if ( index < 0 )
					throw new InvalidOperationException( native.GetErrorString() );
				if ( bindResources )
				{
					var clip = slot.Resource;
					if ( clip is null && Mounting.MountUtility.IsMountPath( slot.Reference.ResolvePath() ) )
						clip = Animation.Clip.Load( slot.Reference );
					if ( slot.Resource is not null && !clip.IsValid() )
						throw new InvalidOperationException( "The runtime clip has been disposed." );
					if ( clip.IsValid() )
						native.SetAnimationResource( index, clip.native );
				}
			}
			foreach ( var slot in referencedGraphs )
			{
				var index = native.AddReferencedGraph( slot.Graph.ResolvePath() ?? "", slot.Graph.Guid ?? Guid.Empty, slot.Variation );
				if ( index < 0 )
					throw new InvalidOperationException( native.GetErrorString() );
				if ( bindResources )
				{
					var graph = slot.Resource;
					if ( graph is null && Mounting.MountUtility.IsMountPath( slot.Graph.ResolvePath() ) )
						graph = string.IsNullOrEmpty( slot.Variation ) ? Graph.Load( slot.Graph )
							: Graph.Load( $"{slot.Graph.ResolvePath()}+{slot.Variation}.ag" );
					if ( slot.Resource is not null && !graph.IsValid() )
						throw new InvalidOperationException( $"Referenced graph '{slot.Graph}' has been disposed." );
					if ( graph.IsValid() )
						native.SetReferencedGraphResource( index, graph.native );
				}
			}
			return native;

			void Check( NativeEngine.AnimationGraphNodeDefinition definition )
			{
				if ( definition.IsNull )
					throw new InvalidOperationException( native.GetErrorString() );
			}
		}
		catch
		{
			native.DeleteThis();
			throw;
		}

	}

	internal void MarkPersistent( Node node )
	{
		ValidateOwner( node, nameof( node ) );
		persistentNodes.Add( node );
	}

	/// <summary>
	/// Creates an animation graph with <paramref name="root"/> producing the final pose.
	/// </summary>
	/// <param name="root">The pose node producing the graph's output.</param>
	/// <param name="name">
	/// Optional name. Graphs made this way are anonymous - they can never be found by name, so
	/// this is only used when identifying the graph in logs and profiles.
	/// </param>
	public Graph Create( Node root, string name = null )
	{
		ThreadSafe.AssertIsMainThread();
		name = string.IsNullOrWhiteSpace( name ) ? "runtime_animgraph" : name.Trim();
		var native = BuildNative( root, bindResources: true );
		try
		{
			var graph = Graph.FromNative( NativeGlue.Resources.CreateBuiltAnimGraph2( name.Replace( ':', '_' ), native, root.Index ), name, procedural: true );
			return graph ?? throw new InvalidOperationException( $"Failed to create animation graph '{name}'." );
		}
		finally
		{
			native.DeleteThis();
		}
	}
}
