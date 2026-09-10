using System.ComponentModel.DataAnnotations;
using Sandbox.Services;

namespace Sandbox;

public enum LicenseType
{
	None,

	[Display( Name = "CC0 (Public Domain)", ShortName = "public" )]
	CC0,

	[Display( Name = "CC BY-NC-ND (Restricted)", ShortName = "lock" )]
	CCBYNCND,

	[Display( Name = "CC BY (Attribution)", ShortName = "person" )]
	CCBY,

	[Display( Name = "CC BY-SA (Share Alike)", ShortName = "loop" )]
	CCBYSA
}

public static class Licensing
{
	public static PackageLicense[] Assets = new PackageLicense[]
	{
		new ()
		{
			Type = LicenseType.CC0,
			Name = "CC0",
			Title = "CC0",
			Icon = "https://licensebuttons.net/l/zero/1.0/88x31.png",
			Description = "CC0 allows reusers to distribute, remix, adapt, and build upon the material in any medium or format, with no conditions.",
			Url = "https://creativecommons.org/publicdomain/zero/1.0/"
		},

		new ()
		{
			Type = LicenseType.CCBYNCND,
			Name = "CC_BYNCND",
			Title = "CC BY-NC-ND",
			Icon = "https://licensebuttons.net/l/by-nc-nd/4.0/88x31.png",
			Description = "Allows reusers to copy and distribute the material in any medium or format in unadapted form only, for noncommercial purposes only, and only so long as attribution is given to the creator.",
			Url = "https://creativecommons.org/licenses/by-nc-nd/4.0/"
		},

		new ()
		{
			Type = LicenseType.CCBY,
			Name = "CC_BY",
			Title = "CC BY",
			Icon = "https://licensebuttons.net/l/by/4.0/88x31.png",
			Description = "Allows reusers to distribute, remix, adapt, and build upon the material in any medium or format, so long as attribution is given to the creator. The license allows for commercial use.",
			Url = "https://creativecommons.org/licenses/by-nc-nd/4.0/"
		},

		new ()
		{
			Type = LicenseType.CCBYSA,
			Name = "CC_BYSA",
			Title = "CC BY-SA",
			Icon = "https://licensebuttons.net/l/by-sa/4.0/88x31.png",
			Description = "Allows reusers to copy and distribute the material in any medium or format in unadapted form only, for noncommercial purposes only, and only so long as attribution is given to the creator.",
			Url = "https://creativecommons.org/licenses/by-sa/4.0/"
		},
	};
}
