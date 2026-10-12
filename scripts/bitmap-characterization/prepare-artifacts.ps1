param(
    [Parameter(Mandatory = $true)]
    [string] $ArtifactsPath
)

$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force -Path $ArtifactsPath | Out-Null

$programFilesX86 = [Environment]::GetFolderPath("ProgramFilesX86")
$targetingPackRoot = Join-Path $programFilesX86 "Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8"
$frameworkList = Join-Path $targetingPackRoot "RedistList\FrameworkList.xml"
$targetingPackAvailable = Test-Path -LiteralPath $frameworkList

$vswhere = Join-Path $programFilesX86 "Microsoft Visual Studio\Installer\vswhere.exe"
$visualStudioPath = $null
if (Test-Path -LiteralPath $vswhere) {
    $visualStudioPath = (& $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -property installationPath | Select-Object -First 1)
}
$visualStudioMsBuildPath = $null
$visualStudioMsBuildAvailable = $false
if ($visualStudioPath) {
    $visualStudioMsBuildPath = Join-Path $visualStudioPath "MSBuild\Current\Bin\MSBuild.exe"
    $visualStudioMsBuildAvailable = Test-Path -LiteralPath $visualStudioMsBuildPath
}

$frameworkRelease = $null
$frameworkKey = "HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"
if (Test-Path $frameworkKey) {
    $frameworkRelease = (Get-ItemProperty -Path $frameworkKey -Name Release -ErrorAction SilentlyContinue).Release
}

$msbuildCommand = Get-Command msbuild -ErrorAction SilentlyContinue
$msbuildPath = $null
$msbuildVersion = $null
if ($msbuildCommand) {
    $msbuildPath = $msbuildCommand.Source
    $msbuildVersion = ((& $msbuildCommand.Source -version -nologo 2>&1) | Out-String).Trim()
}
$referenceAssemblies = @("mscorlib.dll", "System.dll", "System.Drawing.dll") | ForEach-Object {
    [ordered]@{ name = $_; available = Test-Path -LiteralPath (Join-Path $targetingPackRoot $_) }
}

$sourcePaths = @(
    "Kinovea.Video.Bitmap/VideoReaderBitmap.cs",
    "Kinovea.Video.Bitmap/FrameGeneratorImageFile.cs",
    "Kinovea.Video/VideoReaders/VideoReader.cs",
    "Kinovea.Video/VideoReaders/IFrameGenerator.cs",
    "Kinovea.Video/Caching/VideoFrame.cs",
    "Kinovea.Video/Caching/SingleFrame.cs",
    "Kinovea.Video/Caching/Cache.cs",
    "Kinovea.Video/Caching/PreBuffer2.cs",
    "Kinovea.Video/Delegates.cs",
    "Kinovea.Video/Caching/PlayerState.cs",
    "Kinovea.Video/Caching/PlayerAction.cs",
    "Kinovea.Video/VideoInfo.cs",
    "Kinovea.Video/VideoSummary.cs",
    "Kinovea.Video/VideoGeometry/VideoGeometry.cs",
    "Kinovea.Video/VideoGeometry/VideoGeometryRequest.cs",
    "Kinovea.Services/Types/ImageRotation.cs",
    "Kinovea.Services/Types/VideoSection.cs",
    "Kinovea.Video.Bitmap/Kinovea.Video.Bitmap.csproj",
    "Kinovea.Video/Kinovea.Video.csproj",
    "Kinovea.Services/Kinovea.Services.csproj",
    "Kinovea.Services/packages.config",
    "tests/Bitmap.Characterization/Program.cs",
    "tests/Bitmap.Characterization/FixtureFactory.cs",
    "tests/Bitmap.Characterization/EvidenceWriter.cs",
    "tests/Bitmap.Characterization/Kinovea.Bitmap.Characterization.csproj",
    "src/Kinovea.PortablePixels/PixelBuffer.cs",
    "src/Kinovea.PortablePixels.GdiPlus/GdiPlusPixelBufferAdapter.cs",
    "src/Kinovea.PortablePixels.GdiPlus/Kinovea.PortablePixels.GdiPlus.csproj",
    "tests/WindowsBitmapAdapter.Tests/Program.cs",
    "tests/WindowsBitmapAdapter.Tests/WindowsBitmapAdapter.Tests.csproj",
    "src/Kinovea.PortableImageReader/PortableImageReader.cs",
    "src/Kinovea.PortableImageReader/Kinovea.PortableImageReader.csproj",
    "tests/PortableImageReader.Tests/Program.cs",
    "tests/PortableImageReader.Tests/PortableImageReader.Tests.csproj",
    "src/Kinovea.PortableFrameGenerator/PortableImageFrame.cs",
    "src/Kinovea.PortableFrameGenerator/PortableImageFrameGenerator.cs",
    "src/Kinovea.PortableFrameGenerator/Kinovea.PortableFrameGenerator.csproj",
    "src/Kinovea.PortableReaderShell/PortableReaderShell.cs",
    "src/Kinovea.PortableReaderShell/Kinovea.PortableReaderShell.csproj",
    "tests/PortableFrameGenerator.Tests/Program.cs",
    "tests/PortableFrameGenerator.Tests/PortableFrameGenerator.Tests.csproj",
    "tests/PortableReaderShell.Tests/Program.cs",
    "tests/PortableReaderShell.Tests/PortableReaderShell.Tests.csproj",
    "scripts/bitmap-characterization/prepare-artifacts.ps1",
    ".github/workflows/bitmap-characterization.yml",
    ".github/workflows/windows-bitmap-adapter.yml",
    ".github/workflows/portable-image-reader.yml",
    ".github/workflows/portable-frame-integration.yml"
)
$sourceHashes = foreach ($relativePath in $sourcePaths) {
    $fullPath = Join-Path (Get-Location) $relativePath
    if (Test-Path -LiteralPath $fullPath) {
        $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $fullPath
        [ordered]@{ path = $relativePath; sha256 = $hash.Hash.ToLowerInvariant() }
    } else {
        [ordered]@{ path = $relativePath; sha256 = $null; status = "MISSING" }
    }
}

$sourceHashes | ConvertTo-Json -Depth 5 | Set-Content -Encoding UTF8 (Join-Path $ArtifactsPath "source-hashes.json")

$environment = [ordered]@{
    capturedUtc = [DateTime]::UtcNow.ToString("o")
    gitSha = $env:GITHUB_SHA
    runnerOs = $env:RUNNER_OS
    runnerImage = $env:ImageVersion
    machineName = $env:COMPUTERNAME
    osVersion = [Environment]::OSVersion.VersionString
    processArchitecture = [IntPtr]::Size * 8
    powershellVersion = $PSVersionTable.PSVersion.ToString()
    dotnetFrameworkRelease = $frameworkRelease
    visualStudioPath = $visualStudioPath
    visualStudioMsBuildPath = $visualStudioMsBuildPath
    visualStudioMsBuildAvailable = $visualStudioMsBuildAvailable
    msbuildPath = $msbuildPath
    msbuildVersion = $msbuildVersion
    netFramework48TargetingPackRoot = $targetingPackRoot
    netFramework48TargetingPackAvailable = $targetingPackAvailable
    netFramework48ReferenceAssemblies = $referenceAssemblies
    frameworkListSha256 = if ($targetingPackAvailable) { (Get-FileHash -Algorithm SHA256 -LiteralPath $frameworkList).Hash.ToLowerInvariant() } else { $null }
}
$environment | ConvertTo-Json -Depth 5 | Set-Content -Encoding UTF8 (Join-Path $ArtifactsPath "environment.json")

if (-not $targetingPackAvailable) {
    throw "The .NET Framework 4.8 targeting pack is unavailable. See artifacts/bitmap-characterization/environment.json."
}
if (-not $msbuildCommand) {
    throw "Visual Studio MSBuild is unavailable. See artifacts/bitmap-characterization/environment.json."
}
if (-not $visualStudioMsBuildAvailable) {
    throw "Visual Studio MSBuild.exe was not found under the vswhere installation. See artifacts/bitmap-characterization/environment.json."
}
