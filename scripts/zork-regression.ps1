param(
    [string]$StoryPath
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($StoryPath)) {
    $StoryPath = Join-Path $repoRoot 'stories/zork1.z3'
}

if (-not (Test-Path -LiteralPath $StoryPath -PathType Leaf)) {
    Write-Output "SKIP: Zork I story file not found: $StoryPath"
    exit 0
}

$tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "gruegpt-zork-regression.$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $tempDirectory | Out-Null
$transcriptPath = Join-Path $tempDirectory 'input.txt'
$outputPath = Join-Path $tempDirectory 'output.txt'
$errorPath = Join-Path $tempDirectory 'error.txt'
[System.IO.File]::WriteAllText($transcriptPath, "look`ninventory`nnorth`nsouth`nquit`nyes`n", [System.Text.Encoding]::ASCII)

try {
    $cliProject = Join-Path $repoRoot 'src/GrueGPT.Cli/GrueGPT.Cli.csproj'
    $dotnetArguments = @('run', '--project', $cliProject, '--configuration', 'Release', '--', $StoryPath)
    Get-Content -LiteralPath $transcriptPath | & dotnet @dotnetArguments 1> $outputPath 2> $errorPath
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "Zork I CLI session returned exit code $exitCode."
    }

    $transcript = [System.IO.File]::ReadAllText($outputPath)
    foreach ($command in @('look', 'inventory', 'north', 'south')) {
        if (-not $transcript.Contains("> $command")) {
            throw "CLI transcript did not echo the expected command prompt for '$command'."
        }
    }

    if ([System.IO.File]::ReadAllText($errorPath).Contains('Story execution stopped at')) {
        throw 'Zork I execution reported an interpreter error.'
    }

    Write-Output 'PASS: Zork I completed the local command regression transcript.'
}
catch {
    Write-Error "FAIL: $($_.Exception.Message)"
    exit 1
}
finally {
    Remove-Item -LiteralPath $tempDirectory -Recurse -Force
}
