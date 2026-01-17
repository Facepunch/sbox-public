using System.Text;

namespace Sandbox.SolutionGenerator;

/// <summary>
/// Class to produce the template output
/// </summary>
internal partial class Project
{
	/// <summary>
	/// Create the template output
	/// </summary>
	public virtual string TransformText()
	{
		var sb = new StringBuilder();

		sb.AppendLine( "<Project Sdk=\"Microsoft.NET.Sdk.Razor\">" );
		sb.AppendLine( "" );

		sb.AppendLine( $"	<PropertyGroup>" );
		sb.AppendLine( $"		<TargetFramework>net10.0</TargetFramework>" );
		sb.AppendLine( $"		<GenerateDocumentationFile>true</GenerateDocumentationFile>" );
		sb.AppendLine( $"		<AssemblyName>{ProjectName}</AssemblyName>" );
		sb.AppendLine( $"		<PackageId>{ProjectName}</PackageId>" );
		sb.AppendLine( $"		<LangVersion>14</LangVersion>" );
		sb.AppendLine( $"		<NoWarn>{NoWarn}</NoWarn>" );
		sb.AppendLine( $"		<WarningsAsErrors></WarningsAsErrors>" );
		sb.AppendLine( $"		<TreatWarningsAsErrors>{TreatWarningsAsErrors}</TreatWarningsAsErrors>" );
		sb.AppendLine( $"		<AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>" );
		sb.AppendLine( $"		<DefineConstants>{DefineConstants}</DefineConstants>" );
		sb.AppendLine( $"		<AllowUnsafeBlocks>{Unsafe}</AllowUnsafeBlocks>" );
		sb.AppendLine( $"		<OutputPath>{GameRoot}/.vs/output/</OutputPath>" );
		sb.AppendLine( $"		<DocumentationFile>{GameRoot}/.vs/output/{ProjectName}.xml</DocumentationFile>" );
		sb.AppendLine( $"		<RootNamespace>{RootNamespace}</RootNamespace>" );
		sb.AppendLine( $"		<Nullable>{Nullable}</Nullable>" );
		foreach ( var entry in PropertyGroupExtras )
		{
			sb.AppendLine( $"		{entry}" );
		}
		sb.AppendLine( $"	</PropertyGroup>" );
		sb.AppendLine( $"" );

		if ( IsUnitTestProject )
		{
			sb.AppendLine( $"	<ItemGroup>" );
			sb.AppendLine( $"		<PackageReference Include=\"Microsoft.NET.Test.Sdk\" Version=\"17.7.2\" /> " );
			sb.AppendLine( $"		<PackageReference Include=\"MSTest.TestAdapter\" Version=\"3.1.1\" /> " );
			sb.AppendLine( $"		<PackageReference Include=\"MSTest.TestFramework\" Version=\"3.1.1\" /> " );
			sb.AppendLine( $"		<PackageReference Include=\"coverlet.collector\" Version=\"6.0.0\" /> " );
			sb.AppendLine( $"	</ItemGroup>" );
			sb.AppendLine( $"" );
		}

		if ( ProjectName == "Base Library" )
		{
			sb.AppendLine( $"	<ItemGroup>" );
			sb.AppendLine( $"		<EmbeddedResource Include=\"../shaders/**/*.shader\" LinkBase=\"Shaders (Embedded)\" /> " );
			sb.AppendLine( $"		<EmbeddedResource Include=\"../shaders/**/*.hlsl\" LinkBase=\"Shaders (Embedded)\" /> " );
			sb.AppendLine( $"	</ItemGroup>" );
			sb.AppendLine( $"" );
		}

		if ( GlobalStatic.Count > 0 || GlobalUsing.Count > 0 )
		{
			sb.AppendLine( $"	<ItemGroup>" );

			foreach ( var entry in GlobalStatic )
			{
				sb.AppendLine( $"		<Using Include=\"{entry}\" Static=\"true\" />" );
			}

			foreach ( var entry in GlobalUsing )
			{
				sb.AppendLine( $"		<Using Include=\"{entry}\" />" );
			}

			sb.AppendLine( $"	</ItemGroup>" );
			sb.AppendLine( $"" );
		}

		{
			sb.AppendLine( $"	<ItemGroup>" );
			sb.AppendLine( $"		<Analyzer Include=\"{ManagedRoot}/Sandbox.CodeUpgrader.dll\"/> " );
			sb.AppendLine( $"		<Analyzer Include=\"{ManagedRoot}/Sandbox.Generator.dll\"/> " );

			foreach ( var entry in References )
			{
				sb.AppendLine( $"		<Reference Include=\"{ManagedRoot}/{entry}\"/> " );
			}

			sb.AppendLine( $"	</ItemGroup>" );
			sb.AppendLine( $"" );
		}

		if ( !string.IsNullOrWhiteSpace( ProjectReferences ) )
		{
			sb.AppendLine( $"	<ItemGroup>" );
			sb.AppendLine( $"		{ProjectReferences.Trim()}" );
			sb.AppendLine( $"	</ItemGroup>" );
			sb.AppendLine( $"" );
		}

		sb.AppendLine( "</Project>" );

		return sb.ToString();
	}
}
