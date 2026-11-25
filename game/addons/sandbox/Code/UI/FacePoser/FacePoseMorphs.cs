namespace Sandbox;

/// <summary>
/// Morph names that drive the same facial feature on Human, Citizen and Source rigs.
/// </summary>
internal static class FacePoseMorphs
{
	internal static string[] BrowUp( string side ) => [$"browOuterUp_{side}", $"outerbrowraiser{side}", $"AU2{side}"];
	internal static string[] BrowDown( string side ) => [$"browDown_{side}", $"browlowerer{side}", $"AU4{side}"];
	internal static string[] LidUp( string side ) => [$"eyeWide_{side}", side == "L" ? "upper_left" : "upper_right"];
	internal static string[] LidDown( string side ) => [$"eyeBlink_{side}", side == "L" ? "lower_left" : "lower_right"];
	internal static string[] Smile( string side ) => [$"mouthSmile_{side}", $"lipcornerpuller{side}", $"AU12{side}"];
	internal static string[] Frown( string side ) => [$"mouthFrown_{side}", $"lipcornerdepressor{side}", $"AU15{side}"];
	internal static readonly string[] OpenJaw = ["jawOpen", "openjawL", "openjawR", "AU26L", "AU26R"];

	internal static (float Left, float Right) SplitStrength( float strength, float balance )
	{
		return (strength * (1f + balance).Clamp( 0f, 1f ), strength * (1f - balance).Clamp( 0f, 1f ));
	}

	internal static Dictionary<string, float> Transfer( Model source, Dictionary<string, float> pose, Model target )
	{
		var from = source.Morphs.Names.ToDictionary( n => n, n => pose.GetValueOrDefault( n ), StringComparer.OrdinalIgnoreCase );
		var dest = target.Morphs.Names.ToHashSet( StringComparer.OrdinalIgnoreCase );
		var result = dest.Where( from.ContainsKey ).ToDictionary( n => n, n => from[n], StringComparer.OrdinalIgnoreCase );

		foreach ( var side in new[] { "L", "R" } )
		{
			Copy( BrowUp( side ) );
			Copy( BrowDown( side ) );
			Copy( LidUp( side ) );
			Copy( LidDown( side ) );
			Copy( Smile( side ) );
			Copy( Frown( side ) );
			Copy( [$"openjaw{side}", $"AU26{side}"] );
			Copy( [$"mouthStretch_{side}", $"AU20{side}"] );
			Copy( [$"mouthUpperUp_{side}", $"upperlipraiser{side}", $"AU10{side}"] );
			Copy( [$"cheekSquint_{side}", $"AU6{side}"] );
			Copy( [$"noseSneer_{side}", $"nosewrinkler{side}", $"AU9{side}"] );
			Copy( [$"innerbrowraiser{side}", $"AU1{side}"] );
		}
		Copy( OpenJaw );
		Copy( ["browInnerUp", "innerbrowraiserL", "innerbrowraiserR", "AU1L", "AU1R"] );
		Copy( ["mouthPucker", "lippuckerer", "AU18L", "AU18R"] );
		return result;

		void Copy( string[] names )
		{
			var weights = names.Where( from.ContainsKey ).Select( n => from[n] ).ToArray();
			if ( weights.Length == 0 ) return;
			var average = weights.Average();
			foreach ( var name in names )
				if ( dest.TryGetValue( name, out var actual ) ) result.TryAdd( actual, average );
		}
	}
}
