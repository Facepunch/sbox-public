using Sandbox.Npcs.Layers;

namespace Sandbox.Npcs;

public partial class Npc : Component
{
	public void DrawDebugString()
	{
		var bounds = GameObject.GetBounds();
		var worldpos = WorldPosition + Vector3.Up * bounds.Size.z;
		var pos = Scene.Camera.PointToScreenPixels( worldpos, out var behind );

		if ( behind ) return;

		var str = $"{ActiveSchedule?.GetDebugString()}";

		str += $"\nFaction: {Faction}";

		if ( Awareness != NpcAwareness.None )
			str += $"\nAwareness: {Awareness}";

		// Collect debug output from all layers
		foreach ( var layer in GetComponents<BaseNpcLayer>() )
		{
			var layerDebug = layer.GetDebugString();
			if ( !string.IsNullOrEmpty( layerDebug ) )
			{
				str += $"\n{layerDebug}";
			}
		}

		var text = TextRendering.Scope.Default;
		text.Text = str;
		text.FontSize = 13;
		text.FontName = "Poppins";
		text.FontWeight = 600;
		text.TextColor = Color.Yellow;
		text.Outline = new TextRendering.Outline { Color = Color.Black, Size = 4, Enabled = true };
		text.FilterMode = Rendering.FilterMode.Point;

		DebugOverlay.ScreenText( pos, text, TextFlag.LeftBottom );
	}
}
