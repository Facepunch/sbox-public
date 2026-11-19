namespace Sandbox;

public sealed partial class SceneWorld
{
	/// <summary>
	/// Skies in creation order, matching CSceneSystem::CreateSkyBox's increasing depth-sort bias.
	/// </summary>
	internal List<SceneSkyBox> Skies { get; } = new();

	/// <summary>
	/// The child world registered by CSceneWorld::Add3DSkyboxWorld. Removing any skybox clears it.
	/// </summary>
	internal SceneSkybox3D ActiveSkybox { get; set; }

	/// <summary>
	/// The active 3D skybox, gated by CCameraRenderer::CreateView and Add3DSkyboxLayers.
	/// A child with no 2D sky still replaces the parent sky.
	/// </summary>
	internal SceneSkybox3D GetRenderingSkybox()
	{
		return ActiveSkybox is { IsValid: true, Scale: > 0 } skybox && SkyboxesOn() ? skybox : null;
	}

	// r_3d_skybox, read again when a convar was set, or every second or so for a change native made itself: a native
	// convar's value comes back as a new string each time
	static bool SkyboxesOn()
	{
		if ( skyboxesRead == 0 || skyboxesChanges != ConVarSystem.Changes || Application.FrameCount - skyboxesRead >= 60 )
		{
			(skyboxesRead, skyboxesChanges) = (Math.Max( Application.FrameCount, 1 ), ConVarSystem.Changes);
			skyboxesOn = ConVarSystem.GetInt( "r_3d_skybox", 1, true ) != 0;
		}

		return skyboxesOn;
	}

	static ulong skyboxesRead;
	static int skyboxesChanges;
	static bool skyboxesOn;

	/// <summary>
	/// Select the newest visible sky, as CSkyBoxObjectDesc::DrawArray does. Child views pass no object-tag filters.
	/// </summary>
	internal SceneSkyBox GetSky( ITagSet renderTags = null, ITagSet excludeTags = null )
	{
		// Camera tag sets cache their tokens; avoid enumerators and string lookups on every view.
		var include = renderTags?.GetTokens();
		var exclude = excludeTags?.GetTokens();
		var filterInclusion = include is { Count: > 0 };
		var filterExclusion = exclude is { Count: > 0 };

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
						if ( filterExclusion && exclude.Contains( tag ) )
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
