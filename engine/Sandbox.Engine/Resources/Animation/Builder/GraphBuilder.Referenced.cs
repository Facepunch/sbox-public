namespace Sandbox.Animation;

public sealed partial class GraphBuilder
{
	private readonly HashSet<int> externalGraphNodeIndices = new();
	private Node VirtualParameter( string name, Node value, NodeKind kind )
	{
		Require( value, kind, nameof( value ) );
		ValidateParameterName( name );
		var node = AddNode( kind, native =>
		{
			switch ( kind )
			{
				case NodeKind.Bool:
					{
						var definition = NativeEngine.GraphVirtualParameterBoolNode.Add( native );
						definition.m_childNodeIdx = (short)Resolve( value );
						return definition;
					}

				case NodeKind.Float:
					{
						var definition = NativeEngine.GraphVirtualParameterFloatNode.Add( native );
						definition.m_childNodeIdx = (short)Resolve( value );
						return definition;
					}

				case NodeKind.Id:
					{
						var definition = NativeEngine.GraphVirtualParameterIDNode.Add( native );
						definition.m_childNodeIdx = (short)Resolve( value );
						return definition;
					}

				case NodeKind.Vector:
					{
						var definition = NativeEngine.GraphVirtualParameterVectorNode.Add( native );
						definition.m_childNodeIdx = (short)Resolve( value );
						return definition;
					}

				case NodeKind.Target:
					{
						var definition = NativeEngine.GraphVirtualParameterTargetNode.Add( native );
						definition.m_childNodeIdx = (short)Resolve( value );
						return definition;
					}

				case NodeKind.BoneMask:
					{
						var definition = NativeEngine.GraphVirtualParameterBoneMaskNode.Add( native );
						definition.m_childNodeIdx = (short)Resolve( value );
						return definition;
					}

				default:
					throw new ArgumentOutOfRangeException( nameof( kind ) );
			}
		} );
		parameterNames.Add( name );
		virtualParameterNames.Add( name );
		virtualParameterNodes.Add( node );
		return node;
	}

	/// <summary>
	/// Name a computed bool value as a virtual parameter. Unlike a control parameter it
	/// is not set externally - it evaluates <paramref name="value"/> each frame - but it
	/// can be reflected into referenced child graphs by name.
	/// </summary>
	public Node VirtualBoolParameter( string name, Node value ) => VirtualParameter( name, value, NodeKind.Bool );
	/// <inheritdoc cref="VirtualBoolParameter"/>
	public Node VirtualFloatParameter( string name, Node value ) => VirtualParameter( name, value, NodeKind.Float );
	/// <inheritdoc cref="VirtualBoolParameter"/>
	public Node VirtualIdParameter( string name, Node value ) => VirtualParameter( name, value, NodeKind.Id );
	/// <inheritdoc cref="VirtualBoolParameter"/>
	public Node VirtualVectorParameter( string name, Node value ) => VirtualParameter( name, value, NodeKind.Vector );
	/// <inheritdoc cref="VirtualBoolParameter"/>
	public Node VirtualTargetParameter( string name, Node value ) => VirtualParameter( name, value, NodeKind.Target );
	/// <inheritdoc cref="VirtualBoolParameter"/>
	public Node VirtualBoneMaskParameter( string name, Node value ) => VirtualParameter( name, value, NodeKind.BoneMask );
	/// <summary>
	/// Bind an existing node as a named virtual parameter.
	/// </summary>
	/// <exception cref="ArgumentException">The name is null, blank, or already used by another parameter (case-insensitive).</exception>
	public void BindVirtualParameter( string name, Node value )
	{
		ValidateOwner( value, nameof( value ) );
		ValidateParameterName( name );
		parameterNames.Add( name );
		virtualParameterNames.Add( name );
		virtualParameterNodes.Add( value );
		MarkPersistent( value );
	}

	private void ValidateSlotName( List<(string Slot, Node Node)> slots, string slotName )
	{
		if ( string.IsNullOrWhiteSpace( slotName ) )
			throw new ArgumentException( "Slot name must not be null or blank.", nameof( slotName ) );
		if ( slots.Any( s => string.Equals( s.Slot, slotName, StringComparison.OrdinalIgnoreCase ) ) )
			throw new ArgumentException( $"A slot named '{slotName}' already exists (slot names are case-insensitive).", nameof( slotName ) );
	}

	/// <summary>
	/// A pose slot filled by native integration before evaluation.
	/// Managed runtime pose assignment is not currently exposed.
	/// </summary>
	public Node ExternalPose( string slotName )
	{
		ValidateSlotName( externalPoseSlots, slotName );
		var node = AddNode( NodeKind.Pose, native =>
		{
			var definition = NativeEngine.GraphExternalPoseNode.Add( native );
			return definition;
		} );
		externalPoseSlots.Add( (slotName, node) );
		return node;
	}

	/// <summary>
	/// Embed another animation graph asset as a child graph. Its control parameters are
	/// reflected from this graph by matching name. Plays <paramref name="fallback"/>
	/// (or nothing) while the child graph is unavailable.
	/// </summary>
	/// <param name="graphPath">Asset path of the child animation graph.</param>
	/// <param name="fallback">Optional pose played while the child graph is unavailable.</param>
	public Node ReferencedGraph( string graphPath, Node fallback = default )
	{
		if ( string.IsNullOrWhiteSpace( graphPath ) )
			throw new ArgumentException( "Referenced graph path must not be null or blank.", nameof( graphPath ) );
		Optional( fallback, NodeKind.Pose );
		// Every use gets its own slot - each runtime node needs a private child instance
		referencedGraphs.Add( graphPath );
		var slotIdx = referencedGraphs.Count - 1;
		return AddNode( NodeKind.Pose, native =>
		{
			var definition = NativeEngine.GraphReferencedGraphNode.Add( native );
			definition.m_referencedGraphIdx = (short)slotIdx;
			definition.m_fallbackNodeIdx = (short)ResolveOptional( fallback );
			return definition;
		} );
	}

	/// <summary>
	/// A child-graph slot filled at runtime through <see cref="AnimationAccessor.ExternalGraphs"/>
	/// using <paramref name="slotName"/>. Plays <paramref name="fallback"/> (or nothing) while empty.
	/// </summary>
	public Node ExternalGraphSlot( string slotName, Node fallback = default )
	{
		ValidateSlotName( externalGraphSlots, slotName );
		Optional( fallback, NodeKind.Pose );
		var node = AddNode( NodeKind.Pose, native =>
		{
			var definition = NativeEngine.GraphReferencedGraphNode.Add( native );
			definition.m_fallbackNodeIdx = (short)ResolveOptional( fallback );
			return definition;
		} );
		externalGraphSlots.Add( (slotName, node) );
		externalGraphNodeIndices.Add( node.Index );
		return node;
	}

	/// <summary>
	/// True while the given <see cref="ExternalGraphSlot"/> has a child graph connected.
	/// </summary>
	public Node IsExternalGraphSlotFilled( Node externalGraphSlot )
	{
		RequirePose( externalGraphSlot );
		if ( !externalGraphNodeIndices.Contains( externalGraphSlot.Index ) )
			throw new ArgumentException( "IsExternalGraphSlotFilled requires a node returned by ExternalGraphSlot(...).", nameof( externalGraphSlot ) );
		return AddNode( NodeKind.Bool, native =>
		{
			var definition = NativeEngine.GraphIsExternalGraphSlotFilledNode.Add( native );
			definition.m_externalGraphNodeIdx = (short)Resolve( externalGraphSlot );
			return definition;
		} );
	}
}
