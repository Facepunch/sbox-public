using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Sandbox.Mounting.Source1;

internal sealed class MaterialProxyProgram
{
	internal enum Operation
	{
		Constant, Time, Add, Subtract, Multiply, Divide, Abs, Frac, Int, Sin, Cos, Min, Max, LessOrEqual, Select, Exp, Pow, VectorDivide, Fmod
	}

	internal sealed class Expression
	{
		internal readonly Operation Operation;
		internal readonly float Constant;
		internal readonly Expression A;
		internal readonly Expression B;
		internal readonly Expression C;

		internal Expression( Operation operation, float constant = 0, Expression a = null, Expression b = null, Expression c = null )
		{
			Operation = operation;
			Constant = constant;
			A = a;
			B = b;
			C = c;
		}
		public bool IsConstant => Operation == Operation.Constant;
	}

	internal sealed record Value( Expression[] Components, bool Matrix = false )
	{
		public Expression Scalar => Components[0];
		public bool IsConstant => Array.TrueForAll( Components, x => x.IsConstant );
	}

	private readonly string path;
	private readonly VmtParameters parameters;
	private readonly Dictionary<string, Value> variables = new( StringComparer.OrdinalIgnoreCase );
	private readonly Dictionary<string, Value> outputs = new( StringComparer.OrdinalIgnoreCase );
	private readonly HashSet<string> unavailable = new( StringComparer.OrdinalIgnoreCase );
	private readonly HashSet<string> initialReads = new( StringComparer.OrdinalIgnoreCase );
	private readonly HashSet<string> writes = new( StringComparer.OrdinalIgnoreCase );
	private readonly Dictionary<Expression, int> indices = new();
	private readonly Dictionary<(Operation Operation, int A, int B, int C), int> operations = new();
	private readonly Dictionary<float, int> constants = new();
	private readonly List<Vector4> instructions = [];
	private Func<string, int> textureFrames;
	private int timeIndex = -1;

	internal IReadOnlyDictionary<string, Value> Outputs => outputs;
	internal HashSet<string> Inputs { get; } = new( StringComparer.OrdinalIgnoreCase );
	internal IReadOnlyList<Vector4> Instructions => instructions;
	internal bool HasInstructions => instructions.Count > 0;

	internal MaterialProxyProgram( IReadOnlyDictionary<string, string> values, string path )
	{
		this.path = path;
		parameters = new VmtParameters( values, path );
	}

	internal void Compile( VmtNode proxies, Func<VmtNode, bool> handled = null, Func<string, int> textureFrames = null )
	{
		this.textureFrames = textureFrames;
		foreach ( var proxy in proxies?.Children ?? [] )
		{
			if ( handled?.Invoke( proxy ) == true ) continue;
			if ( !proxy.IsBlock )
				throw Invalid( $"proxy '{proxy.Name}' must be a block" );
			try
			{
				Compile( proxy );
			}
			catch ( NotSupportedException e )
			{
				var inputs = new VmtParameters( proxy.Values, path );
				var target = inputs.String( "resultvar", inputs.String( "texturescrollvar", inputs.String( "animatedtextureframenumvar" ) ) );
				if ( string.IsNullOrWhiteSpace( target ) )
					throw new NotSupportedException( $"{path}: proxy '{proxy.Name}' has no identifiable output; its dependency block cannot be evaluated.", e );
				var (name, _) = Reference( target );
				unavailable.Add( name );
				outputs.Remove( name );
				variables.Remove( name );
				Log.Trace( $"{path}: proxy '{proxy.Name}' was not evaluated: {e.Message} Dependent outputs retain authored values." );
			}
		}
		if ( initialReads.Any( writes.Contains ) )
			throw new NotSupportedException( $"{path}: the proxy block reads previously bound material state; persistent feedback requires per-bind evaluation." );
	}

	private void Compile( VmtNode proxy )
	{
		var p = new VmtParameters( proxy.Values, path );
		var kind = proxy.Name.ToLowerInvariant();
		if ( kind == "empty" || kind == "waterlod" ) return;
		var target = p.String( kind == "texturescroll" ? "texturescrollvar" : kind == "animatedtexture" ? "animatedtextureframenumvar" : "resultvar" );
		if ( string.IsNullOrWhiteSpace( target ) )
			throw new NotSupportedException( $"missing output variable for '{proxy.Name}'" );

		Expression Input( string key, float fallback ) => FloatInput( p.String( key ), fallback );
		Value Source( string key ) => Read( p.String( key ) ?? throw Invalid( $"{proxy.Name} requires {key}" ) );
		Expression Clamp( Expression value, Expression min, Expression max ) =>
			Make( Operation.Max, Make( Operation.Min, value, Make( Operation.Max, min, max ) ), Make( Operation.Min, min, max ) );
		Value result;
		switch ( kind )
		{
			case "currenttime":
				result = Scalar( new Expression( Operation.Time ) );
				break;
			case "sine":
				{
					var period = Input( "sineperiod", 1 );
					period = Make( Operation.Select, Make( Operation.LessOrEqual, Make( Operation.Abs, period ), Constant( 0 ) ), Constant( 1 ), period );
					var phase = Make( Operation.Divide, Make( Operation.Fmod, Make( Operation.Subtract, new Expression( Operation.Time ), Input( "timeoffset", 0 ) ), period ), period );
					var wave = Make( Operation.Add, Make( Operation.Multiply, Make( Operation.Sin, Make( Operation.Multiply, phase, Constant( MathF.Tau ) ) ), Constant( 0.5f ) ), Constant( 0.5f ) );
					var min = Input( "sinemin", 0 );
					result = Scalar( Make( Operation.Add, Make( Operation.Multiply, Make( Operation.Subtract, Input( "sinemax", 1 ), min ), wave ), min ) );
					break;
				}
			case "linearramp":
				{
					var wrapped = Make( Operation.Fmod, new Expression( Operation.Time ), Constant( 10000 ) );
					result = Scalar( Make( Operation.Add, Make( Operation.Multiply, Input( "rate", 1 ), wrapped ), Input( "initialvalue", 0 ) ) );
					break;
				}
			case "texturescroll":
				{
					var degrees = Input( "texturescrollangle", 0 );
					var angle = Make( Operation.Multiply, degrees, Constant( MathF.PI / 180 ) );
					var distance = Make( Operation.Multiply, new Expression( Operation.Time ), Input( "texturescrollrate", 1 ) );
					Expression Offset( Operation axis )
					{
						var direction = degrees.IsConstant
							? Constant( (float)(axis == Operation.Cos ? Math.Cos( degrees.Constant * (Math.PI / 180) ) : Math.Sin( degrees.Constant * (Math.PI / 180) )) )
							: Make( axis, angle );
						var offset = Make( Operation.Multiply, distance, direction );
						var signed = Make( Operation.Frac, offset );
						return Make( Operation.Select, Make( Operation.LessOrEqual, Constant( 0 ), signed ), signed, Make( Operation.Add, signed, Constant( 1 ) ) );
					}
					var scale = Input( "texturescale", 1 );
					var (scrollTarget, _) = Reference( target );
					result = ReadVariable( scrollTarget, required: false )?.Matrix == true || scrollTarget.Contains( "transform", StringComparison.OrdinalIgnoreCase )
						? new Value( [scale, Constant( 0 ), Offset( Operation.Cos ), Constant( 0 ), scale, Offset( Operation.Sin )], true )
						: new Value( [Offset( Operation.Cos ), Offset( Operation.Sin ), Constant( 0 )] );
					break;
				}
			case "texturetransform":
				{
					Expression Component( string key, int index, float fallback )
					{
						var reference = p.String( key );
						if ( string.IsNullOrWhiteSpace( reference ) ) return Constant( fallback );
						var value = Read( reference );
						return ComponentAt( value, index );
					}
					var cx = Component( "centervar", 0, 0.5f );
					var cy = Component( "centervar", 1, 0.5f );
					var sx = Component( "scalevar", 0, 1 );
					var sy = Component( "scalevar", 1, 1 );
					var angle = Make( Operation.Multiply, Component( "rotatevar", 0, 0 ), Constant( MathF.PI / 180 ) );
					var cosine = Make( Operation.Cos, angle );
					var sine = Make( Operation.Sin, angle );
					var ux = Make( Operation.Multiply, sx, cosine );
					var uy = Make( Operation.Multiply, sy, Make( Operation.Subtract, Constant( 0 ), sine ) );
					var vx = Make( Operation.Multiply, sx, sine );
					var vy = Make( Operation.Multiply, sy, cosine );
					Expression Translate( Expression a, Expression b, Expression center, string key, int component ) =>
						Make( Operation.Subtract, Make( Operation.Add, center, Component( key, component, 0 ) ),
							Make( Operation.Add, Make( Operation.Multiply, a, cx ), Make( Operation.Multiply, b, cy ) ) );
					result = new Value( [ux, uy, Translate( ux, uy, cx, "translatevar", 0 ), vx, vy, Translate( vx, vy, cy, "translatevar", 1 )], true );
					break;
				}
			case "animatedtexture":
				{
					var frames = textureFrames?.Invoke( p.String( "animatedtexturevar" ) ) ?? 0;
					if ( frames < 1 || p.Boolean( "animationnowrap" ) || p.Float( "animatedtextureframerate", 15 ) < 0 )
						throw new NotSupportedException( "AnimatedTexture requires a supported texture and forward looping playback" );
					var frame = Make( Operation.Int, Make( Operation.Multiply, Make( Operation.Max, new Expression( Operation.Time ), Constant( 0 ) ), Constant( p.Float( "animatedtextureframerate", 15 ) ) ) );
					result = Scalar( Make( Operation.Fmod, frame, Constant( frames ) ) );
					break;
				}
			case "add":
			case "subtract":
			case "multiply":
			case "divide":
			case "equals":
			case "frac":
			case "int":
			case "abs":
			case "clamp":
			case "exponential":
			case "lessorequal":
				{
					var a = Source( "srcvar1" );
					var b = kind is "add" or "subtract" or "multiply" or "divide" or "lessorequal" ? Source( "srcvar2" ) : null;
					var (name, component) = Reference( target );
					var original = ReadVariable( name, required: false );
					var count = component >= 0 ? 1 : original?.Components.Length
						?? (kind == "lessorequal" ? Math.Max( Source( "lessequalvar" ).Components.Length, Source( "greatervar" ).Components.Length ) : a.Components.Length);
					if ( a.Matrix || b?.Matrix == true || original?.Matrix == true )
						throw new NotSupportedException( "matrix arithmetic is not supported" );
					var values = new Expression[count];
					for ( var i = 0; i < count; i++ )
					{
						var x = kind is "abs" or "exponential" or "lessorequal" ? a.Scalar : ComponentAt( a, i );
						var y = b is null ? null : ComponentAt( b, i );
						values[i] = kind switch
						{
							"add" => Make( Operation.Add, x, y ),
							"subtract" => Make( Operation.Subtract, x, y ),
							"multiply" => Make( Operation.Multiply, x, y ),
							"divide" => Make( count > 1 ? Operation.VectorDivide : Operation.Divide, x, y ),
							"equals" => x,
							"frac" => Make( Operation.Frac, x ),
							"int" => Make( Operation.Int, x ),
							"abs" => Make( Operation.Abs, x ),
							"clamp" => Clamp( x, Input( "min", 0 ), Input( "max", 1 ) ),
							"exponential" => Clamp( Make( Operation.Multiply, Input( "scale", 1 ),
								Make( Operation.Exp, Make( Operation.Add, x, Input( "offset", 0 ) ) ) ), Input( "minval", -float.MaxValue ), Input( "maxval", float.MaxValue ) ),
							"lessorequal" => Make( Operation.Select, Make( Operation.LessOrEqual, a.Scalar, b.Scalar ),
								ComponentAt( Source( "lessequalvar" ), i ), ComponentAt( Source( "greatervar" ), i ) ),
							_ => throw new NotSupportedException( proxy.Name )
						};
					}
					result = new Value( values );
					break;
				}
			default:
				throw new NotSupportedException( $"unsupported proxy '{proxy.Name}'" );
		}
		Write( target, result );
	}

	internal Value Read( string reference )
	{
		var (name, component) = Reference( reference );
		var value = ReadVariable( name );
		Inputs.Add( name );
		if ( !writes.Contains( name ) ) initialReads.Add( name );
		return component >= 0 ? Scalar( ComponentAt( value, component ) ) : value;
	}

	private Value ReadVariable( string name, bool required = true )
	{
		if ( required && unavailable.Contains( name ) )
			throw new NotSupportedException( $"input '{name}' depends on an unsupported proxy" );
		if ( variables.TryGetValue( name, out var value ) ) return value;
		var text = parameters.String( name );
		if ( text is null )
		{
			if ( !required ) return null;
			if ( name is "$color" or "$color2" or "$envmaptint" or "$selfillumtint" )
				return new Value( [Constant( 1 ), Constant( 1 ), Constant( 1 )] );
			if ( name is "$alpha" or "$detailblendfactor" or "$phongboost" ) return Scalar( Constant( 1 ) );
			if ( name is "$frame" or "$bumpframe" ) return Scalar( Constant( 0 ) );
			throw new NotSupportedException( $"input variable '{name}' does not exist" );
		}
		if ( name.Contains( "transform", StringComparison.OrdinalIgnoreCase ) )
		{
			var matrix = parameters.Transform( name );
			value = new Value( [Constant( matrix.M11 ), Constant( matrix.M21 ), Constant( matrix.M41 ), Constant( matrix.M12 ), Constant( matrix.M22 ), Constant( matrix.M42 )], true );
		}
		else if ( text.TrimStart().StartsWith( '[' ) || text.TrimStart().StartsWith( '{' ) )
		{
			value = new Value( parameters.Components( name ).Select( Constant ).ToArray() );
		}
		else value = Scalar( Constant( parameters.Float( name ) ) );
		variables[name] = value;
		return value;
	}

	private Expression FloatInput( string text, float fallback )
	{
		if ( string.IsNullOrWhiteSpace( text ) ) return Constant( fallback );
		var first = text.TrimStart()[0];
		if ( !char.IsAsciiDigit( first ) && first is not ('+' or '-' or '.') ) return Read( text ).Scalar;
		var p = new VmtParameters( new Dictionary<string, string> { ["input"] = text }, path );
		return Constant( p.Float( "input" ) );
	}

	private void Write( string reference, Value value )
	{
		var (name, component) = Reference( reference );
		unavailable.Remove( name );
		writes.Add( name );
		if ( component >= 0 )
		{
			var original = ReadVariable( name, required: false ) ?? new Value( [Constant( 0 ), Constant( 0 ), Constant( 0 ), Constant( 0 )] );
			if ( original.Matrix ) throw new NotSupportedException( "matrix component writes are not supported" );
			var components = new Expression[Math.Max( component + 1, original.Components.Length )];
			for ( var i = 0; i < components.Length; i++ ) components[i] = ComponentAt( original, i );
			components[component] = value.Scalar;
			value = new Value( components );
		}
		else if ( !value.Matrix && value.Components.Length == 1 && ReadVariable( name, required: false ) is { Matrix: false } original )
		{
			value = new Value( Enumerable.Repeat( value.Scalar, original.Components.Length ).ToArray() );
		}
		variables[name] = value;
		outputs[name] = value;
	}

	internal int Emit( Expression expression ) => Emit( expression, 0 );

	private int Emit( Expression expression, int depth )
	{
		if ( depth >= 128 ) throw new NotSupportedException( $"{path}: proxy expression nesting exceeds 128 instructions." );
		if ( indices.TryGetValue( expression, out var existing ) ) return existing;
		if ( expression.IsConstant && constants.TryGetValue( expression.Constant, out existing ) ) return existing;
		if ( expression.Operation == Operation.Time && timeIndex >= 0 ) return timeIndex;
		var a = expression.A is null ? 0 : Emit( expression.A, depth + 1 );
		var b = expression.B is null ? 0 : Emit( expression.B, depth + 1 );
		var c = expression.C is null ? 0 : Emit( expression.C, depth + 1 );
		var key = (expression.Operation, a, b, c);
		if ( !expression.IsConstant && operations.TryGetValue( key, out var shared ) )
		{
			indices.Add( expression, shared );
			return shared;
		}
		var index = instructions.Count;
		if ( index >= 128 ) throw new NotSupportedException( $"{path}: proxy expression program exceeds 128 scalar instructions." );
		instructions.Add( new Vector4( (int)expression.Operation, expression.IsConstant ? expression.Constant : a, b, c ) );
		indices.Add( expression, index );
		if ( expression.IsConstant ) constants[expression.Constant] = index;
		else operations[key] = index;
		if ( expression.Operation == Operation.Time ) timeIndex = index;
		return index;
	}

	internal void ResetInstructions()
	{
		indices.Clear();
		operations.Clear();
		constants.Clear();
		instructions.Clear();
		timeIndex = -1;
	}

	internal static Expression Make( Operation operation, Expression a, Expression b = null, Expression c = null )
	{
		if ( a.IsConstant && (b?.IsConstant ?? true) && (c?.IsConstant ?? true) )
			return Constant( Calculate( operation, a.Constant, b?.Constant ?? 0, c?.Constant ?? 0 ) );
		if ( operation is Operation.Multiply or Operation.Divide or Operation.Pow && b is { IsConstant: true, Constant: 1 } )
			return a;
		if ( operation == Operation.Multiply && a is { IsConstant: true, Constant: 1 } )
			return b;
		if ( operation == Operation.Select && a.IsConstant )
			return a.Constant != 0 ? b : c;
		return new Expression( operation, 0, a, b, c );
	}

	internal static float Calculate( Operation operation, float a, float b, float c ) => operation switch
	{
		Operation.Add => a + b,
		Operation.Subtract => a - b,
		Operation.Multiply => a * b,
		Operation.Divide => b == 0 ? a : a / b,
		Operation.Abs => MathF.Abs( a ),
		Operation.Frac => a - MathF.Truncate( a ),
		Operation.Int => MathF.Truncate( a ),
		Operation.Sin => MathF.Sin( a ),
		Operation.Cos => MathF.Cos( a ),
		Operation.Min => MathF.Min( a, b ),
		Operation.Max => MathF.Max( a, b ),
		Operation.LessOrEqual => a <= b ? 1 : 0,
		Operation.Select => a != 0 ? b : c,
		Operation.Exp => MathF.Exp( a ),
		Operation.Pow => MathF.Pow( a, b ),
		Operation.VectorDivide => a / b,
		Operation.Fmod => a % b,
		_ => throw new ArgumentOutOfRangeException( nameof( operation ) )
	};

	internal static Expression Constant( float value )
	{
		if ( !float.IsFinite( value ) ) throw new InvalidDataException( "Source proxy constant expression is not finite." );
		return new Expression( Operation.Constant, value );
	}

	internal static Value Scalar( Expression value ) => new( [value] );
	internal static Expression TextureFrame( Expression value, int count )
	{
		ArgumentOutOfRangeException.ThrowIfLessThan( count, 1 );
		var frame = Make( Operation.Int, value );
		var upper = Make( Operation.Select, Make( Operation.LessOrEqual, frame, Constant( count - 1 ) ), frame, Constant( 0 ) );
		return Make( Operation.Select, Make( Operation.LessOrEqual, Constant( 0 ), frame ), upper, Constant( 0 ) );
	}

	internal static Expression ComponentAt( Value value, int component ) => value.Components.Length == 1
		? value.Scalar : component < value.Components.Length ? value.Components[component] : Constant( 0 );

	private (string Name, int Component) Reference( string reference )
	{
		if ( string.IsNullOrWhiteSpace( reference ) ) throw Invalid( "empty material variable reference" );
		reference = reference.Trim().ToLowerInvariant();
		var bracket = reference.IndexOf( '[' );
		if ( bracket < 0 ) return (reference, -1);
		if ( !reference.EndsWith( ']' ) || !int.TryParse( reference.AsSpan( bracket + 1, reference.Length - bracket - 2 ),
			NumberStyles.Integer, CultureInfo.InvariantCulture, out var component ) || component is < 0 or > 3 )
			throw Invalid( $"invalid material vector component '{reference}'" );
		return (reference[..bracket], component);
	}

	private InvalidDataException Invalid( string message ) => new( $"{path}: {message}." );
}
