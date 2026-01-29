namespace Editor.ShaderGraph;

internal static class ShaderGraphTheme
{
	public static Dictionary<Type, HandleConfig> HandleConfigs { get; private set; }
	public static Dictionary<Type, BlackboardConfig> BlackboardConfigs { get; private set; }

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
			{ typeof( Texture ), new HandleConfig( "Texture2D", Color.Parse( "#ffb3a7" )!.Value ) },
			{ typeof( Sampler ), new HandleConfig( "Sampler", Color.Parse( "#dddddd" )!.Value ) },
			{ typeof( Gradient ), new HandleConfig( "Gradient", Color.Parse( "#dddddd" )!.Value ) },

		};

		BlackboardConfigs = new()
		{
			{ typeof( BoolBlackboardParameter ), new BlackboardConfig( "bool", HandleConfigs[typeof( bool )].Color ) },
			{ typeof( IntBlackboardParameter ), new BlackboardConfig( "int", HandleConfigs[typeof( int )].Color ) },
			{ typeof( FloatBlackboardParameter ), new BlackboardConfig( "float", HandleConfigs[typeof( float )].Color ) },
			{ typeof( Float2BlackboardParameter ), new BlackboardConfig( "float2", HandleConfigs[typeof( Vector2 )].Color ) },
			{ typeof( Float3BlackboardParameter ), new BlackboardConfig( "float3", HandleConfigs[typeof( Vector3 )].Color ) },
			{ typeof( Float4BlackboardParameter ), new BlackboardConfig( "float4", HandleConfigs[typeof( Vector4 )].Color ) },
			{ typeof( ColorBlackboardParameter ), new BlackboardConfig( "float4", HandleConfigs[typeof( Color )].Color ) },
			{ typeof( Texture2DBlackboardParameter ), new BlackboardConfig( "Texture2D", HandleConfigs[typeof( Texture )].Color ) },

			{ typeof( BoolSubgraphInputBlackboardParameter ), new BlackboardConfig( "bool", HandleConfigs[typeof( bool )].Color ) },
			{ typeof( IntSubgraphInputBlackboardParameter ), new BlackboardConfig( "int", HandleConfigs[typeof( int )].Color ) },
			{ typeof( FloatSubgraphInputBlackboardParameter ), new BlackboardConfig( "float", HandleConfigs[typeof( float )].Color ) },
			{ typeof( Float2SubgraphInputBlackboardParameter ), new BlackboardConfig( "float2", HandleConfigs[typeof( Vector2 )].Color ) },
			{ typeof( Float3SubgraphInputBlackboardParameter ), new BlackboardConfig( "float3", HandleConfigs[typeof( Vector3 )].Color ) },
			{ typeof( Float4SubgraphInputBlackboardParameter ), new BlackboardConfig( "float4", HandleConfigs[typeof( Vector4 )].Color ) },
			{ typeof( ColorSubgraphInputBlackboardParameter ), new BlackboardConfig( "float4", HandleConfigs[typeof( Color )].Color ) },
			{ typeof( Texture2DSubgraphInputBlackboardParameter ), new BlackboardConfig( "Texture2D", HandleConfigs[typeof( Texture )].Color ) },
		};
	}
}
