using Sandbox;

public static class ScriptUsage
{
	public static bool Draw( Script script, Painter painter ) => script.With( "painter", painter ).With( "amount", 10 ).Run();

	public static bool Compile( Script script ) => script.With( "amount", 10 ).Compile();

	public static int EvaluateInputs( Script script ) => script.With( "amount", 10 ).RunAndReturn<int>();

	public static double Run() => Game.Scripting.Run<double>( "return Math.Sin(0);" );

	public static string Complete()
	{
		Script.Analysis analysis = Game.Scripting.Analyze( "Player.He", new[] { new Script.Input( "Player", typeof( GameObject ) ) } );
		Script.Token? token = analysis.GetToken( 0 );
		Script.Symbol? symbol = analysis.GetSymbol( 0 );
		Script.CompletionList suggestions = analysis.GetCompletions( analysis.Source.Length );
		return suggestions.Items.Count > 0 ? suggestions.Items[0].InsertText : null;
	}

	public static float Evaluate()
	{
		Script script = Game.Scripting.CreateScript( "return Strength * 2.0f;" );
		script.SetArgument( "Strength", 10.0f );
		float result = script.RunAndReturn<float>();
		Script.ScriptError error = script.LastError;
		return error is null ? result : 0;
	}
}
