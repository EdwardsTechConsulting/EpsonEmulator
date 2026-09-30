param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
dotnet build .\src\EpsonEmulator.App\EpsonEmulator.App.csproj --configuration $Configuration
dotnet build .\tests\EpsonEmulator.Tests\EpsonEmulator.Tests.csproj --configuration $Configuration
dotnet run --project .\tests\EpsonEmulator.Tests\EpsonEmulator.Tests.csproj --configuration $Configuration
