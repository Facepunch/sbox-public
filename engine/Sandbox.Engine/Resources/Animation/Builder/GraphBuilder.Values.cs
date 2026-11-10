namespace Sandbox.Animation;

public sealed partial class GraphBuilder
{
	private readonly Dictionary<(Node Input, string Id, bool MustMatch), Node> parameterIdComparisons = new();
	private readonly Dictionary<(string Operation, string Inputs), Node> parameterBooleanExpressions = new();
	private readonly HashSet<Node> parameterExpressions = new();
	private readonly Dictionary<bool, Node> constantBools = new();

	private Node AddBooleanExpression( string operation, Node[] inputs, Func<NativeEngine.AnimationGraphBuilder, NativeEngine.AnimationGraphNodeDefinition> create )
	{
		// Only share expressions whose leaves are parameters or constants, never stateful value nodes.
		if ( !inputs.All( node => node.Index < parameters.Count || parameterExpressions.Contains( node ) ) )
			return AddNode( NodeKind.Bool, create );

		var key = (operation, string.Join( ",", inputs.Select( node => node.Index ) ));
		if ( parameterBooleanExpressions.TryGetValue( key, out var existing ) )
			return existing;

		var result = AddNode( NodeKind.Bool, create );
		parameterBooleanExpressions.Add( key, result );
		parameterExpressions.Add( result );
		return result;
	}

	/// <summary>
	/// Add a constant float value.
	/// </summary>
	public Node Constant( float value )
	{
		return AddNode( NodeKind.Float, native =>
		{
			var definition = NativeEngine.GraphConstFloatNode.Add( native );
			definition.m_value = value;
			return definition;
		} );
	}

	/// <summary>
	/// Add a constant bool value.
	/// </summary>
	public Node Constant( bool value )
	{
		if ( constantBools.TryGetValue( value, out var existing ) )
			return existing;

		var node = AddNode( NodeKind.Bool, native =>
		{
			var definition = NativeEngine.GraphConstBoolNode.Add( native );
			definition.m_value = value;
			return definition;
		} );
		constantBools.Add( value, node );
		parameterExpressions.Add( node );
		return node;
	}

	/// <summary>
	/// Add a constant vector value.
	/// </summary>
	public Node Constant( Vector3 value )
	{
		return AddNode( NodeKind.Vector, native =>
		{
			var definition = NativeEngine.GraphConstVectorNode.Add( native );
			definition.m_value = value;
			return definition;
		} );
	}

	/// <summary>
	/// Add a constant ID (tag) value, matched by name against events, selectors and comparisons.
	/// </summary>
	public Node ConstantId( string value )
	{
		return AddNode( NodeKind.Id, native =>
		{
			var definition = NativeEngine.GraphConstIDNode.Add( native );
			definition.SetValue( native.InternID( IdString( value ) ) );
			return definition;
		} );
	}

	/// <summary>
	/// Add a constant, always-set target holding a fixed position and rotation.
	/// </summary>
	public Node ConstantTarget( Vector3 position, Rotation rotation )
	{
		return AddNode( NodeKind.Target, native =>
		{
			var definition = NativeEngine.GraphConstTargetNode.Add( native );
			definition.m_isSet = true;
			definition.m_rotation = rotation;
			definition.m_translation = position;
			return definition;
		} );
	}

	/// <inheritdoc cref="ConstantTarget(Vector3, Rotation)"/>
	public Node ConstantTarget( Transform transform ) => ConstantTarget( transform.Position, transform.Rotation );
	/// <summary>
	/// Add a constant target that refers to a character bone rather than a fixed transform.
	/// </summary>
	public Node BoneTarget( string boneName )
	{
		if ( string.IsNullOrWhiteSpace( boneName ) )
			throw new ArgumentException( "Bone name must not be null or blank.", nameof( boneName ) );
		return AddNode( NodeKind.Target, native =>
		{
			var definition = NativeEngine.GraphConstTargetNode.Add( native );
			definition.m_isSet = true;
			definition.m_isBoneTarget = true;
			definition.SetBoneID( native.InternID( IdString( boneName ) ) );
			return definition;
		} );
	}

	/// <summary>
	/// Add a constant unset target - the "no target" value, useful as a switch or selector fallback.
	/// </summary>
	public Node UnsetTarget()
	{
		return AddNode( NodeKind.Target, native =>
		{
			var definition = NativeEngine.GraphConstTargetNode.Add( native );
			definition.m_isSet = false;
			return definition;
		} );
	}

	public Node NullTarget()
	{
		return AddNode( NodeKind.Target, native =>
		{
			var definition = NativeEngine.GraphConstTargetNode.Add( native );
			return definition;
		} );
	}

	/// <summary>
	/// Invert a bool value.
	/// </summary>
	public Node Not( Node value )
	{
		Require( value, NodeKind.Bool );
		return AddBooleanExpression( nameof( Not ), [value], native =>
		{
			var definition = NativeEngine.GraphNotNode.Add( native );
			definition.m_inputValueNodeIdx = (short)Resolve( value );
			return definition;
		} );
	}

	/// <summary>
	/// True when all conditions are true.
	/// </summary>
	public Node And( params Node[] conditions )
	{
		if ( conditions is null || conditions.Length == 0 )
			throw new ArgumentException( "And needs at least one condition.", nameof( conditions ) );
		conditions = conditions.ToArray();
		foreach ( var c in conditions )
			Require( c, NodeKind.Bool, nameof( conditions ) );
		return AddBooleanExpression( nameof( And ), conditions, native =>
		{
			var definition = NativeEngine.GraphAndNode.Add( native );
			var nativeConditionNodeIndices = definition.GetConditionNodeIndices();
			foreach ( var c in conditions )
			{
				nativeConditionNodeIndices.Add( Resolve( c ) );
			}

			return definition;
		} );
	}

	/// <summary>
	/// True when any condition is true.
	/// </summary>
	public Node Or( params Node[] conditions )
	{
		if ( conditions is null || conditions.Length == 0 )
			throw new ArgumentException( "Or needs at least one condition.", nameof( conditions ) );
		conditions = conditions.ToArray();
		foreach ( var c in conditions )
			Require( c, NodeKind.Bool, nameof( conditions ) );
		return AddBooleanExpression( nameof( Or ), conditions, native =>
		{
			var definition = NativeEngine.GraphOrNode.Add( native );
			var nativeConditionNodeIndices = definition.GetConditionNodeIndices();
			foreach ( var c in conditions )
			{
				nativeConditionNodeIndices.Add( Resolve( c ) );
			}

			return definition;
		} );
	}

	/// <summary>
	/// How to compare a float against another value in <see cref="Compare(Node, FloatComparison, Node, float)"/>.
	/// </summary>
	[Expose]
	public enum FloatComparison
	{
		[Title( ">=" )]
		GreaterThanEqual,
		[Title( "<=" )]
		LessThanEqual,
		[Title( "~=" )]
		NearEqual,
		[Title( ">" )]
		GreaterThan,
		[Title( "<" )]
		LessThan,
	}

	/// <summary>
	/// True when <paramref name="input"/> compares against <paramref name="comparand"/> using <paramref name="comparison"/>.
	/// </summary>
	/// <param name="input">The float node to test.</param>
	/// <param name="comparison">The comparison operation.</param>
	/// <param name="comparand">The float node to compare against.</param>
	/// <param name="epsilon">Tolerance used by <see cref="FloatComparison.NearEqual"/>.</param>
	public Node Compare( Node input, FloatComparison comparison, Node comparand, float epsilon = 0.0f )
	{
		Require( input, NodeKind.Float );
		Require( comparand, NodeKind.Float );
		return AddNode( NodeKind.Bool, native =>
		{
			var definition = NativeEngine.GraphFloatComparisonNode.Add( native );
			definition.m_inputValueNodeIdx = (short)Resolve( input );
			definition.m_comparandValueNodeIdx = (short)Resolve( comparand );
			definition.SetComparison( (int)comparison );
			definition.m_epsilon = epsilon;
			return definition;
		} );
	}

	/// <summary>
	/// True when <paramref name="input"/> compares against <paramref name="value"/> using <paramref name="comparison"/>.
	/// </summary>
	/// <param name="input">The float node to test.</param>
	/// <param name="comparison">The comparison operation.</param>
	/// <param name="value">The constant value to compare against.</param>
	/// <param name="epsilon">Tolerance used by <see cref="FloatComparison.NearEqual"/>.</param>
	public Node Compare( Node input, FloatComparison comparison, float value, float epsilon = 0.0f )
	{
		Require( input, NodeKind.Float );
		return AddNode( NodeKind.Bool, native =>
		{
			var definition = NativeEngine.GraphFloatComparisonNode.Add( native );
			definition.m_inputValueNodeIdx = (short)Resolve( input );
			definition.m_comparandValueNodeIdx = (short)(-1);
			definition.SetComparison( (int)comparison );
			definition.m_comparisonValue = value;
			definition.m_epsilon = epsilon;
			return definition;
		} );
	}

	/// <summary>
	/// True when <paramref name="input"/> is within [<paramref name="min"/>, <paramref name="max"/>].
	/// </summary>
	public Node InRange( Node input, float min, float max, bool inclusive = true )
	{
		Require( input, NodeKind.Float );
		return AddNode( NodeKind.Bool, native =>
		{
			var definition = NativeEngine.GraphFloatRangeComparisonNode.Add( native );
			definition.m_inputValueNodeIdx = (short)Resolve( input );
			definition.m_rangeMin = min;
			definition.m_rangeMax = max;
			definition.m_isInclusiveCheck = inclusive;
			return definition;
		} );
	}

	private enum MathOperator
	{
		Add,
		Sub,
		Mul,
		Div,
		Mod,
		Abs,
		Negate,
		Floor,
		Ceiling,
		IntegerPart,
		FractionalPart,
		InverseFractionalPart,
	}

	[Expose]
	public enum FloatMathOperation
	{
		Add,
		Subtract,
		Multiply,
		Divide,
		Modulo,
		Abs,
		Negate,
		Floor,
		Ceiling,
		[Title( "Integer Part" )]
		IntegerPart,
		[Title( "Fractional Part" )]
		FractionalPart,
		[Title( "Inverse Fractional Part" )]
		InverseFractionalPart,
	}

	public sealed class FloatMathSettings
	{
		public Node B { get; init; }
		public float ValueB { get; init; }
		public bool AbsoluteResult { get; init; }
		public bool NegateResult { get; init; }
	}

	public Node FloatMath( FloatMathOperation operation, Node a, FloatMathSettings settings = null )
	{
		settings ??= new();
		Require( a, NodeKind.Float, nameof( a ) );
		Optional( settings.B, NodeKind.Float );
		return AddNode( NodeKind.Float, native =>
		{
			var definition = NativeEngine.GraphFloatMathNode.Add( native );
			definition.m_inputValueNodeIdxA = (short)Resolve( a );
			definition.m_inputValueNodeIdxB = (short)ResolveOptional( settings.B );
			definition.SetOperator( (int)operation );
			definition.m_valueB = settings.ValueB;
			definition.m_returnAbsoluteResult = settings.AbsoluteResult;
			definition.m_returnNegatedResult = settings.NegateResult;
			return definition;
		} );
	}

	private Node Math( MathOperator op, Node a, Node b )
	{
		Require( a, NodeKind.Float, nameof( a ) );
		Require( b, NodeKind.Float, nameof( b ) );
		return AddNode( NodeKind.Float, native =>
		{
			var definition = NativeEngine.GraphFloatMathNode.Add( native );
			definition.m_inputValueNodeIdxA = (short)Resolve( a );
			definition.m_inputValueNodeIdxB = (short)Resolve( b );
			definition.SetOperator( (int)op );
			return definition;
		} );
	}

	private Node Math( MathOperator op, Node a, float valueB = 0.0f )
	{
		Require( a, NodeKind.Float, nameof( a ) );
		return AddNode( NodeKind.Float, native =>
		{
			var definition = NativeEngine.GraphFloatMathNode.Add( native );
			definition.m_inputValueNodeIdxA = (short)Resolve( a );
			definition.m_inputValueNodeIdxB = (short)(-1);
			definition.SetOperator( (int)op );
			definition.m_valueB = valueB;
			return definition;
		} );
	}

	/// <summary>a + b</summary>
	public Node Add( Node a, Node b ) => Math( MathOperator.Add, a, b );
	/// <inheritdoc cref="Add(Node, Node)"/>
	public Node Add( Node a, float b ) => Math( MathOperator.Add, a, valueB: b );
	/// <summary>a - b</summary>
	public Node Subtract( Node a, Node b ) => Math( MathOperator.Sub, a, b );
	/// <inheritdoc cref="Subtract(Node, Node)"/>
	public Node Subtract( Node a, float b ) => Math( MathOperator.Sub, a, valueB: b );
	/// <summary>a * b</summary>
	public Node Multiply( Node a, Node b ) => Math( MathOperator.Mul, a, b );
	/// <inheritdoc cref="Multiply(Node, Node)"/>
	public Node Multiply( Node a, float b ) => Math( MathOperator.Mul, a, valueB: b );
	/// <summary>a / b</summary>
	public Node Divide( Node a, Node b ) => Math( MathOperator.Div, a, b );
	/// <inheritdoc cref="Divide(Node, Node)"/>
	public Node Divide( Node a, float b ) => Math( MathOperator.Div, a, valueB: b );
	/// <summary>a modulo b</summary>
	public Node Modulo( Node a, Node b ) => Math( MathOperator.Mod, a, b );
	/// <inheritdoc cref="Modulo(Node, Node)"/>
	public Node Modulo( Node a, float b ) => Math( MathOperator.Mod, a, valueB: b );
	/// <summary>The absolute value of <paramref name="input"/>.</summary>
	public Node Abs( Node input ) => Math( MathOperator.Abs, input );
	/// <summary>-input</summary>
	public Node Negate( Node input ) => Math( MathOperator.Negate, input );
	/// <summary>The largest integer not greater than <paramref name="input"/>.</summary>
	public Node Floor( Node input ) => Math( MathOperator.Floor, input );
	/// <summary>The smallest integer not less than <paramref name="input"/>.</summary>
	public Node Ceiling( Node input ) => Math( MathOperator.Ceiling, input );
	/// <summary>The fractional part of <paramref name="input"/>.</summary>
	public Node Fraction( Node input ) => Math( MathOperator.FractionalPart, input );
	/// <summary>Sample a uniform random value each time this node is initialized, holding it until shutdown.</summary>
	/// <param name="min">The inclusive lower bound.</param>
	/// <param name="max">The inclusive upper bound.</param>
	public Node RandomFloat( float min = 0.0f, float max = 1.0f )
	{
		if ( !float.IsFinite( min ) || !float.IsFinite( max ) || max < min || !float.IsFinite( max - min ) )
			throw new ArgumentException( "Random float bounds must be finite and ordered." );
		return AddNode( NodeKind.Float, native =>
		{
			var definition = NativeEngine.GraphFloatRandomNode.Add( native );
			definition.m_min = min;
			definition.m_max = max;
			return definition;
		} );
	}

	/// <summary>
	/// Clamp <paramref name="input"/> to [<paramref name="min"/>, <paramref name="max"/>].
	/// </summary>
	public Node Clamp( Node input, float min, float max )
	{
		Require( input, NodeKind.Float );
		return AddNode( NodeKind.Float, native =>
		{
			var definition = NativeEngine.GraphFloatClampNode.Add( native );
			definition.m_inputValueNodeIdx = (short)Resolve( input );
			definition.m_clampMin = min;
			definition.m_clampMax = max;
			return definition;
		} );
	}

	/// <summary>
	/// Remap <paramref name="input"/> from one range to another, clamped to the output range.
	/// </summary>
	public Node Remap( Node input, Vector2 inputRange, Vector2 outputRange )
	{
		Require( input, NodeKind.Float );
		return AddNode( NodeKind.Float, native =>
		{
			var definition = NativeEngine.GraphFloatRemapNode.Add( native );
			definition.m_inputValueNodeIdx = (short)Resolve( input );
			definition.m_inputRangeMin = inputRange.x;
			definition.m_inputRangeMax = inputRange.y;
			definition.m_outputRangeMin = outputRange.x;
			definition.m_outputRangeMax = outputRange.y;
			return definition;
		} );
	}

	/// <summary>
	/// Select between two float values based on a bool <paramref name="condition"/>.
	/// </summary>
	public Node Switch( Node condition, Node whenTrue, Node whenFalse )
	{
		Require( condition, NodeKind.Bool );
		Require( whenTrue, NodeKind.Float );
		Require( whenFalse, NodeKind.Float );
		return AddNode( NodeKind.Float, native =>
		{
			var definition = NativeEngine.GraphFloatSwitchNode.Add( native );
			definition.m_switchValueNodeIdx = (short)Resolve( condition );
			definition.m_trueValueNodeIdx = (short)Resolve( whenTrue );
			definition.m_falseValueNodeIdx = (short)Resolve( whenFalse );
			return definition;
		} );
	}

	/// <inheritdoc cref="Switch(Node, Node, Node)"/>
	public Node Switch( Node condition, float whenTrue, float whenFalse )
	{
		Require( condition, NodeKind.Bool );
		return AddNode( NodeKind.Float, native =>
		{
			var definition = NativeEngine.GraphFloatSwitchNode.Add( native );
			definition.m_switchValueNodeIdx = (short)Resolve( condition );
			definition.m_trueValueNodeIdx = (short)(-1);
			definition.m_falseValueNodeIdx = (short)(-1);
			definition.m_trueValue = whenTrue;
			definition.m_falseValue = whenFalse;
			return definition;
		} );
	}

	/// <summary>
	/// An easing curve, matching the runtime's easing operations.
	/// </summary>
	[Expose]
	public enum Easing
	{
		Linear,
		InQuad,
		OutQuad,
		InOutQuad,
		InCubic,
		OutCubic,
		InOutCubic,
		InQuart,
		OutQuart,
		InOutQuart,
		InQuint,
		OutQuint,
		InOutQuint,
		InSine,
		OutSine,
		InOutSine,
		InExpo,
		OutExpo,
		InOutExpo,
		InCirc,
		OutCirc,
		InOutCirc,
		None,
	}

	public sealed class EaseSettings
	{
		public float Time { get; init; } = 0.2f;
		public Easing Easing { get; init; } = Easing.Linear;
		public bool UseStartValue { get; init; }
		public float StartValue { get; init; }
	}

	public sealed class SpringSettings
	{
		public float Hertz { get; init; } = 4.0f;
		public float DampingRatio { get; init; } = 0.7f;
		public bool UseStartValue { get; init; }
		public float StartValue { get; init; }
	}

	/// <summary>
	/// Ease towards <paramref name="input"/> over time
	/// whenever it changes, smoothing out sudden value jumps.
	/// </summary>
	public Node Ease( Node input, EaseSettings settings = null )
	{
		settings ??= new();
		Require( input, NodeKind.Float );
		return AddNode( NodeKind.Float, native =>
		{
			var definition = NativeEngine.GraphFloatEaseNode.Add( native );
			definition.m_inputValueNodeIdx = (short)Resolve( input );
			definition.m_easeTime = settings.Time;
			definition.SetEasingOp( (int)settings.Easing );
			definition.m_useStartValue = settings.UseStartValue;
			definition.m_startValue = settings.StartValue;
			return definition;
		} );
	}

	/// <summary>
	/// Smoothly track <paramref name="input"/> with a damped spring - useful for
	/// damping noisy inputs like velocity before they drive a blend space.
	/// </summary>
	public Node Spring( Node input, SpringSettings settings = null )
	{
		settings ??= new();
		Require( input, NodeKind.Float );
		return AddNode( NodeKind.Float, native =>
		{
			var definition = NativeEngine.GraphFloatSpringNode.Add( native );
			definition.m_inputValueNodeIdx = (short)Resolve( input );
			definition.m_hertz = settings.Hertz;
			definition.m_dampingRatio = settings.DampingRatio;
			definition.m_useStartValue = settings.UseStartValue;
			definition.m_startValue = settings.StartValue;
			return definition;
		} );
	}

	/// <summary>
	/// Remap <paramref name="input"/> through a piecewise cubic Hermite curve.
	/// Points are sorted by parameter; inputs outside the curve's parameter range
	/// clamp to the nearest end point's value.
	/// </summary>
	/// <param name="input">The float node driving the curve parameter.</param>
	/// <param name="points">Curve points as (parameter, value, in-tangent, out-tangent).</param>
	public Node Curve( Node input, params (float Parameter, float Value, float InTangent, float OutTangent)[] points )
	{
		Require( input, NodeKind.Float );
		var sorted = points?.OrderBy( point => point.Parameter ).ToArray() ?? [];
		return AddNode( NodeKind.Float, native =>
		{
			var definition = NativeEngine.GraphFloatCurveNode.Add( native );
			definition.m_inputValueNodeIdx = (short)Resolve( input );
			var nativeCurve = definition.GetCurve();
			var nativePoints = nativeCurve.GetPoints();
			foreach ( var p in sorted )
			{
				var nativeItem = nativePoints.Add();
				nativeItem.m_parameter = p.Parameter;
				nativeItem.m_value = p.Value;
				nativeItem.m_inTangent = p.InTangent;
				nativeItem.m_outTangent = p.OutTangent;
			}

			return definition;
		} );
	}

	/// <inheritdoc cref="Curve(Node, ValueTuple{float, float, float, float}[])"/>
	public Node Curve( Node input, params (float Parameter, float Value)[] points )
	{
		return Curve( input, points.Select( p => (p.Parameter, p.Value, 1.0f, 1.0f) ).ToArray() );
	}

	/// <summary>
	/// Angle normalization operation for <see cref="AngleMath"/>.
	/// </summary>
	[Expose]
	public enum AngleOperation
	{
		/// <summary>Wrap the angle into [-180, 180].</summary>
		[Title( "Clamp To [-180, 180]" )]
		ClampTo180,
		/// <summary>Wrap the angle into [0, 360].</summary>
		[Title( "Clamp To [0, 360]" )]
		ClampTo360,
		/// <summary>Flip the angle into the opposite hemisphere.</summary>
		[Title( "Flip Hemisphere" )]
		FlipHemisphere,
		/// <summary>Flip the angle into the opposite hemisphere and negate it.</summary>
		[Title( "Flip Hemisphere Negate" )]
		FlipHemisphereNegate,
	}

	/// <summary>
	/// Normalize an angle (in degrees) with the given <paramref name="operation"/>.
	/// </summary>
	public Node AngleMath( Node input, AngleOperation operation )
	{
		Require( input, NodeKind.Float );
		return AddNode( NodeKind.Float, native =>
		{
			var definition = NativeEngine.GraphFloatAngleMathNode.Add( native );
			definition.m_inputValueNodeIdx = (short)Resolve( input );
			definition.SetOperation( (int)operation );
			return definition;
		} );
	}

	/// <summary>
	/// Output the value of the first case whose condition is true, easing between
	/// changes, falling back to <paramref name="defaultValue"/> when none are true.
	/// </summary>
	public Node FloatSelector( IEnumerable<(Node Condition, float Value)> cases, float defaultValue = 0.0f, float easeTime = 0.2f, Easing easing = Easing.Linear )
	{
		var list = cases?.ToArray() ?? [];
		foreach ( var c in list )
			Require( c.Condition, NodeKind.Bool, nameof( cases ) );
		return AddNode( NodeKind.Float, native =>
		{
			var definition = NativeEngine.GraphFloatSelectorNode.Add( native );
			var nativeConditionNodeIndices = definition.GetConditionNodeIndices();
			foreach ( var c in list )
			{
				nativeConditionNodeIndices.Add( Resolve( c.Condition ) );
			}

			var nativeValues = definition.GetValues();
			foreach ( var c in list )
			{
				nativeValues.Add( c.Value );
			}

			definition.m_defaultValue = defaultValue;
			definition.m_easeTime = easeTime;
			definition.SetEasingOp( (int)easing );
			return definition;
		} );
	}

	/// <summary>
	/// True when <paramref name="id"/> matches (or, when <paramref name="mustMatch"/> is
	/// false, does not match) any of the given <paramref name="matchIds"/>.
	/// </summary>
	public Node IdCompare( Node id, IEnumerable<string> matchIds, bool mustMatch = true )
	{
		Require( id, NodeKind.Id );
		var ids = matchIds?.ToArray() ?? [];
		bool canShare = id.Index < parameters.Count && ids.Length == 1;
		var key = (id, ids.Length == 1 ? IdString( ids[0] ) : "", mustMatch);
		if ( canShare && parameterIdComparisons.TryGetValue( key, out var existing ) )
			return existing;

		var node = AddNode( NodeKind.Bool, native =>
		{
			var definition = NativeEngine.GraphIDComparisonNode.Add( native );
			definition.m_inputValueNodeIdx = (short)Resolve( id );
			definition.SetComparison( mustMatch ? 0 : 1 );
			var nativeComparisionIDs = definition.GetComparisionIDs();
			foreach ( var nativeValue in ids )
			{
				nativeComparisionIDs.Add( native.InternID( IdString( nativeValue ) ) );
			}

			return definition;
		} );
		if ( canShare )
		{
			parameterIdComparisons.Add( key, node );
			parameterExpressions.Add( node );
		}
		return node;
	}

	/// <inheritdoc cref="IdCompare(Node, IEnumerable{string}, bool)"/>
	public Node IdCompare( Node id, string matchId, bool mustMatch = true ) => IdCompare( id, [matchId], mustMatch );
	/// <summary>
	/// Map an ID to a float using a lookup table, returning <paramref name="defaultValue"/> when unmatched.
	/// </summary>
	public Node IdToFloat( Node id, IEnumerable<(string Id, float Value)> mapping, float defaultValue = 0.0f )
	{
		Require( id, NodeKind.Id );
		var list = mapping?.ToArray() ?? [];
		if ( list.Length == 0 )
			throw new ArgumentException( "IdToFloat needs at least one mapping entry.", nameof( mapping ) );
		return AddNode( NodeKind.Float, native =>
		{
			var definition = NativeEngine.GraphIDToFloatNode.Add( native );
			definition.m_inputValueNodeIdx = (short)Resolve( id );
			definition.m_defaultValue = defaultValue;
			var nativeIDs = definition.GetIDs();
			foreach ( var mapping in list )
			{
				nativeIDs.Add( native.InternID( IdString( mapping.Id ) ) );
			}

			var nativeValues = definition.GetValues();
			foreach ( var m in list )
			{
				nativeValues.Add( m.Value );
			}

			return definition;
		} );
	}

	/// <summary>
	/// Select between two ID values based on a bool <paramref name="condition"/>.
	/// </summary>
	public Node IdSwitch( Node condition, Node whenTrue, Node whenFalse )
	{
		Require( condition, NodeKind.Bool );
		Require( whenTrue, NodeKind.Id );
		Require( whenFalse, NodeKind.Id );
		return AddNode( NodeKind.Id, native =>
		{
			var definition = NativeEngine.GraphIDSwitchNode.Add( native );
			definition.m_switchValueNodeIdx = (short)Resolve( condition );
			definition.m_trueValueNodeIdx = (short)Resolve( whenTrue );
			definition.m_falseValueNodeIdx = (short)Resolve( whenFalse );
			return definition;
		} );
	}

	/// <inheritdoc cref="IdSwitch(Node, Node, Node)"/>
	public Node IdSwitch( Node condition, string whenTrue, string whenFalse )
	{
		Require( condition, NodeKind.Bool );
		return AddNode( NodeKind.Id, native =>
		{
			var definition = NativeEngine.GraphIDSwitchNode.Add( native );
			definition.m_switchValueNodeIdx = (short)Resolve( condition );
			definition.m_trueValueNodeIdx = (short)(-1);
			definition.m_falseValueNodeIdx = (short)(-1);
			definition.SetTrueValue( native.InternID( IdString( whenTrue ) ) );
			definition.SetFalseValue( native.InternID( IdString( whenFalse ) ) );
			return definition;
		} );
	}

	public sealed class IdSwitchSettings
	{
		public Node WhenTrue { get; init; }
		public string TrueValue { get; init; }
		public Node WhenFalse { get; init; }
		public string FalseValue { get; init; }
	}

	public Node IdSwitch( Node condition, IdSwitchSettings settings )
	{
		ArgumentNullException.ThrowIfNull( settings );
		Optional( condition, NodeKind.Bool );
		Optional( settings.WhenTrue, NodeKind.Id );
		Optional( settings.WhenFalse, NodeKind.Id );
		return AddNode( NodeKind.Id, native =>
		{
			var definition = NativeEngine.GraphIDSwitchNode.Add( native );
			definition.m_switchValueNodeIdx = (short)ResolveOptional( condition );
			definition.m_trueValueNodeIdx = (short)ResolveOptional( settings.WhenTrue );
			definition.m_falseValueNodeIdx = (short)ResolveOptional( settings.WhenFalse );
			definition.SetTrueValue( native.InternID( IdString( settings.TrueValue ) ) );
			definition.SetFalseValue( native.InternID( IdString( settings.FalseValue ) ) );
			return definition;
		} );
	}

	/// <summary>
	/// Output the ID of the first case whose condition is true, falling back to
	/// <paramref name="defaultValue"/> when none are true.
	/// </summary>
	public Node IdSelect( IEnumerable<(Node Condition, string Value)> cases, string defaultValue = null )
	{
		var list = cases?.ToArray() ?? [];
		if ( list.Length == 0 )
			throw new ArgumentException( "IdSelect needs at least one case.", nameof( cases ) );
		foreach ( var c in list )
			Require( c.Condition, NodeKind.Bool, nameof( cases ) );
		return AddNode( NodeKind.Id, native =>
		{
			var definition = NativeEngine.GraphIDSelectorNode.Add( native );
			var nativeConditionNodeIndices = definition.GetConditionNodeIndices();
			foreach ( var c in list )
			{
				nativeConditionNodeIndices.Add( Resolve( c.Condition ) );
			}

			var nativeValues = definition.GetValues();
			foreach ( var item in list )
			{
				nativeValues.Add( native.InternID( IdString( item.Value ) ) );
			}

			definition.SetDefaultValue( native.InternID( IdString( defaultValue ) ) );
			return definition;
		} );
	}

	/// <summary>
	/// A scalar extracted from a vector by <see cref="VectorInfo"/>.
	/// </summary>
	[Expose]
	public enum VectorComponent
	{
		X,
		Y,
		Z,
		Length,
		/// <summary>Angle in the horizontal plane, in degrees.</summary>
		AngleHorizontal,
		/// <summary>Angle from the horizontal plane, in degrees.</summary>
		AngleVertical,
	}

	/// <summary>
	/// Extract a scalar from a vector - a component, its length, or an angle.
	/// </summary>
	public Node VectorInfo( Node vector, VectorComponent info )
	{
		Require( vector, NodeKind.Vector );
		return AddNode( NodeKind.Float, native =>
		{
			var definition = NativeEngine.GraphVectorInfoNode.Add( native );
			definition.m_inputValueNodeIdx = (short)Resolve( vector );
			definition.SetDesiredInfo( (int)info );
			return definition;
		} );
	}

	/// <summary>
	/// Build a vector from float components. Any omitted component falls back to the
	/// matching component of <paramref name="baseVector"/> (or zero when it too is omitted).
	/// </summary>
	public Node VectorCreate( Node x = default, Node y = default, Node z = default, Node baseVector = default )
	{
		Optional( x, NodeKind.Float );
		Optional( y, NodeKind.Float );
		Optional( z, NodeKind.Float );
		Optional( baseVector, NodeKind.Vector );
		if ( x is null && y is null && z is null && baseVector is null )
			throw new ArgumentException( "VectorCreate needs at least one component or a base vector." );
		return AddNode( NodeKind.Vector, native =>
		{
			var definition = NativeEngine.GraphVectorCreateNode.Add( native );
			definition.m_inputVectorValueNodeIdx = (short)ResolveOptional( baseVector );
			definition.m_inputValueXNodeIdx = (short)ResolveOptional( x );
			definition.m_inputValueYNodeIdx = (short)ResolveOptional( y );
			definition.m_inputValueZNodeIdx = (short)ResolveOptional( z );
			return definition;
		} );
	}

	/// <summary>
	/// Negate a vector.
	/// </summary>
	public Node VectorNegate( Node vector )
	{
		Require( vector, NodeKind.Vector );
		return AddNode( NodeKind.Vector, native =>
		{
			var definition = NativeEngine.GraphVectorNegateNode.Add( native );
			definition.m_inputValueNodeIdx = (short)Resolve( vector );
			return definition;
		} );
	}

	/// <summary>
	/// Latch a value from <paramref name="source"/> - by default once when this node
	/// starts (cache-on-init), otherwise once when its branch deactivates (cache-on-exit).
	/// Works with bool, float, ID, vector and target values.
	/// </summary>
	public Node Cached( Node source, bool cacheOnInit = true )
	{
		if ( source is null )
			throw new ArgumentException( "The 'source' node input is required but was not provided.", nameof( source ) );
		ValidateOwner( source, nameof( source ) );
		if ( source.Kind is not (NodeKind.Bool or NodeKind.Float or NodeKind.Id or NodeKind.Vector or NodeKind.Target) )
			throw new ArgumentException( $"Cannot cache a {source.Kind} node - only bool, float, ID, vector and target values can be cached.", nameof( source ) );
		return AddNode( source.Kind, native =>
		{
			switch ( source.Kind )
			{
				case NodeKind.Bool:
					{
						var definition = NativeEngine.GraphCachedBoolNode.Add( native );
						definition.m_sourceNodeIdx = (short)Resolve( source );
						definition.m_cacheOnInit = cacheOnInit;
						return definition;
					}

				case NodeKind.Float:
					{
						var definition = NativeEngine.GraphCachedFloatNode.Add( native );
						definition.m_sourceNodeIdx = (short)Resolve( source );
						definition.m_cacheOnInit = cacheOnInit;
						return definition;
					}

				case NodeKind.Id:
					{
						var definition = NativeEngine.GraphCachedIDNode.Add( native );
						definition.m_sourceNodeIdx = (short)Resolve( source );
						definition.m_cacheOnInit = cacheOnInit;
						return definition;
					}

				case NodeKind.Vector:
					{
						var definition = NativeEngine.GraphCachedVectorNode.Add( native );
						definition.m_sourceNodeIdx = (short)Resolve( source );
						definition.m_cacheOnInit = cacheOnInit;
						return definition;
					}

				case NodeKind.Target:
					{
						var definition = NativeEngine.GraphCachedTargetNode.Add( native );
						definition.m_sourceNodeIdx = (short)Resolve( source );
						definition.m_cacheOnInit = cacheOnInit;
						return definition;
					}

				default:
					throw new ArgumentException( $"Cannot cache a {source.Kind} node - only bool, float, ID, vector and target values can be cached.", nameof( source ) );
			}
		} );
	}

	/// <summary>
	/// True while the given target is set.
	/// </summary>
	public Node IsTargetSet( Node target )
	{
		Require( target, NodeKind.Target );
		return AddNode( NodeKind.Bool, native =>
		{
			var definition = NativeEngine.GraphIsTargetSetNode.Add( native );
			definition.m_inputValueNodeIdx = (short)Resolve( target );
			return definition;
		} );
	}

	/// <summary>
	/// A scalar extracted from a target by <see cref="TargetInfo"/>.
	/// </summary>
	[Expose]
	public enum TargetInfoType
	{
		/// <summary>Angle to the target in the horizontal plane, in degrees.</summary>
		AngleHorizontal,
		/// <summary>Angle to the target from the horizontal plane, in degrees.</summary>
		AngleVertical,
		Distance,
		DistanceHorizontalOnly,
		DistanceVerticalOnly,
		DeltaOrientationX,
		DeltaOrientationY,
		DeltaOrientationZ,
	}

	/// <summary>
	/// Extract a scalar from a target - an angle, distance, or delta orientation.
	/// </summary>
	public Node TargetInfo( Node target, TargetInfoType info, bool worldSpace = true )
	{
		Require( target, NodeKind.Target );
		return AddNode( NodeKind.Float, native =>
		{
			var definition = NativeEngine.GraphTargetInfoNode.Add( native );
			definition.m_inputValueNodeIdx = (short)Resolve( target );
			definition.SetInfoType( (int)info );
			definition.m_isWorldSpaceTarget = worldSpace;
			return definition;
		} );
	}

	/// <summary>
	/// Extract the translation of a target as a vector.
	/// </summary>
	public Node TargetPoint( Node target, bool worldSpace = true )
	{
		Require( target, NodeKind.Target );
		return AddNode( NodeKind.Vector, native =>
		{
			var definition = NativeEngine.GraphTargetPointNode.Add( native );
			definition.m_inputValueNodeIdx = (short)Resolve( target );
			definition.m_isWorldSpaceTarget = worldSpace;
			return definition;
		} );
	}

	/// <summary>
	/// Apply a rotation and translation offset to a target.
	/// </summary>
	/// <param name="target">The target node to offset.</param>
	/// <param name="translationOffset">The translation offset to apply.</param>
	/// <param name="rotationOffset">The rotation offset to apply.</param>
	/// <param name="boneSpace">When true the offset is applied in the target's local (bone) space.</param>
	/// <param name="translation">Optional vector input overriding the constant translation offset.</param>
	public Node TargetOffset( Node target, Vector3 translationOffset, Rotation rotationOffset, bool boneSpace = true, Node translation = null )
	{
		Require( target, NodeKind.Target );
		if ( translation is not null )
			Require( translation, NodeKind.Vector );
		return AddNode( NodeKind.Target, native =>
		{
			var definition = NativeEngine.GraphTargetOffsetNode.Add( native );
			definition.m_inputValueNodeIdx = (short)Resolve( target );
			definition.m_translationOffsetNodeIdx = (short)ResolveOptional( translation );
			definition.m_isBoneSpaceOffset = boneSpace;
			definition.m_rotationOffset = rotationOffset;
			definition.m_translationOffset = translationOffset;
			return definition;
		} );
	}
}
