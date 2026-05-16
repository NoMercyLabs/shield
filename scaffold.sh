#!/usr/bin/env bash
set -e
cd "$(dirname "$0")"

CLASSLIBS=(
  "Shield.Core"
  "Shield.Data"
  "Shield.Scanners"
  "Shield.Parsers.Npm"
  "Shield.Parsers.Nuget"
  "Shield.Parsers.Composer"
  "Shield.Parsers.Gradle"
  "Shield.Parsers.Os"
  "Shield.Feeds.Osv"
  "Shield.Feeds.Ghsa"
  "Shield.Feeds.NpmRegistry"
  "Shield.Feeds.DepsDev"
  "Shield.Feeds.Socket"
  "Shield.Feeds.TrivyDb"
  "Shield.Matcher"
  "Shield.Alerter"
  "Shield.Channels"
)

TEST_PROJECTS=(
  "Shield.Core.Tests"
  "Shield.Data.Tests"
  "Shield.Parsers.Npm.Tests"
  "Shield.Parsers.Nuget.Tests"
  "Shield.Parsers.Composer.Tests"
  "Shield.Parsers.Gradle.Tests"
  "Shield.Feeds.Osv.Tests"
  "Shield.Feeds.Ghsa.Tests"
  "Shield.Feeds.NpmRegistry.Tests"
  "Shield.Matcher.Tests"
  "Shield.Alerter.Tests"
  "Shield.Api.Tests"
)

CLASSLIB_CSPROJ='<Project Sdk="Microsoft.NET.Sdk">
</Project>
'

WEBAPI_CSPROJ='<Project Sdk="Microsoft.NET.Sdk.Web">
</Project>
'

CONSOLE_CSPROJ='<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
</Project>
'

TEST_CSPROJ='<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="coverlet.collector" />
    <PackageReference Include="FluentAssertions" />
    <PackageReference Include="NSubstitute" />
  </ItemGroup>
</Project>
'

# Create classlibs
for proj in "${CLASSLIBS[@]}"; do
  mkdir -p "src/${proj}"
  printf "%s" "$CLASSLIB_CSPROJ" > "src/${proj}/${proj}.csproj"
done

# Web SPA project folder (Vue lives here)
mkdir -p src/Shield.Web

# Api (webapi)
mkdir -p src/Shield.Api
printf "%s" "$WEBAPI_CSPROJ" > src/Shield.Api/Shield.Api.csproj

# Agent (console)
mkdir -p agent/shield-agent
printf "%s" "$CONSOLE_CSPROJ" > agent/shield-agent/shield-agent.csproj

# Test projects
for proj in "${TEST_PROJECTS[@]}"; do
  mkdir -p "tests/${proj}"
  printf "%s" "$TEST_CSPROJ" > "tests/${proj}/${proj}.csproj"
done

# Add all to solution
echo "Adding projects to sln..."
for proj in "${CLASSLIBS[@]}"; do
  dotnet sln Shield.sln add "src/${proj}/${proj}.csproj" >/dev/null
done
dotnet sln Shield.sln add src/Shield.Api/Shield.Api.csproj >/dev/null
dotnet sln Shield.sln add agent/shield-agent/shield-agent.csproj >/dev/null
for proj in "${TEST_PROJECTS[@]}"; do
  dotnet sln Shield.sln add "tests/${proj}/${proj}.csproj" >/dev/null
done

echo "Done."
ls src/
ls tests/
