using Sandbox.Engine;
using System.Threading.Tasks;

namespace ScriptingTests;

public partial class ScriptLoadingTests
{
	/// <summary>
	/// Menu package scans and subsequent file changes never load script resources or publish their declarations.
	/// </summary>
	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public async Task MenuSkipsScriptResources( bool asynchronous )
	{
		var previousMenu = GlobalContext.Menu;
		GlobalContext.Menu = context;
		GlobalContext.Game = previousGame;
		try
		{
			files.WriteAllText( "menu.csx", "class MenuScript : Component {}" );
			files.WriteAllText( "menu.server.csx", "class MenuServerScript : Component {}" );

			if ( asynchronous )
			{
				await ResourceLoader.LoadAllGameResourceAsync( files );
			}
			else
			{
				ResourceLoader.LoadAllGameResource( files );
			}

			Assert.IsNull( ResourceLibrary.Get<ScriptResource>( "menu.csx" ) );
			Assert.IsNull( ResourceLibrary.Get<ScriptResource>( "menu.server.csx" ) );

			files.WriteAllText( "menu.csx", "class ChangedMenuScript : Component {}" );
			files.WriteAllText( "added.csx", "class AddedMenuScript : Component {}" );
			DispatchResourceChanges( "menu.csx", "added.csx" );

			Assert.IsNull( ResourceLibrary.Get<ScriptResource>( "menu.csx" ) );
			Assert.IsNull( ResourceLibrary.Get<ScriptResource>( "added.csx" ) );
			Assert.IsNull( context.TypeLibrary.GetType( "MenuScript" ) );
			Assert.IsNull( context.TypeLibrary.GetType( "MenuServerScript" ) );
			Assert.IsNull( context.TypeLibrary.GetType( "ChangedMenuScript" ) );
			Assert.IsNull( context.TypeLibrary.GetType( "AddedMenuScript" ) );
		}
		finally
		{
			GlobalContext.Menu = previousMenu;
			GlobalContext.Game = context;
		}
	}
}
