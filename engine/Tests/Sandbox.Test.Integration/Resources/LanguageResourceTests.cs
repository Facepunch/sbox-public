using Sandbox.Engine;
using Sandbox.Resources;
using System.Text;

namespace ResourceTests;

/// <summary>
/// Exercises compiled language resources through the game resource loader.
/// </summary>
[TestClass, DoNotParallelize]
public class LanguageResourceTests
{
	/// <summary>
	/// Asset-local and legacy phrases share locale selection, fallback and formatting.
	/// A watched edit replaces the file's previous contributions, and deletion restores the underlying catalog.
	/// </summary>
	[TestMethod]
	public void LanguageAssetsFollowTheSelectedLanguageAndFileChanges()
	{
		ThreadSafe.MarkMainThread();
		const string path = "items/flashlight.lang_c";
		var files = new MemoryFileSystem();
		var context = new GlobalContext
		{
			TypeLibrary = SharedTypeLibrary.Engine,
			FileMount = files,
			JsonSerializerOptions = GlobalContext.Current.JsonSerializerOptions
		};
		using var scope = new GlobalContext.GlobalContextScope( context );
		var previousLanguage = Application.LanguageCode;
		var legacy = new MemoryFileSystem();
		context.Language = new LanguageContainer();
		context.Language.FileSystem.Mount( legacy );
		context.UISystem = new UISystem();

		try
		{
			legacy.WriteAllText( "de/items.json", """{ "light.name": "Alt", "light.on": "An" }""" );
			WriteCompiled( """
				{
					"name": { "en": "Flashlight", "DE": "Taschenlampe" },
					"battery": { "@context": "Remaining battery percentage.", "en": "{charge}% left" },
					"on": { "en": "On" },
					"__references": [],
					"@prefix": "light"
				}
				""" );

			ResourceLoader.LoadAllGameResource( files );
			var resource = ResourceLibrary.Get<LanguageResource>( "items/flashlight.lang" );
			Assert.IsNotNull( resource );
			var saved = resource.Serialize();
			Assert.AreEqual( "light", (string)saved["@prefix"] );
			Assert.AreEqual( "Remaining battery percentage.", (string)saved["battery"]["@context"] );
			Application.LanguageCode = "de";
			context.Language.Tick();
			Assert.AreEqual( "Taschenlampe", Language.GetPhrase( "light.name" ) );
			Assert.AreEqual( "name", Language.GetPhrase( "name" ) );
			Assert.AreEqual( "50% left", Language.GetPhrase( "light.battery", new() { ["charge"] = 50 } ) );
			Assert.AreEqual( "An", Language.GetPhrase( "light.on" ) );

			Application.LanguageCode = "@context";
			context.Language.Tick();
			Assert.AreEqual( "{charge}% left", Language.GetPhrase( "light.battery" ) );

			Application.LanguageCode = "en";
			context.Language.Tick();
			Assert.AreEqual( "Flashlight", Language.GetPhrase( "light.name" ) );

			WriteCompiled( """{ "light.name": { "en": "Torch" } }""" );
			RefreshFile();
			Assert.AreEqual( "Torch", Language.GetPhrase( "light.name" ) );
			Assert.AreEqual( "light.battery", Language.GetPhrase( "light.battery" ) );

			files.DeleteFile( path );
			RefreshFile();
			Assert.AreEqual( "light.name", Language.GetPhrase( "light.name" ) );
			Application.LanguageCode = "de";
			context.Language.Tick();
			Assert.AreEqual( "Alt", Language.GetPhrase( "light.name" ) );
		}
		finally
		{
			context.Language.Shutdown();
			context.Shutdown();
			files.Dispose();
			legacy.Dispose();
			Application.LanguageCode = previousLanguage;
		}

		void WriteCompiled( string json )
		{
			var writer = new ResourceWriter();
			writer.SetDataBlock( Encoding.UTF8.GetBytes( json ) );
			files.WriteAllBytes( path, writer.ToArray() );
		}

		void RefreshFile()
		{
			files.AddChangedFile( path );
			FileWatch.TimeSinceLastChange = 1;
			FileWatch.Tick();
			context.Language.Tick();
		}
	}
}
