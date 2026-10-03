using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Sandbox.SolutionGenerator
{
	public class LaunchSettings
	{
		public class Profile
		{
			[JsonPropertyName( "commandName" )]
			public string CommandName { get; set; }

			[JsonPropertyName( "executablePath" )]
			public string ExecutablePath { get; set; }

			[JsonPropertyName( "commandLineArgs" )]
			public string CommandLineArgs { get; set; }

			[JsonPropertyName( "workingDirectory" )]
			public string WorkingDirectory { get; set; }
		}

		[JsonPropertyName( "profiles" )]
		public Dictionary<string, Profile> Profiles { get; set; }
	}
}
