namespace Sandbox.Animation;

public sealed partial class GraphBuilder
{
	public sealed class ClipSettings
	{
		public bool Looping { get; init; } = true;
		public float Speed { get; init; } = 1.0f;
		public Node PlayInReverse { get; init; }
		public Node ResetTime { get; init; }
		public int SyncEventOffset { get; init; }
		public bool SampleRootMotion { get; init; } = true;
		public IEnumerable<string> GraphEvents { get; init; }
	}

	public sealed class TimeControlledClipSettings
	{
		public Node PlayInReverse { get; init; }
		public bool SampleRootMotion { get; init; } = true;
		public IEnumerable<string> GraphEvents { get; init; }
	}

	public sealed class PoseSettings
	{
		public Node Time { get; init; }
		public float RemapRangeStart { get; init; }
		public float RemapRangeEnd { get; init; } = 1.0f;
		public float FixedTime { get; init; }
		public bool UseFramesAsInput { get; init; }
	}

	public sealed class ParameterizedSelectorSettings
	{
		public IReadOnlyList<int> Weights { get; init; } = [];
		public bool IgnoreInvalidOptions { get; init; }
		public bool HasWeights { get; init; }
		public bool ClipsOnly { get; init; }
	}

	public sealed class TargetSelectorSettings
	{
		public float PositionWeight { get; init; } = 1.0f;
		public float OrientationWeight { get; init; } = 1.0f;
		public bool WorldSpace { get; init; }
		public bool IgnoreInvalidOptions { get; init; }
		public string AlignmentBone { get; init; }
	}

	public sealed class BlendSpace2DSettings
	{
		public IReadOnlyList<Node> Sources { get; init; } = [];
		public IReadOnlyList<Vector2> Positions { get; init; } = [];
		public IReadOnlyList<int> Indices { get; init; } = [];
		public IReadOnlyList<int> HullIndices { get; init; } = [];
		public bool Looping { get; init; } = true;
	}

	public sealed class LayerSettings
	{
		public Node Weight { get; init; }
		public Node BoneMask { get; init; }
		public LayerBlendMode BlendMode { get; init; } = LayerBlendMode.Overlay;
		public bool Synchronized { get; init; }
		public bool IgnoreEvents { get; init; }
		public Node RootMotionWeight { get; init; }
	}

	/// <summary>
	/// Play an animation clip, by resource path.
	/// </summary>
	/// <param name="animation">Resource path of the clip to play.</param>
	/// <param name="settings">Playback and event options.</param>
	public Node Clip( string animation, ClipSettings settings = null ) => Clip( (ResourceId)animation, settings );

	internal Node Clip( ResourceId animation, ClipSettings settings = null )
	{
		settings ??= new();
		var slot = GetAnimationSlot( animation );
		Optional( settings.PlayInReverse, NodeKind.Bool );
		Optional( settings.ResetTime, NodeKind.Bool );
		var events = settings.GraphEvents?.ToArray() ?? [];
		return AddNode( NodeKind.Pose, native =>
		{
			var definition = NativeEngine.GraphClipNode.Add( native );
			definition.m_dataSlotIdx = (short)slot;
			definition.m_playInReverseValueNodeIdx = (short)ResolveOptional( settings.PlayInReverse );
			definition.m_resetTimeValueNodeIdx = (short)ResolveOptional( settings.ResetTime );
			definition.m_startSyncEventOffset = settings.SyncEventOffset;
			definition.m_sampleRootMotion = settings.SampleRootMotion;
			definition.m_allowLooping = settings.Looping;
			definition.m_speedMultiplier = settings.Speed;
			var nativeGraphEvents = definition.GetGraphEvents();
			foreach ( var nativeValue in events )
			{
				nativeGraphEvents.Add( native.InternID( IdString( nativeValue ) ) );
			}

			return definition;
		}, isClip: true );
	}

	/// <inheritdoc cref="Clip(string, ClipSettings)"/>
	public Node Clip( Clip animation, ClipSettings settings = null )
	{
		ArgumentNullException.ThrowIfNull( animation );
		return Clip( ResourceId.Get( animation ), settings );
	}

	/// <summary>
	/// Play an animation whose playback time is driven directly by <paramref name="time"/>
	/// (normalized 0-1 across the clip) instead of advancing with the graph - e.g. scrubbing
	/// a full clip from a gameplay value. Ignores synchronization.
	/// </summary>
	public Node TimeControlledClip( string animation, Node time, TimeControlledClipSettings settings = null ) => TimeControlledClip( (ResourceId)animation, time, settings );

	internal Node TimeControlledClip( ResourceId animation, Node time, TimeControlledClipSettings settings = null )
	{
		settings ??= new();
		var slot = GetAnimationSlot( animation );
		Require( time, NodeKind.Float );
		Optional( settings.PlayInReverse, NodeKind.Bool );
		var events = settings.GraphEvents?.ToArray() ?? [];
		return AddNode( NodeKind.Pose, native =>
		{
			var definition = NativeEngine.GraphTimeControlledClipNode.Add( native );
			definition.m_dataSlotIdx = (short)slot;
			definition.m_playInReverseValueNodeIdx = (short)ResolveOptional( settings.PlayInReverse );
			definition.m_timeValueNodeIdx = (short)Resolve( time );
			definition.m_sampleRootMotion = settings.SampleRootMotion;
			var nativeGraphEvents = definition.GetGraphEvents();
			foreach ( var nativeValue in events )
			{
				nativeGraphEvents.Add( native.InternID( IdString( nativeValue ) ) );
			}

			return definition;
		} );
	}

	/// <inheritdoc cref="TimeControlledClip(string, Node, TimeControlledClipSettings)"/>
	public Node TimeControlledClip( Clip animation, Node time, TimeControlledClipSettings settings = null )
	{
		ArgumentNullException.ThrowIfNull( animation );
		return TimeControlledClip( ResourceId.Get( animation ), time, settings );
	}

	/// <summary>
	/// Sample a single pose from an animation at a fixed time driven by
	/// <paramref name="time"/>, normalized 0-1 across the clip - e.g. scrubbing
	/// a pose-sheet animation where every frame is a distinct pose, like the
	/// citizen's hand poses.
	/// </summary>
	public Node Pose( string animation, Node time ) => Pose( (ResourceId)animation, time );

	internal Node Pose( ResourceId animation, Node time )
	{
		var slot = GetAnimationSlot( animation );
		Require( time, NodeKind.Float );
		// Omitting m_inputTimeRemapRange keeps the runtime default FloatRange(0,1),
		// an identity clamp for normalized time input
		return AddNode( NodeKind.Pose, native =>
		{
			var definition = NativeEngine.GraphAnimationPoseNode.Add( native );
			definition.m_dataSlotIdx = (short)slot;
			definition.m_poseTimeValueNodeIdx = (short)Resolve( time );
			return definition;
		} );
	}

	/// <summary>
	/// Sample a single pose from an animation, with full control over how the input
	/// time maps onto the clip.
	/// </summary>
	public Node Pose( string animation, PoseSettings settings ) => Pose( (ResourceId)animation, settings );

	internal Node Pose( ResourceId animation, PoseSettings settings )
	{
		ArgumentNullException.ThrowIfNull( settings );
		var slot = GetAnimationSlot( animation );
		Optional( settings.Time, NodeKind.Float );
		return AddNode( NodeKind.Pose, native =>
		{
			var definition = NativeEngine.GraphAnimationPoseNode.Add( native );
			definition.m_poseTimeValueNodeIdx = (short)ResolveOptional( settings.Time );
			definition.m_dataSlotIdx = (short)slot;
			var nativeInputTimeRemapRange = definition.GetInputTimeRemapRange();
			nativeInputTimeRemapRange.m_begin = settings.RemapRangeStart;
			nativeInputTimeRemapRange.m_end = settings.RemapRangeEnd;
			definition.m_userSpecifiedTime = settings.FixedTime;
			definition.m_useFramesAsInput = settings.UseFramesAsInput;
			return definition;
		} );
	}

	/// <inheritdoc cref="Pose(string, Node)"/>
	public Node Pose( Clip animation, Node time )
	{
		ArgumentNullException.ThrowIfNull( animation );
		return Pose( ResourceId.Get( animation ), time );
	}

	/// <inheritdoc cref="Pose(string, PoseSettings)"/>
	public Node Pose( Clip animation, PoseSettings settings )
	{
		ArgumentNullException.ThrowIfNull( animation );
		return Pose( ResourceId.Get( animation ), settings );
	}

	/// <summary>
	/// A pose with every bone at identity - the "zero" pose, useful as an additive base.
	/// </summary>
	public Node ZeroPose()
	{
		return AddNode( NodeKind.Pose, native =>
		{
			var definition = NativeEngine.GraphZeroPoseNode.Add( native );
			return definition;
		} );
	}

	/// <summary>
	/// The skeleton's reference (bind) pose.
	/// </summary>
	public Node ReferencePose()
	{
		return AddNode( NodeKind.Pose, native =>
		{
			var definition = NativeEngine.GraphReferencePoseNode.Add( native );
			return definition;
		} );
	}

	/// <summary>
	/// Blend between two nodes. A weight of 0 plays <paramref name="source"/>, 1 plays <paramref name="target"/>.
	/// </summary>
	public Node Blend( Node source, Node target, Node weight ) => BlendSpace( weight ).Add( 0.0f, source ).Add( 1.0f, target );
	/// <inheritdoc cref="Blend(Node, Node, Node)"/>
	public Node Blend( Node source, Node target, float weight ) => Blend( source, target, Constant( weight ) );
	/// <summary>
	/// Select between pose nodes by condition, evaluated once when the node starts
	/// playing - e.g. on state entry. Plays the first option whose condition is
	/// true, falling back to the first option.
	/// </summary>
	public SelectorBuilder Selector()
	{
		var selector = new SelectorBuilder( this );
		selector.Node = AddNode( NodeKind.Pose, native =>
		{
			if ( selector.Options.Count == 0 )
			{
				throw new InvalidOperationException( "Selector needs at least one option - add options with Add(option, condition)." );
			}

			var definition = NativeEngine.GraphSelectorNode.Add( native );
			var nativeOptionNodeIndices = definition.GetOptionNodeIndices();
			foreach ( var o in selector.Options )
			{
				nativeOptionNodeIndices.Add( Resolve( o ) );
			}

			var nativeConditionNodeIndices = definition.GetConditionNodeIndices();
			foreach ( var c in selector.Conditions )
			{
				nativeConditionNodeIndices.Add( Resolve( c ) );
			}

			return definition;
		} );
		return selector;
	}

	/// <summary>
	/// A selector being built, created by <see cref="Selector"/>. Add options with
	/// their conditions, then use it anywhere a <see cref="Node"/> is expected.
	/// </summary>
	public sealed class SelectorBuilder
	{
		internal Node Node;
		internal readonly List<Node> Options = new();
		internal readonly List<Node> Conditions = new();
		private readonly GraphBuilder builder;
		internal SelectorBuilder( GraphBuilder builder ) => this.builder = builder;
		/// <summary>
		/// Play <paramref name="option"/> when <paramref name="condition"/> is true.
		/// Options are checked in the order added; the first added is the fallback.
		/// </summary>
		public SelectorBuilder Add( Node option, Node condition )
		{
			builder.RequirePose( option );
			builder.Require( condition, NodeKind.Bool );
			Options.Add( option );
			Conditions.Add( condition );
			return this;
		}

		public static implicit operator Node( SelectorBuilder selector ) => selector.Node;
	}

	/// <summary>
	/// Select between animation clips by condition, evaluated once when the node starts.
	/// Like <see cref="Selector"/> but restricted to clips so it can be fed to clip-only
	/// consumers such as warps. Plays the first option whose condition is true.
	/// </summary>
	public Node ClipSelector( params (Node Clip, Node Condition)[] cases )
	{
		if ( cases is null || cases.Length == 0 )
			throw new ArgumentException( "ClipSelector needs at least one case.", nameof( cases ) );
		cases = cases.ToArray();
		foreach ( var c in cases )
		{
			RequireClip( c.Clip, nameof( cases ) );
			Require( c.Condition, NodeKind.Bool, nameof( cases ) );
		}

		return AddNode( NodeKind.Pose, native =>
		{
			var definition = NativeEngine.GraphClipSelectorNode.Add( native );
			var nativeOptionNodeIndices = definition.GetOptionNodeIndices();
			foreach ( var c in cases )
			{
				nativeOptionNodeIndices.Add( Resolve( c.Clip ) );
			}

			var nativeConditionNodeIndices = definition.GetConditionNodeIndices();
			foreach ( var c in cases )
			{
				nativeConditionNodeIndices.Add( Resolve( c.Condition ) );
			}

			return definition;
		}, isClip: true );
	}

	/// <summary>
	/// Pick one of several pose nodes by weighted selection, seeded from a float
	/// parameter and evaluated once when the node starts playing - e.g. random clip
	/// variation on state entry, driven by a parameter fed a new value each time.
	/// </summary>
	public VariationBuilder Variation( Node seed )
	{
		Require( seed, NodeKind.Float );
		var variation = new VariationBuilder( this );
		variation.Node = AddNode( NodeKind.Pose, native =>
		{
			if ( variation.Options.Count == 0 )
			{
				throw new InvalidOperationException( "Variation needs at least one option - add options with Add(option, weight)." );
			}

			var definition = NativeEngine.GraphParameterizedSelectorNode.Add( native );
			var nativeOptionNodeIndices = definition.GetOptionNodeIndices();
			foreach ( var o in variation.Options )
			{
				nativeOptionNodeIndices.Add( Resolve( o ) );
			}

			var nativeOptionWeights = definition.GetOptionWeights();
			foreach ( var w in variation.Weights )
			{
				nativeOptionWeights.Add( w );
			}

			definition.m_parameterNodeIdx = (short)Resolve( seed );
			definition.m_ignoreInvalidOptions = false;
			definition.m_hasWeightsSet = true;
			return definition;
		} );
		return variation;
	}

	/// <summary>
	/// A weighted variation set being built, created by <see cref="Variation"/>. Add
	/// options with weights, then use it anywhere a <see cref="Node"/> is expected.
	/// </summary>
	public sealed class VariationBuilder
	{
		internal Node Node;
		internal readonly List<Node> Options = new();
		internal readonly List<int> Weights = new();
		private readonly GraphBuilder builder;
		internal VariationBuilder( GraphBuilder builder ) => this.builder = builder;
		/// <summary>
		/// Add an option. Higher <paramref name="weight"/> makes it proportionally
		/// more likely to be picked.
		/// </summary>
		public VariationBuilder Add( Node option, int weight = 1 )
		{
			builder.RequirePose( option );
			ArgumentOutOfRangeException.ThrowIfNegative( weight );
			ArgumentOutOfRangeException.ThrowIfGreaterThan( weight, 255 );
			Options.Add( option );
			Weights.Add( weight );
			return this;
		}

		public static implicit operator Node( VariationBuilder variation ) => variation.Node;
	}

	/// <summary>
	/// Weighted random selection restricted to clips, seeded from a float parameter.
	/// Like <see cref="Variation"/> but the result can feed clip-only consumers.
	/// </summary>
	public Node ClipVariation( Node seed, params (Node Clip, int Weight)[] options )
	{
		Require( seed, NodeKind.Float );
		if ( options is null || options.Length == 0 )
			throw new ArgumentException( "ClipVariation needs at least one option.", nameof( options ) );
		options = options.ToArray();
		foreach ( var o in options )
		{
			RequireClip( o.Clip, nameof( options ) );
			ArgumentOutOfRangeException.ThrowIfNegative( o.Weight, nameof( options ) );
			ArgumentOutOfRangeException.ThrowIfGreaterThan( o.Weight, 255, nameof( options ) );
		}

		return AddNode( NodeKind.Pose, native =>
		{
			var definition = NativeEngine.GraphParameterizedClipSelectorNode.Add( native );
			var nativeOptionNodeIndices = definition.GetOptionNodeIndices();
			foreach ( var o in options )
			{
				nativeOptionNodeIndices.Add( Resolve( o.Clip ) );
			}

			var nativeOptionWeights = definition.GetOptionWeights();
			foreach ( var o in options )
			{
				nativeOptionWeights.Add( o.Weight );
			}

			definition.m_parameterNodeIdx = (short)Resolve( seed );
			definition.m_ignoreInvalidOptions = false;
			definition.m_hasWeightsSet = true;
			return definition;
		}, isClip: true );
	}

	public Node ParameterizedSelector( Node parameter, IReadOnlyList<Node> options, ParameterizedSelectorSettings settings )
	{
		ArgumentNullException.ThrowIfNull( settings );
		Require( parameter, NodeKind.Float );
		options = options.ToArray();
		var weights = settings.Weights?.ToArray() ?? [];
		if ( settings.HasWeights && weights.Length != options.Count )
			throw new ArgumentException( "ParameterizedSelector needs one weight per option.", nameof( settings ) );
		foreach ( var option in options )
		{
			if ( settings.ClipsOnly )
				RequireClip( option, nameof( options ) );
			else
				RequirePose( option, nameof( options ) );
		}
		return AddNode( NodeKind.Pose, native =>
		{
			if ( settings.ClipsOnly )
			{
				var definition = NativeEngine.GraphParameterizedClipSelectorNode.Add( native );
				var nativeOptionNodeIndices = definition.GetOptionNodeIndices();
				foreach ( var option in options )
				{
					nativeOptionNodeIndices.Add( Resolve( option ) );
				}

				var nativeOptionWeights = definition.GetOptionWeights();
				foreach ( var weight in weights )
				{
					nativeOptionWeights.Add( weight );
				}

				definition.m_parameterNodeIdx = (short)Resolve( parameter );
				definition.m_ignoreInvalidOptions = settings.IgnoreInvalidOptions;
				definition.m_hasWeightsSet = settings.HasWeights;
				return definition;
			}
			else
			{
				var definition = NativeEngine.GraphParameterizedSelectorNode.Add( native );
				var nativeOptionNodeIndices = definition.GetOptionNodeIndices();
				foreach ( var option in options )
				{
					nativeOptionNodeIndices.Add( Resolve( option ) );
				}

				var nativeOptionWeights = definition.GetOptionWeights();
				foreach ( var weight in weights )
				{
					nativeOptionWeights.Add( weight );
				}

				definition.m_parameterNodeIdx = (short)Resolve( parameter );
				definition.m_ignoreInvalidOptions = settings.IgnoreInvalidOptions;
				definition.m_hasWeightsSet = settings.HasWeights;
				return definition;
			}
		}, isClip: settings.ClipsOnly );
	}

	/// <summary>
	/// Pick a pose whose ID matches the value of <paramref name="id"/>, falling back to
	/// <paramref name="fallback"/> (or the first option) when nothing matches.
	/// </summary>
	public Node IdBasedSelector( Node id, IEnumerable<(string Id, Node Option)> options, Node fallback = default ) => AddIdBasedSelector( id, options, fallback, clipsOnly: false );
	/// <summary>
	/// Like <c>IdBasedSelector</c> but restricted to clips so the result can feed
	/// clip-only consumers.
	/// </summary>
	public Node IdBasedClipSelector( Node id, IEnumerable<(string Id, Node Clip)> options, Node fallback = default ) => AddIdBasedSelector( id, options, fallback, clipsOnly: true );
	private Node AddIdBasedSelector( Node id, IEnumerable<(string Id, Node Option)> options, Node fallback, bool clipsOnly )
	{
		Require( id, NodeKind.Id );
		Optional( fallback, NodeKind.Pose );
		var list = options?.ToArray() ?? [];
		if ( list.Length == 0 )
			throw new ArgumentException( "An ID-based selector needs at least one option.", nameof( options ) );
		foreach ( var o in list )
		{
			if ( clipsOnly )
				RequireClip( o.Option, nameof( options ) );
			else
				RequirePose( o.Option, nameof( options ) );
		}

		if ( fallback is not null && clipsOnly )
			RequireClip( fallback );
		return AddNode( NodeKind.Pose, native =>
		{
			if ( clipsOnly )
			{
				var definition = NativeEngine.GraphIDBasedClipSelectorNode.Add( native );
				var nativeOptionNodeIndices = definition.GetOptionNodeIndices();
				foreach ( var o in list )
				{
					nativeOptionNodeIndices.Add( Resolve( o.Option ) );
				}

				var nativeOptionIDs = definition.GetOptionIDs();
				foreach ( var o in list )
				{
					nativeOptionIDs.Add( native.InternID( IdString( o.Id ) ) );
				}

				definition.m_parameterNodeIdx = (short)Resolve( id );
				definition.m_fallbackNodeIdx = (short)ResolveOptional( fallback );
				definition.m_ignoreInvalidOptions = false;
				return definition;
			}
			else
			{
				var definition = NativeEngine.GraphIDBasedSelectorNode.Add( native );
				var nativeOptionNodeIndices = definition.GetOptionNodeIndices();
				foreach ( var o in list )
				{
					nativeOptionNodeIndices.Add( Resolve( o.Option ) );
				}

				var nativeOptionIDs = definition.GetOptionIDs();
				foreach ( var o in list )
				{
					nativeOptionIDs.Add( native.InternID( IdString( o.Id ) ) );
				}

				definition.m_parameterNodeIdx = (short)Resolve( id );
				definition.m_fallbackNodeIdx = (short)ResolveOptional( fallback );
				definition.m_ignoreInvalidOptions = false;
				return definition;
			}
		}, isClip: clipsOnly );
	}

	public sealed class IdBasedSelectorSettings
	{
		public Node Fallback { get; init; }
		public bool IgnoreInvalidOptions { get; init; }
		public bool ClipsOnly { get; init; }
	}

	public Node IdBasedSelector( Node id, IReadOnlyList<(string Id, Node Option)> options, IdBasedSelectorSettings settings = null )
	{
		settings ??= new();
		Require( id, NodeKind.Id );
		Optional( settings.Fallback, NodeKind.Pose );
		if ( settings.Fallback is not null && settings.ClipsOnly )
			RequireClip( settings.Fallback );
		options = options.ToArray();
		foreach ( var option in options )
		{
			if ( settings.ClipsOnly )
				RequireClip( option.Option, nameof( options ) );
			else
				RequirePose( option.Option, nameof( options ) );
		}
		return AddNode( NodeKind.Pose, native =>
		{
			if ( settings.ClipsOnly )
			{
				var definition = NativeEngine.GraphIDBasedClipSelectorNode.Add( native );
				var nativeOptionNodeIndices = definition.GetOptionNodeIndices();
				foreach ( var option in options )
				{
					nativeOptionNodeIndices.Add( Resolve( option.Option ) );
				}

				var nativeOptionIDs = definition.GetOptionIDs();
				foreach ( var option in options )
				{
					nativeOptionIDs.Add( native.InternID( IdString( option.Id ) ) );
				}

				definition.m_parameterNodeIdx = (short)Resolve( id );
				definition.m_fallbackNodeIdx = (short)ResolveOptional( settings.Fallback );
				definition.m_ignoreInvalidOptions = settings.IgnoreInvalidOptions;
				return definition;
			}
			else
			{
				var definition = NativeEngine.GraphIDBasedSelectorNode.Add( native );
				var nativeOptionNodeIndices = definition.GetOptionNodeIndices();
				foreach ( var option in options )
				{
					nativeOptionNodeIndices.Add( Resolve( option.Option ) );
				}

				var nativeOptionIDs = definition.GetOptionIDs();
				foreach ( var option in options )
				{
					nativeOptionIDs.Add( native.InternID( IdString( option.Id ) ) );
				}

				definition.m_parameterNodeIdx = (short)Resolve( id );
				definition.m_fallbackNodeIdx = (short)ResolveOptional( settings.Fallback );
				definition.m_ignoreInvalidOptions = settings.IgnoreInvalidOptions;
				return definition;
			}
		}, isClip: settings.ClipsOnly );
	}

	/// <summary>
	/// Pick the clip whose baked root motion best reaches <paramref name="target"/>,
	/// scoring options by end position and orientation - e.g. choosing a dodge or
	/// vault that lands closest to a marker.
	/// </summary>
	/// <param name="target">Target node providing the transform to reach.</param>
	/// <param name="clips">Candidate clips to score.</param>
	/// <param name="settings">Scoring and target-space options.</param>
	public Node TargetSelector( Node target, IEnumerable<Node> clips, TargetSelectorSettings settings = null )
	{
		settings ??= new();
		Require( target, NodeKind.Target );
		var list = clips?.ToArray() ?? [];
		if ( list.Length == 0 )
			throw new ArgumentException( "TargetSelector needs at least one clip.", nameof( clips ) );
		foreach ( var c in list )
			RequireClip( c, nameof( clips ) );
		return AddNode( NodeKind.Pose, native =>
		{
			var definition = NativeEngine.GraphTargetSelectorNode.Add( native );
			var nativeOptionNodeIndices = definition.GetOptionNodeIndices();
			foreach ( var c in list )
			{
				nativeOptionNodeIndices.Add( Resolve( c ) );
			}

			definition.m_orientationScoreWeight = settings.OrientationWeight;
			definition.m_positionScoreWeight = settings.PositionWeight;
			definition.m_parameterNodeIdx = (short)Resolve( target );
			definition.m_ignoreInvalidOptions = settings.IgnoreInvalidOptions;
			definition.m_isWorldSpaceTarget = settings.WorldSpace;
			definition.SetAlignmentBoneID( native.InternID( IdString( settings.AlignmentBone ) ) );
			return definition;
		}, isClip: true );
	}

	/// <summary>
	/// Blend between any number of nodes placed along a parameter axis - a 1D blend space.
	/// The parameter value picks the two neighbouring entries and blends between them,
	/// e.g. idle at 0, walk at 100, run at 300, driven by movement speed.
	/// </summary>
	public BlendSpaceBuilder BlendSpace( Node parameter, bool looping = true )
	{
		Require( parameter, NodeKind.Float );
		var blendSpace = new BlendSpaceBuilder( this );
		blendSpace.Node = AddNode( NodeKind.Pose, native =>
		{
			if ( blendSpace.Sources.Count == 0 )
				throw new InvalidOperationException( "BlendSpace needs at least one entry - add entries with Add(position, source)." );
			var order = Enumerable.Range( 0, blendSpace.Positions.Count ).OrderBy( i => blendSpace.Positions[i] ).ToArray();
			var definition = NativeEngine.GraphBlend1DNode.Add( native );
			var sources = definition.GetSourceNodeIndices();
			foreach ( var index in order )
				sources.Add( Resolve( blendSpace.Sources[index] ) );
			var ranges = definition.GetBlendRanges();
			for ( var i = 0; i < order.Length - 1; i++ )
			{
				var range = ranges.Add();
				range.m_inputIdx0 = (short)i;
				range.m_inputIdx1 = (short)(i + 1);
				range.m_paramRangeStart = blendSpace.Positions[order[i]];
				range.m_paramRangeEnd = blendSpace.Positions[order[i + 1]];
			}

			definition.m_parameterValueNodeIdx = (short)Resolve( parameter );
			definition.m_allowLooping = looping;
			return definition;
		} );
		return blendSpace;
	}

	public Node BlendSpace( Node parameter, IReadOnlyList<Node> sources, IReadOnlyList<(int Input0, int Input1, float Start, float End)> ranges, bool looping )
	{
		Require( parameter, NodeKind.Float );
		sources = sources.ToArray();
		ranges = ranges.ToArray();
		foreach ( var source in sources )
			RequirePose( source, nameof( sources ) );
		foreach ( var range in ranges )
		{
			if ( range.Input0 < 0 || range.Input0 >= sources.Count || range.Input1 < 0 || range.Input1 >= sources.Count )
				throw new ArgumentOutOfRangeException( nameof( ranges ), "Blend range inputs must index into sources." );
		}
		return AddNode( NodeKind.Pose, native =>
		{
			var definition = NativeEngine.GraphBlend1DNode.Add( native );
			var nativeSourceNodeIndices = definition.GetSourceNodeIndices();
			foreach ( var source in sources )
			{
				nativeSourceNodeIndices.Add( Resolve( source ) );
			}

			definition.m_parameterValueNodeIdx = (short)Resolve( parameter );
			var nativeBlendRanges = definition.GetBlendRanges();
			foreach ( var range in ranges )
			{
				var nativeItem = nativeBlendRanges.Add();
				nativeItem.m_inputIdx0 = (short)range.Input0;
				nativeItem.m_inputIdx1 = (short)range.Input1;
				nativeItem.m_paramRangeStart = range.Start;
				nativeItem.m_paramRangeEnd = range.End;
			}

			definition.m_allowLooping = looping;
			return definition;
		} );
	}

	/// <summary>
	/// A 1D blend space being built, created by <see cref="BlendSpace(Node, bool)"/>. Add entries
	/// along the parameter axis, then use it anywhere a <see cref="Node"/> is expected.
	/// </summary>
	public sealed class BlendSpaceBuilder
	{
		internal Node Node;
		internal readonly List<float> Positions = new();
		internal readonly List<Node> Sources = new();
		private readonly GraphBuilder builder;
		internal BlendSpaceBuilder( GraphBuilder builder ) => this.builder = builder;
		/// <summary>
		/// Play <paramref name="source"/> when the parameter is at <paramref name="position"/>.
		/// </summary>
		public BlendSpaceBuilder Add( float position, Node source )
		{
			builder.RequirePose( source );
			Positions.Add( position );
			Sources.Add( source );
			return this;
		}

		public static implicit operator Node( BlendSpaceBuilder blendSpace ) => blendSpace.Node;
	}

	/// <summary>
	/// Play an animation scaled to move at a desired velocity, blending several clips by
	/// the speeds baked into their root motion - the runtime derives each clip's position
	/// on the axis from its average velocity.
	/// </summary>
	/// <param name="speed">Float node giving the desired movement speed.</param>
	/// <param name="clips">Clips to blend, ordered from slowest to fastest.</param>
	public Node VelocityBlend( Node speed, params Node[] clips )
	{
		Require( speed, NodeKind.Float );
		if ( clips is null || clips.Length == 0 )
			throw new ArgumentException( "VelocityBlend needs at least one clip.", nameof( clips ) );
		clips = clips.ToArray();
		foreach ( var c in clips )
			RequireClip( c, nameof( clips ) );
		return AddNode( NodeKind.Pose, native =>
		{
			var definition = NativeEngine.GraphVelocityBlendNode.Add( native );
			var nativeSourceNodeIndices = definition.GetSourceNodeIndices();
			foreach ( var c in clips )
			{
				nativeSourceNodeIndices.Add( Resolve( c ) );
			}

			definition.m_parameterValueNodeIdx = (short)Resolve( speed );
			return definition;
		} );
	}

	/// <summary>
	/// Scale the playback speed of a node.
	/// </summary>
	public Node SpeedScale( Node child, Node speed )
	{
		RequirePose( child );
		Require( speed, NodeKind.Float );
		return AddNode( NodeKind.Pose, native =>
		{
			var definition = NativeEngine.GraphSpeedScaleNode.Add( native );
			definition.m_childNodeIdx = (short)Resolve( child );
			definition.m_inputValueNodeIdx = (short)Resolve( speed );
			return definition;
		} );
	}

	/// <inheritdoc cref="SpeedScale(Node, Node)"/>
	public Node SpeedScale( Node child, float speed ) => SpeedScale( child, Constant( speed ) );
	/// <summary>
	/// Scale the playback speed of a node so it plays over a desired duration,
	/// e.g. stretching a reload animation to match the weapon's reload time.
	/// </summary>
	public Node DurationScale( Node child, Node duration )
	{
		RequirePose( child );
		Require( duration, NodeKind.Float );
		return AddNode( NodeKind.Pose, native =>
		{
			var definition = NativeEngine.GraphDurationScaleNode.Add( native );
			definition.m_childNodeIdx = (short)Resolve( child );
			definition.m_inputValueNodeIdx = (short)Resolve( duration );
			return definition;
		} );
	}

	/// <inheritdoc cref="DurationScale(Node, Node)"/>
	public Node DurationScale( Node child, float duration ) => DurationScale( child, Constant( duration ) );
	public Node DurationScale( Node child, Node duration, float defaultDuration )
	{
		RequirePose( child );
		Optional( duration, NodeKind.Float );
		return AddNode( NodeKind.Pose, native =>
		{
			var definition = NativeEngine.GraphDurationScaleNode.Add( native );
			definition.m_childNodeIdx = (short)Resolve( child );
			definition.m_inputValueNodeIdx = (short)ResolveOptional( duration );
			definition.m_defaultInputValue = defaultDuration;
			return definition;
		} );
	}

	/// <summary>
	/// Play an animation scaled to move at a desired velocity, using the average
	/// root motion velocity baked into the clip - e.g. matching a walk cycle's
	/// playback to the actual movement speed so the feet never slide.
	/// </summary>
	public Node VelocityScale( string animation, Node desiredVelocity, bool looping = true )
	{
		Require( desiredVelocity, NodeKind.Float );
		var clip = Clip( animation, new ClipSettings { Looping = looping } );
		return AddNode( NodeKind.Pose, native =>
		{
			var definition = NativeEngine.GraphVelocityBasedSpeedScaleNode.Add( native );
			definition.m_childNodeIdx = (short)Resolve( clip );
			definition.m_inputValueNodeIdx = (short)Resolve( desiredVelocity );
			return definition;
		} );
	}

	public Node VelocityScale( Node clip, Node desiredVelocity, float defaultVelocity )
	{
		RequireClip( clip );
		Optional( desiredVelocity, NodeKind.Float );
		return AddNode( NodeKind.Pose, native =>
		{
			var definition = NativeEngine.GraphVelocityBasedSpeedScaleNode.Add( native );
			definition.m_childNodeIdx = (short)Resolve( clip );
			definition.m_inputValueNodeIdx = (short)ResolveOptional( desiredVelocity );
			definition.m_defaultInputValue = defaultVelocity;
			return definition;
		} );
	}

	/// <summary>
	/// Blend between nodes placed on a 2D parameter plane - a 2D blend space, e.g.
	/// directional locomotion clips positioned by the velocity they're authored at.
	/// The points are triangulated at build time; the parameter picks the enclosing
	/// triangle and blends its three corners.
	/// </summary>
	public BlendSpace2DBuilder BlendSpace2D( Node parameterX, Node parameterY, bool looping = true )
	{
		Require( parameterX, NodeKind.Float );
		Require( parameterY, NodeKind.Float );
		var blendSpace = new BlendSpace2DBuilder( this );
		blendSpace.Node = AddNode( NodeKind.Pose, native =>
		{
			if ( blendSpace.Sources.Count < 3 )
			{
				throw new InvalidOperationException( "A 2D blend space needs at least 3 entries to triangulate." );
			}

			if ( !Delaunay.Triangulate( blendSpace.Positions, out var indices, out var hullIndices ) )
			{
				throw new InvalidOperationException( "2D blend space couldn't triangulate - needs at least 3 points, not collinear or duplicated." );
			}

			var definition = NativeEngine.GraphBlend2DNode.Add( native );
			var nativeSourceNodeIndices = definition.GetSourceNodeIndices();
			foreach ( var s in blendSpace.Sources )
			{
				nativeSourceNodeIndices.Add( Resolve( s ) );
			}

			definition.m_inputParameterNodeIdx0 = (short)Resolve( parameterX );
			definition.m_inputParameterNodeIdx1 = (short)Resolve( parameterY );
			var nativeValues = definition.GetValues();
			foreach ( var p in blendSpace.Positions )
			{
				nativeValues.Add( new Vector2( p.x, p.y ) );
			}

			var nativeIndices = definition.GetIndices();
			foreach ( var i in indices )
			{
				nativeIndices.Add( i );
			}

			var nativeHullIndices = definition.GetHullIndices();
			foreach ( var i in hullIndices )
			{
				nativeHullIndices.Add( i );
			}

			definition.m_allowLooping = looping;
			return definition;
		} );
		return blendSpace;
	}

	public Node BlendSpace2D( Node parameterX, Node parameterY, BlendSpace2DSettings settings )
	{
		ArgumentNullException.ThrowIfNull( settings );
		Require( parameterX, NodeKind.Float );
		Require( parameterY, NodeKind.Float );
		var sources = settings.Sources.ToArray();
		var positions = settings.Positions.ToArray();
		var indices = settings.Indices.ToArray();
		var hullIndices = settings.HullIndices.ToArray();
		foreach ( var source in sources )
			RequirePose( source, nameof( settings ) );
		if ( positions.Length != sources.Length || indices.Length % 3 != 0 )
			throw new ArgumentException( "BlendSpace2D needs one position per source and whole triangles.", nameof( settings ) );
		foreach ( var index in indices.Concat( hullIndices ) )
		{
			if ( index < 0 || index >= sources.Length )
				throw new ArgumentOutOfRangeException( nameof( settings ), "BlendSpace2D indices must index into sources." );
		}
		return AddNode( NodeKind.Pose, native =>
		{
			var definition = NativeEngine.GraphBlend2DNode.Add( native );
			var nativeSourceNodeIndices = definition.GetSourceNodeIndices();
			foreach ( var source in sources )
			{
				nativeSourceNodeIndices.Add( Resolve( source ) );
			}

			definition.m_inputParameterNodeIdx0 = (short)Resolve( parameterX );
			definition.m_inputParameterNodeIdx1 = (short)Resolve( parameterY );
			var nativeValues = definition.GetValues();
			foreach ( var position in positions )
			{
				nativeValues.Add( new Vector2( position.x, position.y ) );
			}

			var nativeIndices = definition.GetIndices();
			foreach ( var index in indices )
			{
				nativeIndices.Add( index );
			}

			var nativeHullIndices = definition.GetHullIndices();
			foreach ( var index in hullIndices )
			{
				nativeHullIndices.Add( index );
			}

			definition.m_allowLooping = settings.Looping;
			return definition;
		} );
	}

	/// <summary>
	/// A 2D blend space being built, created by <see cref="BlendSpace2D(Node, Node, bool)"/>. Add entries
	/// on the parameter plane, then use it anywhere a <see cref="Node"/> is expected.
	/// </summary>
	public sealed class BlendSpace2DBuilder
	{
		internal Node Node;
		internal readonly List<Vector2> Positions = new();
		internal readonly List<Node> Sources = new();
		private readonly GraphBuilder builder;
		internal BlendSpace2DBuilder( GraphBuilder builder ) => this.builder = builder;
		/// <summary>
		/// Play <paramref name="source"/> when the parameters are at <paramref name="position"/>.
		/// </summary>
		public BlendSpace2DBuilder Add( Vector2 position, Node source )
		{
			builder.RequirePose( source );
			if ( Sources.Count >= 256 )
				throw new InvalidOperationException( "BlendSpace2D supports at most 256 entries." );
			Positions.Add( position );
			Sources.Add( source );
			return this;
		}

		public static implicit operator Node( BlendSpace2DBuilder blendSpace ) => blendSpace.Node;
	}

	/// <summary>
	/// Stack animation layers on top of a base pose, each optionally weighted and
	/// masked to part of the skeleton - e.g. an upper-body action layered over locomotion.
	/// Add layers, then use it anywhere a <see cref="Node"/> is expected.
	/// </summary>
	public LayersBuilder Layers( Node baseNode )
	{
		RequirePose( baseNode );
		var layers = new LayersBuilder( this );
		layers.Node = AddNode( NodeKind.Pose, native =>
		{
			var definition = NativeEngine.GraphLayerBlendNode.Add( native );
			var layerDefinitions = definition.GetLayerDefinitions();
			foreach ( var layer in layers.LayerList )
			{
				var target = layerDefinitions.Add();
				target.m_inputNodeIdx = (short)Resolve( layer.Input );
				target.m_weightValueNodeIdx = (short)ResolveOptional( layer.Weight );
				target.m_boneMaskValueNodeIdx = (short)ResolveOptional( layer.BoneMask );
				target.m_rootMotionWeightValueNodeIdx = (short)ResolveOptional( layer.RootMotionWeight );
				target.SetBlendMode( (int)layer.BlendMode );
				target.m_isSynchronized = layer.Synchronized;
				target.m_ignoreEvents = layer.IgnoreEvents;
				target.m_isStateMachineLayer = layer.IsStateMachine;
			}

			definition.m_baseNodeIdx = (short)Resolve( baseNode );
			definition.m_onlySampleBaseRootMotion = layers.OnlySampleBaseRootMotion;
			return definition;
		} );
		return layers;
	}

	/// <summary>
	/// How a layer's pose is combined with the pose below it.
	/// </summary>
	[Expose]
	public enum LayerBlendMode
	{
		/// <summary>Cross-fade the layer over the base by the layer weight.</summary>
		Overlay = 0,
		/// <summary>Add the layer's pose (authored as a delta) on top of the base.</summary>
		Additive = 1,
		/// <summary>Blend model-space rotations and local positions/scales using Esoterica's layer blending.</summary>
		ModelSpace = 2,
		/// <summary>Blend model-space rotations root-to-tip, preserving local positions and scale, as in Source 2.</summary>
		ModelSpaceRotationOnly = 3,
		/// <summary>Apply model-space additive deltas using the base pose's original parent orientations.</summary>
		ModelSpaceAdditive = 4,
		ModelSpaceHierarchical = 5,
	}

	/// <summary>
	/// A layer stack being built, created by <see cref="Layers"/>. Layers apply in the
	/// order added, each blending over the result of the ones below it.
	/// </summary>
	public sealed class LayersBuilder
	{
		internal Node Node;
		internal readonly List<(Node Input, Node Weight, Node BoneMask, Node RootMotionWeight, LayerBlendMode BlendMode, bool Synchronized, bool IgnoreEvents, bool IsStateMachine)> LayerList = new();
		internal bool OnlySampleBaseRootMotion = true;
		private readonly GraphBuilder builder;
		internal LayersBuilder( GraphBuilder builder ) => this.builder = builder;
		/// <summary>
		/// Add a layer playing <paramref name="input"/>.
		/// </summary>
		/// <param name="input">The pose to layer on top.</param>
		/// <param name="settings">Layer blending, masking, and synchronization options.</param>
		public LayersBuilder AddLayer( Node input, LayerSettings settings = null )
		{
			settings ??= new();
			builder.RequirePose( input );
			builder.Optional( settings.Weight, NodeKind.Float );
			builder.Optional( settings.BoneMask, NodeKind.BoneMask );
			builder.Optional( settings.RootMotionWeight, NodeKind.Float );
			if ( settings.RootMotionWeight is not null )
				OnlySampleBaseRootMotion = false;
			LayerList.Add( (input, settings.Weight, settings.BoneMask, settings.RootMotionWeight, settings.BlendMode, settings.Synchronized, settings.IgnoreEvents, false) );
			return this;
		}

		/// <summary>
		/// Add a layer driven by a state machine. The weight and bone mask are carried by the
		/// machine's states so its transitions blend layer settings smoothly, and the machine
		/// keeps updating while the layer weight is zero so transitions always complete.
		/// </summary>
		/// <param name="input">The state machine to layer on top.</param>
		/// <param name="settings">Layer blending, masking, and synchronization options.</param>
		public LayersBuilder AddLayer( StateMachineBuilder input, LayerSettings settings = null )
		{
			settings ??= new();
			builder.Optional( settings.Weight, NodeKind.Float );
			builder.Optional( settings.BoneMask, NodeKind.BoneMask );
			builder.Optional( settings.RootMotionWeight, NodeKind.Float );
			input.LayerWeight = settings.Weight;
			input.LayerBoneMask = settings.BoneMask;
			input.LayerRootMotionWeight = settings.RootMotionWeight;
			if ( settings.RootMotionWeight is not null )
				OnlySampleBaseRootMotion = false;
			LayerList.Add( (input.Build(), default, default, default, settings.BlendMode, settings.Synchronized, settings.IgnoreEvents, true) );
			return this;
		}

		public int AddStateMachineLayer( Node input, LayerSettings settings = null )
		{
			settings ??= new();
			builder.RequirePose( input );
			LayerList.Add( (input, default, default, default, settings.BlendMode, settings.Synchronized, settings.IgnoreEvents, true) );
			return LayerList.Count - 1;
		}

		/// <summary>
		/// Include root motion sampled by layers instead of sampling it only from the base pose.
		/// Supplying a root-motion weight to <see cref="AddLayer(Node, LayerSettings)"/>
		/// enables this automatically.
		/// </summary>
		public LayersBuilder IncludeLayerRootMotion( bool include = true )
		{
			OnlySampleBaseRootMotion = !include;
			return this;
		}

		public static implicit operator Node( LayersBuilder layers ) => layers.Node;
	}

	/// <summary>
	/// Pass a child pose through unchanged. Occasionally useful as an explicit graph node.
	/// </summary>
	public Node Passthrough( Node child )
	{
		RequirePose( child );
		return AddNode( NodeKind.Pose, native =>
		{
			var definition = NativeEngine.GraphPassthroughNode.Add( native );
			definition.m_childNodeIdx = (short)Resolve( child );
			return definition;
		} );
	}

	/// <summary>
	/// Apply a bone mask as a per-bone scale on top of a child pose - the mask weight
	/// becomes the scale factor for each bone.
	/// </summary>
	/// <param name="child">Pose to scale.</param>
	/// <param name="boneMask">Bone mask node providing per-bone scale weights.</param>
	/// <param name="enabled">Optional bool node toggling the scale, defaults to always on.</param>
	public Node Scale( Node child, Node boneMask, Node enabled = default )
	{
		RequirePose( child );
		Require( boneMask, NodeKind.BoneMask );
		Optional( enabled, NodeKind.Bool );
		return AddNode( NodeKind.Pose, native =>
		{
			var definition = NativeEngine.GraphScaleNode.Add( native );
			definition.m_childNodeIdx = (short)Resolve( child );
			definition.m_maskNodeIdx = (short)Resolve( boneMask );
			definition.m_enableNodeIdx = (short)ResolveOptional( enabled );
			return definition;
		} );
	}

	/// <summary>
	/// Bake the triangulation a 2D blend space needs: Delaunay triangle index triplets
	/// for interior blending, plus the convex hull boundary loop (first index repeated
	/// at the end) for projecting points outside the space onto its edge.
	/// Returns false when the points can't be triangulated - fewer than 3 points,
	/// collinear, or duplicated.
	/// </summary>
	public static bool TriangulateBlendSpace( IReadOnlyList<Vector2> points, out List<int> triangles, out List<int> hull ) => Delaunay.Triangulate( points, out triangles, out hull );
	/// <summary>
	/// Bowyer-Watson Delaunay triangulation - performs the bake that Esoterica's
	/// editor tooling does when compiling a Blend2D node.
	/// </summary>
	internal static class Delaunay
	{
		private struct Triangle
		{
			public int A, B, C;
		}

		public static bool Triangulate( IReadOnlyList<Vector2> points, out List<int> triangleIndices, out List<int> hullIndices )
		{
			triangleIndices = new List<int>();
			hullIndices = new List<int>();
			if ( points.Count < 3 )
				return false;
			// Super-triangle enclosing all points
			var min = new Vector2( points.Min( p => p.x ), points.Min( p => p.y ) );
			var max = new Vector2( points.Max( p => p.x ), points.Max( p => p.y ) );
			var center = (min + max) * 0.5f;
			var radius = MathF.Max( max.x - min.x, max.y - min.y ) * 16.0f + 1.0f;
			var vertices = new List<Vector2>( points )
			{
				center + new Vector2(-radius, -radius),
				center + new Vector2(radius, -radius),
				center + new Vector2(0.0f, radius),
			};
			var triangles = new List<Triangle>
			{
				MakeCounterClockwise(vertices, points.Count, points.Count + 1, points.Count + 2)
			};
			// Insert each point, carving out the triangles whose circumcircle
			// contains it and fanning new triangles around the cavity boundary
			for ( var i = 0; i < points.Count; i++ )
			{
				var bad = triangles.Where( t => InCircumcircle( vertices, t, points[i] ) ).ToList();
				triangles.RemoveAll( t => InCircumcircle( vertices, t, points[i] ) );
				var edges = new HashSet<long>( bad.SelectMany( EdgesOf ) );
				foreach ( var edge in edges )
				{
					// Shared cavity edges appear in both directions - only the boundary survives
					if ( edges.Contains( Reverse( edge ) ) )
						continue;
					triangles.Add( new Triangle { A = StartOf( edge ), B = EndOf( edge ), C = i } );
				}
			}

			// Strip triangles touching the super-triangle
			triangles.RemoveAll( t => t.A >= points.Count || t.B >= points.Count || t.C >= points.Count );
			if ( triangles.Count == 0 )
				return false;
			foreach ( var t in triangles )
			{
				triangleIndices.Add( t.A );
				triangleIndices.Add( t.B );
				triangleIndices.Add( t.C );
			}

			// The hull is the cycle of directed edges with no opposite twin
			var boundary = triangles.SelectMany( EdgesOf ).ToHashSet();
			var successor = boundary.Where( e => !boundary.Contains( Reverse( e ) ) ).ToDictionary( StartOf, EndOf );
			var start = successor.Keys.First();
			hullIndices.Add( start );
			for ( var i = successor[start]; i != start; i = successor[i] )
			{
				hullIndices.Add( i );
			}

			// Hull has the first index duplicated at the end
			hullIndices.Add( start );
			return true;
		}

		private static IEnumerable<long> EdgesOf( Triangle t )
		{
			yield return Pack( t.A, t.B );
			yield return Pack( t.B, t.C );
			yield return Pack( t.C, t.A );
		}

		private static long Pack( int start, int end ) => ((long)start << 32) | (uint)end;
		private static long Reverse( long edge ) => Pack( EndOf( edge ), StartOf( edge ) );
		private static int StartOf( long edge ) => (int)(edge >> 32);
		private static int EndOf( long edge ) => (int)(edge & 0xFFFFFFFF);
		private static Triangle MakeCounterClockwise( List<Vector2> vertices, int a, int b, int c )
		{
			var ab = vertices[b] - vertices[a];
			var ac = vertices[c] - vertices[a];
			return ab.x * ac.y - ab.y * ac.x >= 0.0f ? new Triangle
			{
				A = a,
				B = b,
				C = c
			}

			: new Triangle
			{
				A = a,
				B = c,
				C = b
			};
		}

		private static bool InCircumcircle( List<Vector2> vertices, Triangle t, Vector2 point )
		{
			double ax = (double)vertices[t.A].x - point.x, ay = (double)vertices[t.A].y - point.y;
			double bx = (double)vertices[t.B].x - point.x, by = (double)vertices[t.B].y - point.y;
			double cx = (double)vertices[t.C].x - point.x, cy = (double)vertices[t.C].y - point.y;
			var det = (ax * ax + ay * ay) * (bx * cy - cx * by) - (bx * bx + by * by) * (ax * cy - cx * ay) + (cx * cx + cy * cy) * (ax * by - bx * ay);
			return det > 0.0;
		}
	}
}
