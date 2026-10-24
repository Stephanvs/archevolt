#Requires -Version 7.2
[CmdletBinding()]
param(
    [string[]] $Skip = @(),
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$')]
    [string] $PackageVersion = '0.1.0',
    [ValidatePattern('^(?:[0-9a-fA-F]{40})?$')]
    [string] $SourceRevisionId = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.ZipFile

$Root = (Resolve-Path -LiteralPath (Split-Path -Parent $PSScriptRoot)).ProviderPath
$Artifacts = Join-Path $Root 'artifacts'
$Logs = Join-Path $Artifacts 'logs'
$Packages = Join-Path $Artifacts 'packages'
$Consumer = Join-Path $Root 'tests/PackageConsumer'
$ConsumerLibrary = Join-Path $Root 'tests/PackageLibrary'
$ConsumerPackages = Join-Path $Artifacts 'consumer-packages'
$Rid = [System.Runtime.InteropServices.RuntimeInformation]::RuntimeIdentifier
$ExeSuffix = $IsWindows ? '.exe' : ''
$BuildArguments = @("-p:Version=$PackageVersion", "-p:ArchevoltPackageVersion=$PackageVersion")
if ($SourceRevisionId) {
    $BuildArguments += "-p:SourceRevisionId=$SourceRevisionId"
}

function Remove-Tree([string[]] $Paths) {
    $comparison = $IsWindows ? [StringComparison]::OrdinalIgnoreCase : [StringComparison]::Ordinal
    $rootPrefix = [System.IO.Path]::TrimEndingDirectorySeparator($Root) + [System.IO.Path]::DirectorySeparatorChar
    foreach ($path in $Paths) {
        $fullPath = [System.IO.Path]::GetFullPath($path)
        if (-not $fullPath.StartsWith($rootPrefix, $comparison)) {
            throw "refusing to remove a path outside the repository: $fullPath"
        }
        if (Test-Path -LiteralPath $path) {
            $resolvedPath = (Resolve-Path -LiteralPath $path).ProviderPath
            if (-not $resolvedPath.StartsWith($rootPrefix, $comparison)) {
                throw "refusing to remove a path outside the repository: $resolvedPath"
            }
            $target = Get-Item -LiteralPath $resolvedPath -Force
            if (-not $target.PSIsContainer) {
                throw "expected a directory to remove: $resolvedPath"
            }
            for ($ancestor = $target; $null -ne $ancestor; $ancestor = $ancestor.Parent) {
                if ($ancestor.Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
                    throw "refusing to remove a directory through a link: $($ancestor.FullName)"
                }
                if ($ancestor.FullName.Equals($Root, $comparison)) {
                    break
                }
            }
            $links = @(Get-ChildItem -LiteralPath $resolvedPath -Recurse -Force -Attributes ReparsePoint)
            if ($links) {
                throw "refusing to remove a directory containing a link: $($links[0].FullName)"
            }
            Remove-Item -LiteralPath $resolvedPath -Recurse -Force
        }
    }
}

function Invoke-Logged([string] $Log, [string] $Command, [string[]] $Arguments) {
    "> $Command $($Arguments -join ' ')" | Add-Content -LiteralPath $Log
    $output = @(& $Command @Arguments 2>&1 | ForEach-Object { "$_" })
    $output | Add-Content -LiteralPath $Log
    if ($LASTEXITCODE -ne 0) {
        throw "$(Split-Path -Leaf $Command) $($Arguments | Select-Object -First 1) exited with $LASTEXITCODE"
    }
    return $output
}

function Assert-Ecs([string[]] $Output) {
    if ($Output -notcontains 'ecs=ok') {
        throw "stdout lacked 'ecs=ok'"
    }
}

function Assert-NoTrimWarnings([string[]] $Output) {
    $warnings = @($Output | Select-String -Pattern '\bIL[23]\d{3}\b')
    if ($warnings) {
        throw "trim/AOT warning: $($warnings[0].Line.Trim())"
    }
}

# A cached copy of the requested version could satisfy restore with a stale package.
function Reset-Consumer {
    Remove-Tree @($ConsumerPackages, (Join-Path $Consumer 'bin'), (Join-Path $Consumer 'obj'),
        (Join-Path $ConsumerLibrary 'bin'), (Join-Path $ConsumerLibrary 'obj'))
}

function Invoke-AotPublishAndRun([string] $Log, [string] $Project, [string] $Name, [string[]] $ExtraArguments = @()) {
    $out = Join-Path $Artifacts "publish/$Name-aot"
    Remove-Tree @($out)
    $publish = Invoke-Logged $Log 'dotnet' (@('publish', $Project, '-c', 'Release', '-r', $Rid, '-o', $out) + $BuildArguments + $ExtraArguments)
    Assert-NoTrimWarnings $publish
    if (Test-Path -LiteralPath (Join-Path $out "$Name.dll")) {
        throw "publish output contains $Name.dll, so it is not a native AOT image"
    }
    Assert-Ecs (Invoke-Logged $Log (Join-Path $out "$Name$ExeSuffix") @())
}

$Steps = [ordered]@{
    'restore-build' = {
        param([string] $Log)
        Remove-Tree @((Join-Path $Artifacts 'bin'), (Join-Path $Artifacts 'obj'))
        $null = Invoke-Logged $Log 'dotnet' (@('build', 'Archevolt.slnx', '-c', 'Release', '-warnaserror') + $BuildArguments)
    }
    'test' = {
        param([string] $Log)
        $null = Invoke-Logged $Log 'dotnet' (@('test', '--solution', 'Archevolt.slnx', '-c', 'Release') + $BuildArguments)
    }
    'archeoids-headless' = {
        param([string] $Log)
        $output = Invoke-Logged $Log 'dotnet' (@('run', '--project', 'samples/Archeoids', '-c', 'Release') + $BuildArguments + @('--', '--verify'))
        if ($output -notcontains 'archeoids=ok checks=5') {
            throw "stdout lacked 'archeoids=ok checks=5'"
        }
    }
    'pack' = {
        param([string] $Log)
        Remove-Tree @($Packages)
        $null = Invoke-Logged $Log 'dotnet' (@('pack', 'src/Archevolt/Archevolt.csproj', '-c', 'Release', '-o', $Packages) + $BuildArguments)
    }
    'package-contents' = {
        param([string] $Log)
        $nupkgs = @(Get-ChildItem -LiteralPath $Packages -Filter 'Archevolt.*.nupkg')
        if ($nupkgs.Count -ne 1) {
            throw "expected one Archevolt package in $Packages, found $($nupkgs.Count)"
        }
        $zip = [System.IO.Compression.ZipFile]::OpenRead($nupkgs[0].FullName)
        try {
            $entries = @($zip.Entries | ForEach-Object FullName)
            $entries | Add-Content -LiteralPath $Log
            foreach ($required in 'lib/net11.0/Archevolt.dll', 'analyzers/dotnet/cs/Archevolt.Generators.dll') {
                if ($entries -notcontains $required) {
                    throw "package lacks $required"
                }
            }
            $roslyn = @($entries -match 'Microsoft\.CodeAnalysis')
            if ($roslyn) {
                throw "package ships Roslyn: $($roslyn -join ', ')"
            }
            $reader = [System.IO.StreamReader]::new(($zip.Entries | Where-Object FullName -Like '*.nuspec').Open())
            try {
                [xml] $nuspec = $reader.ReadToEnd()
            }
            finally {
                $reader.Dispose()
            }
            $nuspec.OuterXml | Add-Content -LiteralPath $Log
            $metadata = $nuspec.SelectSingleNode('/*[local-name()="package"]/*[local-name()="metadata"]')
            if ($metadata.id -ne 'Archevolt') {
                throw "expected package ID Archevolt, found $($metadata.id)"
            }
            if ($metadata.version -ne $PackageVersion) {
                throw "expected package version $PackageVersion, found $($metadata.version)"
            }
            if ($SourceRevisionId -and $metadata.repository.commit -ne $SourceRevisionId) {
                throw "expected repository commit $SourceRevisionId, found $($metadata.repository.commit)"
            }
            $dependencies = @($nuspec.GetElementsByTagName('dependency') | ForEach-Object { $_.GetAttribute('id') })
            if ($dependencies) {
                throw "package declares dependencies: $($dependencies -join ', ')"
            }
        }
        finally {
            $zip.Dispose()
        }
    }
    'consumer-jit' = {
        param([string] $Log)
        Reset-Consumer
        $null = Invoke-Logged $Log 'dotnet' (@('restore', $Consumer) + $BuildArguments)
        Assert-Ecs (Invoke-Logged $Log 'dotnet' (@('run', '--project', $Consumer, '-c', 'Release', '--no-restore') + $BuildArguments))
    }
    'consumer-aot' = {
        param([string] $Log)
        Reset-Consumer
        Invoke-AotPublishAndRun $Log $Consumer 'PackageConsumer' @('-p:PublishAot=true')
    }
    'sample-aot' = {
        param([string] $Log)
        Invoke-AotPublishAndRun $Log 'samples/Archevolt.Particles' 'Archevolt.Particles'
    }
    'performance-smoke' = {
        param([string] $Log)
        $output = Invoke-Logged $Log 'dotnet' (@('run', '--project', 'benchmarks/Archevolt.Benchmarks', '-c', 'Release') + $BuildArguments + @('--', '--quick'))
        $measurements = @($output | Where-Object { $_ -match '^generated-(each|query),' })
        if ($measurements.Count -ne 4) {
            throw 'expected serial and parallel allocation measurements for generated Each and query traversal'
        }
        foreach ($measurement in $measurements) {
            $columns = $measurement -split ','
            if ([long]$columns[2] -ne 0 -or [long]$columns[3] -ne 0) {
                throw "steady-state traversal allocated managed memory: $measurement"
            }
        }
    }
}

$skipped = @($Skip | ForEach-Object { $_ -split ',' } | ForEach-Object Trim | Where-Object { $_ })
$unknown = @($skipped | Where-Object { -not $Steps.Contains($_) })
if ($unknown) {
    throw "unknown step(s) $($unknown -join ', '); known steps are $($Steps.Keys -join ', ')"
}

$null = New-Item -ItemType Directory -Force -Path $Logs
$failures = 0
$originalPath = $env:Path

# VsDevCmd.bat runs vswhere.exe by bare name from its own folder. With NoDefaultCurrentDirectoryInExePath set,
# that lookup fails and NativeAOT captures the error text as the linker path.
if ($IsWindows -and $env:NoDefaultCurrentDirectoryInExePath) {
    $vsInstaller = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
    if (Test-Path -LiteralPath $vsInstaller) {
        $env:Path = "$vsInstaller;$env:Path"
    }
}

# global.json (SDK pin and test runner) is resolved from the working directory, not from the project path.
Push-Location $Root
try {
    foreach ($name in $Steps.Keys) {
        if ($skipped -contains $name) {
            Write-Host "SKIP $name"
            continue
        }
        $log = Join-Path $Logs "$name.txt"
        $null = New-Item -ItemType File -Force -Path $log
        try {
            $null = & $Steps[$name] $log
            Write-Host "PASS $name"
        }
        catch {
            $failures++
            "FAIL $($_.Exception.Message)" | Add-Content -LiteralPath $log
            Write-Host "FAIL ${name}: $($_.Exception.Message) (see $log)"
        }
    }
}
finally {
    Pop-Location
    $env:Path = $originalPath
}

if ($failures -eq 0 -and $skipped -notcontains 'package-contents') {
    Write-Output (Join-Path $Packages "Archevolt.$PackageVersion.nupkg")
}
exit ($failures -eq 0 ? 0 : 1)
