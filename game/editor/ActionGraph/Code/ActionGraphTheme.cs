using Editor.NodeEditor;
using Facepunch.ActionGraphs;
using Sandbox;
using System;
using System.Collections.Generic;

namespace Editor.ActionGraphs;

internal static class ActionGraphTheme
{
	public static Color ActionColor { get; private set; }
	public static Color AsyncActionColor { get; private set; }
	public static Color ExpressionColor { get; private set; }

	public static Dictionary<Type, HandleConfig> HandleConfigs { get; private set; }

	static ActionGraphTheme()
	{
		Update();
	}

	[Event( "hotloaded" )]
	static void Update()
	{
		ExpressionColor = Theme.Blue.AdjustHue( -85 ).Desaturate( 0.2f );
		ActionColor = Theme.Blue;
		AsyncActionColor = Theme.Blue.AdjustHue( 25 );

		HandleConfigs = new()
		{
			{ typeof(Signal), new HandleConfig( "Signal", Color.White, HandleShape.Arrow ) },
			{ typeof(GameObject), new HandleConfig( null, Theme.Blue ) },
			{ typeof(Component), new HandleConfig( null, Theme.Green ) },
			{ typeof(Resource), new HandleConfig( null, Theme.Pink ) },
			{ typeof(float), new HandleConfig( "float", Color.Parse( "#8ec07c" )!.Value ) },
			{ typeof(double), new HandleConfig( "double", Color.Parse( "#8ec07c" )!.Value ) },
			{ typeof(int), new HandleConfig( "int", Color.Parse( "#ce67e0" )!.Value ) },
			{ typeof(uint), new HandleConfig( "uint", Color.Parse( "#ce67e0" )!.Value ) },
			{ typeof(Enum), new HandleConfig( null, Color.Parse( "#ce67e0" )!.Value ) },
			{ typeof(bool), new HandleConfig( "bool", Theme.Blue.AdjustHue( -80 ) ) },
			{ typeof(Vector2), new HandleConfig( "Vector2", Color.Parse( "#7177e1" )!.Value ) },
			{ typeof(Vector3), new HandleConfig( "Vector3", Color.Parse( "#7177e1" )!.Value ) },
			{ typeof(Vector4), new HandleConfig( "Vector4", Color.Parse( "#7177e1" )!.Value ) },
			{ typeof(Rotation), new HandleConfig( "Rotation", Color.Parse( "#7177e1" )!.Value ) },
			{ typeof(string), new HandleConfig( "string", Color.Parse( "#c7ae32" )!.Value ) },
			{ typeof(IEnumerable<>), new HandleConfig( "Enumerable", Color.Parse( "#E08327" )!.Value )}
		};
	}

}
