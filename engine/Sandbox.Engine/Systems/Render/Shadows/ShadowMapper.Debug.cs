namespace Sandbox.Rendering;

internal partial class ShadowMapper
{
	internal static void Draw( ref Vector2 pos, HudPainter Hud )
	{
		var x = pos.x;
		var y = pos.y;

		var header = new TextRendering.Scope( "", Color.White, 12, "Roboto Mono", 700 );
		header.Outline = new TextRendering.Outline { Color = Color.Black, Enabled = true, Size = 2 };

		var scope = new TextRendering.Scope( "", Color.White, 11, "Roboto Mono", 600 );
		scope.Outline = new TextRendering.Outline { Color = Color.Black, Enabled = true, Size = 2 };

		var dimScope = new TextRendering.Scope( "", Color.White.WithAlpha( 0.5f ), 11, "Roboto Mono", 600 );
		dimScope.Outline = new TextRendering.Outline { Color = Color.Black, Enabled = true, Size = 2 };

		scope.Text = $"Shadow Memory Allocated: {MemorySize.FormatBytes()}";
		scope.TextColor = new Color( 0.6f, 0.9f, 1f );
		Hud.DrawText( scope, new Vector2( x, y ), TextFlag.LeftTop );
		y += 14;

		scope.Text = $"Directional Shadow Memory: {DirectionalShadowMemorySize.FormatBytes()}";
		scope.TextColor = new Color( 0.6f, 0.9f, 1f );
		Hud.DrawText( scope, new Vector2( x, y ), TextFlag.LeftTop );
		y += 14;

		scope.Text = $"Max Resolution: {ShadowMapper.MaxResolution}";
		scope.TextColor = new Color( 0.6f, 0.9f, 1f );
		Hud.DrawText( scope, new Vector2( x, y ), TextFlag.LeftTop );
		y += 14;

		scope.Text = $"Filter Quality: {ShadowMapper.ShadowFilter}";
		scope.TextColor = new Color( 0.6f, 0.9f, 1f );
		Hud.DrawText( scope, new Vector2( x, y ), TextFlag.LeftTop );
		y += 14;
		y += 14;

		scope.Text = $"Shadow Maps In Cache: {ShadowMapper.Cache.Count()}";
		scope.TextColor = new Color( 0.6f, 0.9f, 1f );
		Hud.DrawText( scope, new Vector2( x, y ), TextFlag.LeftTop );
		y += 14;

		scope.Text = $"Shadow Maps Rendered This Frame: {ShadowMapper.Cache.Where( x => x.Value.LastFrame > (RealTime.Now - 0.01f) ).Count()}";
		scope.TextColor = new Color( 0.6f, 0.9f, 1f );
		Hud.DrawText( scope, new Vector2( x, y ), TextFlag.LeftTop );
		y += 14;

		scope.Text = $"Shadow Maps Used This Frame: {ShadowMapper.Cache.Where( x => x.Value.LastFrame > (RealTime.Now - 0.01f) ).Count()}";
		scope.TextColor = new Color( 0.6f, 0.9f, 1f );
		Hud.DrawText( scope, new Vector2( x, y ), TextFlag.LeftTop );
		y += 14;

		scope.Text = $"Projected Shadows Rendered: {ShadowMapper.ProjectedShadowsRenderedLastFrame}";
		scope.TextColor = new Color( 0.6f, 1f, 0.6f );
		Hud.DrawText( scope, new Vector2( x, y ), TextFlag.LeftTop );
		y += 14;

		scope.Text = $"Projected Shadows Screen Size Culled: {ShadowMapper.ProjectedShadowsCulledLastFrame}";
		scope.TextColor = new Color( 1f, 0.6f, 0.6f );
		Hud.DrawText( scope, new Vector2( x, y ), TextFlag.LeftTop );
		y += 14;
		y += 14;

		scope.Text = $"In Cache";
		scope.TextColor = new Color( 0.6f, 0.9f, 1f );
		Hud.DrawText( scope, new Vector2( x, y ), TextFlag.LeftTop );
		y += 14;
		x += 14;

		foreach ( var (k, v) in ShadowMapper.Cache.OrderByDescending( x => x.Value.LastFrame ).ThenBy( x => x.Value.ScreenSize ).Take( 20 ) )
		{
			Color textColor = Color.Yellow;

			if ( v.LastFrame < RealTime.Now - 0.1f )
				textColor = Color.White.Darken( 0.4f );

			scope.Text = $"{k.GetType().Name} - {v.ShadowMap.Size.x}px {g_pRenderDevice.ComputeTextureMemorySize( v.ShadowMap.native ).FormatBytes()} (Screen Size: {v.ScreenSize * 100:F0}%)";
			scope.TextColor = textColor;
			Hud.DrawText( scope, new Vector2( x, y ), TextFlag.LeftTop );
			y += 14;
		}

		pos.y = y;

		// Draw world-space overlays on each local light with shadow info
		var camera = Application.GetActiveScene()?.Camera;
		if ( camera is not null )
		{
			const float previewSize = 96;
			var labelScope = new TextRendering.Scope( "", Color.White, 10, "Roboto Mono", 600 );
			labelScope.Outline = new TextRendering.Outline { Color = Color.Black, Enabled = true, Size = 2 };

			foreach ( var (light, entry) in ShadowMapper.Cache )
			{
				if ( entry.ShadowMap is null ) continue;
				if ( !light.IsValid() ) continue;

				var screenPos = camera.PointToScreenPixels( light.Position, out bool isBehind );
				if ( isBehind ) continue;

				// Shadow map preview centered above the light
				var texRect = new Rect( screenPos.x - previewSize * 0.5f, screenPos.y - previewSize - 8, previewSize, previewSize );
				Hud.DrawTexture( entry.ShadowMap, texRect );

				// Info text below the preview
				bool active = entry.LastFrame > RealTime.Now - 0.1f;
				var textColor = active ? Color.Yellow : Color.White.Darken( 0.4f );
				labelScope.TextColor = textColor;

				var textPos = new Vector2( screenPos.x, texRect.Bottom + 4 );

				string lightType = light.lightNative.GetLightType() switch { 1 => "Point", 3 => "Spot", _ => "Light" };
				var memSize = g_pRenderDevice.ComputeTextureMemorySize( entry.ShadowMap.native );

				labelScope.Text = $"{lightType} {entry.CurrentResolution}px ({memSize.FormatBytes()})";
				Hud.DrawText( labelScope, textPos, TextFlag.CenterTop );
				textPos.y += 12;

				labelScope.Text = $"R:{light.Radius:F0} Screen:{entry.ScreenSize * 100:F0}% Bias:{light.ShadowBias}";
				Hud.DrawText( labelScope, textPos, TextFlag.CenterTop );
				textPos.y += 12;

				float halfAngle = light.lightNative.GetLightType() == 3 ? light.lightNative.GetPhi() : 45f;
				float biasScale = ComputeBiasScale( halfAngle, light.Radius, entry.CurrentResolution );
				labelScope.Text = $"BiasScale:{biasScale:F2} Const:{(int)(ShadowDepthBias * biasScale)} Slope:{ShadowSlopeScale * biasScale:F1}";
				Hud.DrawText( labelScope, textPos, TextFlag.CenterTop );
			}
		}

		// Draw CSM cascade textures at the bottom of the screen
		if ( CascadeDebugCount > 0 )
		{
			var margin = 8;
			var size = 160;
			var texY = Screen.Height - size - margin;

			for ( int i = 0; i < CascadeDebugCount; i++ )
			{
				var info = CascadeDebugInfos[i];
				if ( info.DepthTexture is null ) continue;

				var texX = margin + i * (size + margin);
				Hud.DrawTexture( info.DepthTexture, new Rect( texX, texY, size, size ) );
				Hud.DrawText( $"Cascade {i}", 11, Color.Yellow, new Vector2( texX, texY - 48 ), TextFlag.LeftTop );
				Hud.DrawText( $"Depth: {info.Near:F0} to {info.Far:F0}", 11, Color.Yellow, new Vector2( texX, texY - 32 ), TextFlag.LeftTop );
				Hud.DrawText( $"Rect: {info.Width:F0} x {info.Height:F0} units", 11, Color.Yellow, new Vector2( texX, texY - 16 ), TextFlag.LeftTop );
			}
		}
	}
}
