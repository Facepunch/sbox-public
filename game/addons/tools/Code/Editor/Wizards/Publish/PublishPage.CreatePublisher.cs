namespace Editor.Wizards;

partial class PublishWizard
{
	/// <summary>
	/// Build the publisher for this project with everything the earlier pages produced (compiled code, the
	/// packages it references), let <paramref name="configure"/> have its say, and ask the backend what it
	/// needs uploading.
	/// </summary>
	static async Task<ProjectPublisher> CreatePublisher( Project project, PublishConfig config, Func<ProjectPublisher, Task> configure = null )
	{
		var publisher = await ProjectPublisher.FromProject( project );

		if ( project.Config.Type == "game" )
		{
			var settings = publisher.GetGameSettings( config.CompilerOutput );
			publisher.SetMeta( "GameSettings", settings );
		}

		if ( config.AssemblyFiles is not null )
		{
			foreach ( var f in config.AssemblyFiles )
			{
				if ( f.Value is byte[] bytes )
				{
					await publisher.AddFile( bytes, f.Key );
				}

				if ( f.Value is string json )
				{
					await publisher.AddFile( json, f.Key );
				}
			}
		}

		// Add assets from Cloud.Model( .. ) etc
		foreach ( var package in config.CodePackages )
		{
			await publisher.AddCodePackageReference( package );
		}

		if ( configure is not null )
		{
			await configure( publisher );
		}

		await publisher.PrePublish();

		return publisher;
	}
}
