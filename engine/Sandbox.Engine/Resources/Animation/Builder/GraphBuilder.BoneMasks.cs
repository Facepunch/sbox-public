namespace Sandbox.Animation;

public sealed partial class GraphBuilder
{
	/// <summary>
	/// One of the skeleton's authored bone masks, by name.
	/// </summary>
	public Node BoneMask( string maskName )
	{
		if ( string.IsNullOrWhiteSpace( maskName ) )
			throw new ArgumentException( "Mask name must not be null or blank.", nameof( maskName ) );
		return AddNode( NodeKind.BoneMask, native =>
		{
			var definition = NativeEngine.GraphBoneMaskNode.Add( native );
			definition.SetBoneMaskID( native.InternID( IdString( maskName ) ) );
			return definition;
		} );
	}

	/// <summary>
	/// A bone mask applying the same weight to every bone and float channel.
	/// </summary>
	public Node BoneMask( float weight )
	{
		return AddNode( NodeKind.BoneMask, native =>
		{
			var definition = NativeEngine.GraphFixedWeightBoneMaskNode.Add( native );
			definition.m_boneWeight = weight;
			return definition;
		} );
	}

	/// <summary>
	/// Blend between two bone masks. A weight of 0 uses <paramref name="source"/>, 1 uses <paramref name="target"/>.
	/// </summary>
	public Node BoneMaskBlend( Node source, Node target, Node weight )
	{
		Require( source, NodeKind.BoneMask );
		Require( target, NodeKind.BoneMask );
		Require( weight, NodeKind.Float );
		return AddNode( NodeKind.BoneMask, native =>
		{
			var definition = NativeEngine.GraphBoneMaskBlendNode.Add( native );
			definition.m_sourceMaskNodeIdx = (short)Resolve( source );
			definition.m_targetMaskNodeIdx = (short)Resolve( target );
			definition.m_blendWeightValueNodeIdx = (short)Resolve( weight );
			return definition;
		} );
	}

	public sealed class BoneMaskSelectionSettings
	{
		public bool SwitchDynamically { get; init; }
		public float BlendTime { get; init; } = 0.1f;
	}

	/// <summary>
	/// Pick a bone mask whose ID matches the value of <paramref name="id"/>, falling
	/// back to <paramref name="defaultMask"/> when nothing matches.
	/// </summary>
	/// <param name="id">The ID node used to select a mask.</param>
	/// <param name="defaultMask">The mask used when no option matches.</param>
	/// <param name="options">The ID and mask options.</param>
	/// <param name="settings">Selection and blending options.</param>
	public Node BoneMaskSelector( Node id, Node defaultMask, IEnumerable<(string Id, Node Mask)> options, BoneMaskSelectionSettings settings = null )
	{
		settings ??= new();
		Require( id, NodeKind.Id );
		Optional( defaultMask, NodeKind.BoneMask );
		var list = options?.ToArray() ?? [];
		foreach ( var o in list )
			Require( o.Mask, NodeKind.BoneMask, nameof( options ) );
		return AddNode( NodeKind.BoneMask, native =>
		{
			var definition = NativeEngine.GraphBoneMaskSelectorNode.Add( native );
			definition.m_defaultMaskNodeIdx = (short)ResolveOptional( defaultMask );
			definition.m_parameterValueNodeIdx = (short)Resolve( id );
			definition.m_switchDynamically = settings.SwitchDynamically;
			var nativeMaskNodeIndices = definition.GetMaskNodeIndices();
			foreach ( var o in list )
			{
				nativeMaskNodeIndices.Add( Resolve( o.Mask ) );
			}

			var nativeParameterValues = definition.GetParameterValues();
			foreach ( var o in list )
			{
				nativeParameterValues.Add( native.InternID( IdString( o.Id ) ) );
			}

			definition.m_blendTime = settings.BlendTime;
			return definition;
		} );
	}

	/// <summary>
	/// Select between two bone masks based on a bool <paramref name="condition"/>,
	/// optionally blending when it changes.
	/// </summary>
	/// <param name="condition">The bool node selecting between the masks.</param>
	/// <param name="whenTrue">The mask selected when the condition is true.</param>
	/// <param name="whenFalse">The mask selected when the condition is false.</param>
	/// <param name="settings">Selection and blending options.</param>
	public Node BoneMaskSwitch( Node condition, Node whenTrue, Node whenFalse, BoneMaskSelectionSettings settings = null )
	{
		settings ??= new();
		Require( condition, NodeKind.Bool );
		Require( whenTrue, NodeKind.BoneMask );
		Require( whenFalse, NodeKind.BoneMask );
		return AddNode( NodeKind.BoneMask, native =>
		{
			var definition = NativeEngine.GraphBoneMaskSwitchNode.Add( native );
			definition.m_switchValueNodeIdx = (short)Resolve( condition );
			definition.m_trueMaskNodeIdx = (short)Resolve( whenTrue );
			definition.m_falseMaskNodeIdx = (short)Resolve( whenFalse );
			definition.m_switchDynamically = settings.SwitchDynamically;
			definition.m_blendTime = settings.BlendTime;
			return definition;
		} );
	}
}
