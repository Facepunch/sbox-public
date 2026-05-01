using System.Buffers;
using System.Runtime.CompilerServices;
namespace Sandbox;

using static Doo;

/// <summary>
/// System that manages the execution of Doo scripts within a scene.
/// </summary>
[Expose]
public class DooEngine : GameObjectSystem<DooEngine>
{
	Stack<RunContext> _contextStack = new();
	Dictionary<string, object> _globals = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// Creates a new DooEngine for the given scene.
	/// </summary>
	public DooEngine( Scene scene ) : base( scene )
	{

	}

	RunContext GetContext()
	{
		return _contextStack.TryPop( out var pctx ) ? pctx : new RunContext();
	}

	internal void Run( IHost myComponent, Doo doo, Action<Configure> c )
	{
		if ( doo == null ) return;

		var ctx = GetContext();
		ctx.Engine = this;
		ctx.Doo = doo;
		ctx.Source = myComponent;

		if ( c != null )
		{
			var config = new Configure( ctx );
			c( config );
		}

		ctx.Task = RunDoo( ctx );
	}

	async Task RunDoo( RunContext ctx )
	{
		try
		{
			ctx.Source.OnStarted( ctx );

			await RunBody( ctx, ctx.Doo.Body );
		}
		catch ( TaskCanceledException ) { }
		catch ( Exception ex )
		{
			Log.Warning( ex, $"Error running Doo: {ex.Message}" );
		}
		finally
		{
			ctx.Source.OnStopped( ctx );

			ctx.Clear();

			if ( _contextStack.Count < 16 )
			{
				_contextStack.Push( ctx );
			}
		}
	}

	async Task RunBody( RunContext ctx, List<Doo.Block> b )
	{
		if ( b == null ) return;

		for ( int i = 0; i < b.Count; i++ )
		{
			if ( ctx.Stopped ) return;

			await RunBlock( ctx, b[i] );
		}
	}

	async Task RunBlock( RunContext ctx, Doo.Block b )
	{
		switch ( b )
		{
			case Doo.SetBlock s:
				SetVariable( ctx, s.VariableName, Eval( ctx, s.Value ) );
				break;

			case Doo.DelayBlock d:
				await RunBlock_Delay( ctx, d );
				break;

			case Doo.ReturnBlock r:
				ctx.Stopped = true;
				return;

			case Doo.ForBlock forblock:
				await RunBlock_For( ctx, forblock );
				break;

			case Doo.InvokeBlock i:
				RunBlock_Invoke( ctx, i );
				break;
		}
	}

	static bool IsGlobalVariable( string name ) => name.Length > 2 && name[0] == 'g' && name[1] == '_';

	void SetVariable( RunContext ctx, string name, object value )
	{
		if ( string.IsNullOrWhiteSpace( name ) ) return;

		if ( IsGlobalVariable( name ) )
		{
			_globals[name] = value;
			return;
		}

		ctx.LocalVariables[name] = value;
	}

	/// <summary>
	/// Sets a global variable that is accessible to all Doo scripts in this scene.
	/// </summary>
	public void SetGlobalVariable( string name, object value )
	{
		if ( string.IsNullOrWhiteSpace( name ) ) return;

		_globals[name] = value;
	}

	internal object GetVariable( RunContext ctx, string name )
	{
		if ( string.IsNullOrWhiteSpace( name ) ) return null;

		if ( IsGlobalVariable( name ) )
		{
			if ( _globals.TryGetValue( name, out var globalValue ) )
				return globalValue;

			return null;
		}

		if ( ctx.LocalVariables.TryGetValue( name, out var localValue ) )
			return localValue;

		return null;
	}

	private async Task RunBlock_Delay( RunContext ctx, Doo.DelayBlock b )
	{
		double seconds = ToFloat( Eval( ctx, b.Seconds ) );
		if ( seconds < 0 ) seconds = 0;

		await Task.Delay( TimeSpan.FromSeconds( seconds ) );
	}

	private async Task RunBlock_For( RunContext ctx, Doo.ForBlock b )
	{
		double start = ToFloat( Eval( ctx, b.StartValue ) );
		double end = ToFloat( Eval( ctx, b.EndValue ) );
		double jump = ToFloat( Eval( ctx, b.JumpValue ) );

		if ( jump == 0 ) return;

		for ( double i = start; i < end; i += jump )
		{
			if ( ctx.Stopped ) return;

			SetVariable( ctx, b.VariableName, i );

			if ( b.Body != null )
			{
				await RunBody( ctx, b.Body );
			}
		}
	}

	private void RunBlock_Invoke( RunContext ctx, Doo.InvokeBlock b )
	{
		var m = Doo.Helpers.FindMethod( b.Member );

		if ( m == null )
			return;

		int argCount = m.Parameters?.Length ?? 0;

		Component targetInstance = null;

		if ( !m.IsStatic )
		{
			targetInstance = b.TargetComponent.Evaluate( ctx );
			if ( !targetInstance.IsValid() )
				return;
		}

		if ( argCount == 0 )
		{
			var returnedValue = m.InvokeWithReturn<object>( targetInstance );

			if ( m.ReturnType != typeof( void ) && !string.IsNullOrEmpty( b.ReturnVariable ) )
			{
				SetVariable( ctx, b.ReturnVariable, returnedValue );
			}

			return;
		}

		var args = ArrayPool<object>.Shared.Rent( m.Parameters.Length );

		try
		{
			for ( int i = 0; i < m.Parameters.Length; i++ )
			{
				args[i] = null;

				if ( b.Arguments == null || i >= b.Arguments.Count )
					continue;

				var value = Eval( ctx, b.Arguments[i] );
				args[i] = ToType( value, m.Parameters[i].ParameterType );
			}

			var returnedValue = m.InvokeWithReturn<object>( targetInstance, args );

			if ( m.ReturnType != typeof( void ) && !string.IsNullOrEmpty( b.ReturnVariable ) )
			{
				SetVariable( ctx, b.ReturnVariable, returnedValue );
			}
		}
		finally
		{
			ArrayPool<object>.Shared.Return( args, clearArray: true );
		}
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private object Eval( RunContext ctx, Expression e )
	{
		if ( e == null ) return null;

		if ( e is LiteralExpression le ) return le.LiteralValue.Value;
		if ( e is VariableExpression ve ) return GetVariable( ctx, ve.VariableName );

		return null;
	}

	static float ToFloat( object o )
	{
		if ( o == null ) return 0;
		if ( o is float f ) return f;
		if ( o is double d ) return (float)d;
		if ( o is int i ) return i;
		if ( o is long l ) return l;
		if ( o is string s && float.TryParse( s, out var result ) ) return result;
		return 0;
	}

	static object ToType( object o, Type t )
	{
		if ( t == typeof( string ) ) return o?.ToString() ?? "";
		if ( t == typeof( int ) ) return (int)ToFloat( o );
		if ( t == typeof( long ) ) return (long)ToFloat( o );
		if ( t == typeof( double ) ) return (double)ToFloat( o );
		if ( t == typeof( float ) ) return ToFloat( o );
		if ( t == typeof( GameObject ) ) return ToGameObject( o );
		if ( t == typeof( bool ) ) return ToBool( o );

		return o;
	}

	static bool ToBool( object o )
	{
		if ( o == null ) return false;
		if ( o is bool b ) return b;
		if ( o is string s ) return s.ToBool();
		if ( o is float f ) return f != 0.0f;
		if ( o is double d ) return d != 0.0;
		if ( o is int i ) return i != 0;
		if ( o is long l ) return l != 0;
		return true;
	}

	static GameObject ToGameObject( object o )
	{
		if ( o == null ) return null;
		if ( o is GameObject go ) return go;
		if ( o is Component c ) return c?.GameObject;

		return null;
	}
}
