namespace Sandbox;

public sealed partial class SceneWorld
{
	/// <summary>
	/// Skies in creation order, matching CSceneSystem::CreateSkyBox's increasing depth-sort bias.
	/// </summary>
	internal List<SceneSkyBox> Skies { get; } = new();

	/// <summary>
	/// The 3D skybox made last for this world. Removing any skybox clears it.
	/// </summary>
	internal SceneSkybox3D ActiveSkybox { get; set; }

	/// <summary>
	/// The active 3D skybox, gated by CCameraRenderer::CreateView and Add3DSkyboxLayers.
	/// A child with no 2D sky still replaces the parent sky.
	/// </summary>
	internal SceneSkybox3D GetRenderingSkybox()
	{
		return ActiveSkybox is { IsValid: true, Scale: > 0 } skybox && ConVarSystem.GetNativeInt( "r_3d_skybox", 1 ) != 0
			? skybox : null;
	}

	/// <summary>
	/// Select the newest visible sky, as CSkyBoxObjectDesc::DrawArray does.
	/// </summary>
	internal SceneSkyBox GetSky( ITagSet renderTags = null, ITagSet excludeTags = null, ITagSet viewExcludeTags = null )
	{
		// Camera tag sets cache their tokens; avoid enumerators and string lookups on every view.
		var include = renderTags?.GetTokens();
		var exclude = excludeTags?.GetTokens();
		var viewExclude = viewExcludeTags?.GetTokens();
		var filterInclusion = include is { Count: > 0 };
		var filterExclusion = exclude is { Count: > 0 } || viewExclude is { Count: > 0 };

		lock ( Skies )
		{
			for ( int i = Skies.Count - 1; i >= 0; i-- )
			{
				var sky = Skies[i];
				if ( !sky.IsValid() || !sky.RenderingEnabled )
					continue;

				if ( filterInclusion || filterExclusion )
				{
					var included = !filterInclusion;
					var excluded = false;
					var tags = sky.TagTokens;
					for ( int tagIndex = 0; tagIndex < tags.Count; tagIndex++ )
					{
						var tag = tags[tagIndex];
						if ( filterExclusion && (exclude?.Contains( tag ) == true || viewExclude?.Contains( tag ) == true) )
						{
							excluded = true;
							break;
						}

						included |= filterInclusion && include.Contains( tag );
					}

					if ( !included || excluded )
						continue;
				}

				return sky;
			}
		}

		return null;
	}
}
