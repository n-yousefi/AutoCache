param(
    [ValidateRange(1, 100)][int]$TestRepeats = 3,
    [ValidateRange(1, 10000000)][int]$Requests = 250000,
    [ValidateRange(1, 16384)][int]$Concurrency = 1024,
    [ValidateRange(1, 100000)][int]$Keys = 1000,
    [ValidateRange(1, 10000)][int]$Rounds = 20,
    [ValidateRange(1, 1000000)][int]$StorageRequests = 20000
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
function Invoke-DotNet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE" }
}

Push-Location $projectRoot
try {
    Invoke-DotNet -Arguments @('restore', 'AutoCache.sln')
    Invoke-DotNet -Arguments @('clean', 'AutoCache.sln', '-c', 'Release', '--verbosity', 'quiet')
    Invoke-DotNet -Arguments @('build', 'AutoCache.sln', '-c', 'Release', '--no-restore')
    for ($iteration = 1; $iteration -le $TestRepeats; $iteration++) {
        Invoke-DotNet -Arguments @('test', 'tests/AutoCache.Tests/AutoCache.Tests.csproj', '-c', 'Release', '--no-build',
            '--logger', "trx;LogFilePrefix=release-$iteration", '--results-directory', 'artifacts/release-tests',
            '--blame-hang-timeout', '60s')
    }
    foreach ($framework in @('net8.0', 'net10.0')) {
        Invoke-DotNet -Arguments @('run', '--project', 'tests/AutoCache.LoadTests/AutoCache.LoadTests.csproj', '-c', 'Release',
            '-f', $framework, '--no-build', '--', '--requests', "$Requests", '--concurrency', "$Concurrency", '--keys', "$Keys", '--rounds', "$Rounds")
        Invoke-DotNet -Arguments @('run', '--project', 'tests/AutoCache.LoadTests/AutoCache.LoadTests.csproj', '-c', 'Release',
            '-f', $framework, '--no-build', '--', '--requests', "$StorageRequests", '--concurrency', '128', '--keys', '1000',
            '--storage-delay-ms', '1', '--serialize', '--rounds', "$Rounds")
    }
    Invoke-DotNet -Arguments @('pack', 'src/AutoCache/AutoCache.csproj', '-c', 'Release', '--no-build', '-o', 'artifacts/packages')

    # Restore an independent application from the actual nupkg, not a project reference.
    [xml]$packageProject = Get-Content 'src/AutoCache/AutoCache.csproj'
    $packageVersion = [string]$packageProject.Project.PropertyGroup.Version
    $consumerDirectory = Join-Path $projectRoot 'artifacts/package-consumer'
    New-Item -ItemType Directory -Force -Path $consumerDirectory | Out-Null
    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFrameworks>net8.0;net10.0</TargetFrameworks></PropertyGroup>
  <ItemGroup><PackageReference Include="AutoCache" Version="$packageVersion" /></ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $consumerDirectory 'PackageConsumer.csproj')
    Copy-Item -LiteralPath 'scripts/PackageSmoke.cs' -Destination (Join-Path $consumerDirectory 'Program.cs')
    $packageFeed = Join-Path $projectRoot 'artifacts/packages'
    # An isolated NuGet cache prevents a previously installed package with this version
    # from masking the artifact that was just built.
    $consumerPackages = Join-Path $consumerDirectory ("packages-" + [Guid]::NewGuid().ToString('N'))
    Invoke-DotNet -Arguments @('restore', "$consumerDirectory/PackageConsumer.csproj", '--source', $packageFeed, '--packages', $consumerPackages)
    foreach ($framework in @('net8.0', 'net10.0')) {
        Invoke-DotNet -Arguments @('run', '--project', "$consumerDirectory/PackageConsumer.csproj", '-c', 'Release', '-f', $framework, '--no-restore')
    }
    foreach ($package in Get-ChildItem 'artifacts/packages' -File | Where-Object { $_.Name -like "AutoCache.$packageVersion.*pkg" }) {
        $hash = (Get-FileHash $package.FullName -Algorithm SHA256).Hash
        Set-Content -LiteralPath "$($package.FullName).sha256" -Value "$hash  $($package.Name)"
    }
    Write-Output "PASS: release verification completed. Packages: artifacts/packages/AutoCache.$packageVersion.nupkg and .snupkg. Nothing was published."
}
finally { Pop-Location }
