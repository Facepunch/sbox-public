namespace Editor.ShaderGraph;

internal static class ShaderGraphTheme
{
	public static Dictionary<Type, HandleConfig> HandleConfigs { get; private set; }

	static ShaderGraphTheme()
	{
		Update();
	}

	[Event( "hotloaded" )]
	static void Update()
	{
		HandleConfigs = new()
		{
			{ typeof( bool ), new HandleConfig( "bool", Theme.Blue.AdjustHue( -80 ) ) },
			{ typeof( int ), new HandleConfig( "int", Color.Parse( "#ce67e0" )!.Value.AdjustHue( -80 ) ) },
			{ typeof( float ), new HandleConfig( "Float", Color.Parse( "#8ec07c" )!.Value ) },
			{ typeof( Vector2 ), new HandleConfig( "Vector2", Color.Parse( "#ce67e0" )!.Value ) },
			{ typeof( Vector3 ), new HandleConfig( "Vector3", Color.Parse( "#7177e1" )!.Value ) },
			{ typeof( Vector4 ), new HandleConfig( "Vector4", Color.Parse( "#c7ae32" )!.Value ) },
			{ typeof( Color ), new HandleConfig( "Color", Color.Parse( "#c7ae32" )!.Value ) },
			{ typeof( Sampler ), new HandleConfig( "Sampler", Color.Parse( "#dddddd" )!.Value ) },
			{ typeof( Gradient ), new HandleConfig( "Gradient", Color.Parse( "#dddddd" )!.Value ) },
		};
	}
}
