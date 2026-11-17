#Requires -Version 5.1
<#
.SYNOPSIS
    Runs AirGlance's startup benchmark and reports raw values and summary statistics.

.DESCRIPTION
    Launches AirGlance.exe with:
        --bench=startup "<image>"

    Each run's stdout is redirected to a temporary file because AirGlance is a
    WinExe and does not have an attached console. The script parses
    first_window_ms, first_frame_ms, decode_ms, image_mp, and peak_ws_mb, and also
    records wall-clock elapsed time. The default executable is the NativeAOT
    publish/win-x64 build when present; otherwise the normal Release apphost is
    used.

    The comparison targets apply only to a NativeAOT publish of the 24 MP test
    image with 10 runs. Run packaging/scripts/pack.ps1 first for representative
    numbers.

.PARAMETER Runs
    Number of samples. Defaults to 10.

.PARAMETER Exe
    AirGlance.exe path. Defaults to publish/win-x64/AirGlance.exe, then
    src/AirGlance.App/bin/Release/net10.0/AirGlance.exe.

.PARAMETER Image
    Image passed to --bench=startup. Defaults to tools/testdata/photo-24mp.jpg.

.PARAMETER OutputJson
    Optional path for a machine-readable result. The parent directory is created
    if necessary.

.EXAMPLE
    ./tools/bench.ps1

.EXAMPLE
    ./tools/bench.ps1 -Runs 20 -OutputJson artifacts/bench-startup.json
#>
[CmdletBinding()]
param(
    [ValidateRange(1, 500)]
    [int]$Runs = 10,

    [string]$Exe,

    [string]$Image,

    [string]$OutputJson
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$runTimeoutMilliseconds = 120000

function Resolve-InputPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "A path cannot be empty."
    }

    if (-not [IO.Path]::IsPathRooted($Path)) {
        $Path = Join-Path (Get-Location).Path $Path
    }

    return [IO.Path]::GetFullPath($Path)
}

function Get-DefaultExePath {
    $candidates = @(
        (Join-Path $repoRoot "publish\win-x64\AirGlance.exe"),
        (Join-Path $repoRoot "src\AirGlance.App\bin\Release\net10.0\AirGlance.exe")
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return [IO.Path]::GetFullPath($candidate)
        }
    }

    throw "No AirGlance.exe was found. Publish a build with packaging/scripts/pack.ps1 or pass -Exe explicitly."
}

function Get-DotnetExecutable {
    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -ne $command -and -not [string]::IsNullOrWhiteSpace($command.Source)) {
        return $command.Source
    }

    if (-not [string]::IsNullOrWhiteSpace($env:DOTNET_ROOT)) {
        $candidate = Join-Path $env:DOTNET_ROOT "dotnet.exe"
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return $candidate
        }
    }

    return $null
}

function Get-DotnetVersion {
    $dotnetPath = Get-DotnetExecutable
    if ($null -eq $dotnetPath) {
        return $null
    }

    try {
        $version = & $dotnetPath --version 2>$null
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace(($version | Select-Object -First 1))) {
            return ([string]($version | Select-Object -First 1)).Trim()
        }
    }
    catch {
        return $null
    }

    return $null
}

function Get-MachineInfo {
    $caption = [Environment]::OSVersion.VersionString
    $version = [Environment]::OSVersion.Version.ToString()
    $cpuName = [string]$env:PROCESSOR_IDENTIFIER
    $ramGb = $null

    try {
        $os = Get-CimInstance -ClassName Win32_OperatingSystem -ErrorAction Stop
        if ($null -ne $os) {
            if (-not [string]::IsNullOrWhiteSpace([string]$os.Caption)) {
                $caption = ([string]$os.Caption).Trim()
            }
            if (-not [string]::IsNullOrWhiteSpace([string]$os.Version)) {
                $version = ([string]$os.Version).Trim()
            }
            if ($null -ne $os.TotalVisibleMemorySize) {
                $ramGb = [math]::Round(([double]$os.TotalVisibleMemorySize) / 1MB, 1)
            }
        }
    }
    catch {
        # CIM is not available on every host; fall back to the environment values.
    }

    try {
        $cpu = Get-CimInstance -ClassName Win32_Processor -ErrorAction Stop | Select-Object -First 1
        if ($null -ne $cpu -and -not [string]::IsNullOrWhiteSpace([string]$cpu.Name)) {
            $cpuName = (([string]$cpu.Name) -replace "\s+", " ").Trim()
        }
    }
    catch {
        # Keep PROCESSOR_IDENTIFIER as the fallback name.
    }

    return [ordered]@{
        os_caption = $caption
        os_version = $version
        cpu_name = $cpuName
        ram_gb = $ramGb
        dotnet_version = Get-DotnetVersion
        powershell_version = $PSVersionTable.PSVersion.ToString()
    }
}

function Format-MetricValue {
    param(
        $Value,
        [string]$Format = "0.0"
    )

    if ($null -eq $Value) {
        return "MISSING"
    }

    return ([double]$Value).ToString($Format, [Globalization.CultureInfo]::InvariantCulture)
}

function Get-Median {
    param(
        [AllowEmptyCollection()]
        [double[]]$Values
    )

    if ($null -eq $Values -or $Values.Count -eq 0) {
        return $null
    }

    $sorted = @($Values | Sort-Object)
    $count = $sorted.Count
    if ($count % 2 -eq 1) {
        return [double]$sorted[[int](($count - 1) / 2)]
    }

    $lower = [double]$sorted[($count / 2) - 1]
    $upper = [double]$sorted[$count / 2]
    return ($lower + $upper) / 2.0
}

function Get-Percentile95 {
    param(
        [AllowEmptyCollection()]
        [double[]]$Values
    )

    if ($null -eq $Values -or $Values.Count -eq 0) {
        return $null
    }

    $sorted = @($Values | Sort-Object)
    $index = [int][math]::Ceiling(0.95 * $sorted.Count) - 1
    if ($index -lt 0) {
        $index = 0
    }
    if ($index -ge $sorted.Count) {
        $index = $sorted.Count - 1
    }

    return [double]$sorted[$index]
}

function Get-MetricStats {
    param(
        [object[]]$Records,
        [string]$PropertyName
    )

    $values = @()
    foreach ($record in $Records) {
        $value = $record.$PropertyName
        if ($null -ne $value) {
            $values += [double]$value
        }
    }

    $median = Get-Median -Values $values
    $p95 = Get-Percentile95 -Values $values

    if ($null -ne $median) {
        $median = [math]::Round($median, 2)
    }
    if ($null -ne $p95) {
        $p95 = [math]::Round($p95, 2)
    }

    return [pscustomobject]@{
        median  = $median
        p95     = $p95
        samples = $values.Count
    }
}

if ([string]::IsNullOrWhiteSpace($Exe)) {
    $ExePath = Get-DefaultExePath
}
else {
    $ExePath = Resolve-InputPath -Path $Exe
}

if (-not (Test-Path -LiteralPath $ExePath -PathType Leaf)) {
    throw "AirGlance executable not found: '$ExePath'."
}

if ([string]::IsNullOrWhiteSpace($Image)) {
    $Image = Join-Path $PSScriptRoot "testdata\photo-24mp.jpg"
}

$ImagePath = Resolve-InputPath -Path $Image
if (-not (Test-Path -LiteralPath $ImagePath -PathType Leaf)) {
    throw "Benchmark image not found: '$ImagePath'. Generate the test data with tools/make-test-images.py, or pass -Image."
}

$imageInfo = Get-Item -LiteralPath $ImagePath
$machine = Get-MachineInfo
$date = (Get-Date).ToString("o", [Globalization.CultureInfo]::InvariantCulture)
$is24MpImage = [IO.Path]::GetFileName($ImagePath) -ieq "photo-24mp.jpg"

Write-Output "AirGlance startup benchmark"
Write-Output ("  Machine:  {0} (version {1})" -f $machine["os_caption"], $machine["os_version"])
Write-Output ("  CPU:      {0}" -f $machine["cpu_name"])
if ($null -eq $machine["ram_gb"]) {
    Write-Output "  RAM:      unknown"
}
else {
    Write-Output ("  RAM:      {0} GB" -f (Format-MetricValue -Value $machine["ram_gb"] -Format "0.0"))
}

if ($null -eq $machine["dotnet_version"]) {
    Write-Output "  .NET:     not found on PATH"
}
else {
    Write-Output ("  .NET SDK: {0}" -f $machine["dotnet_version"])
}

Write-Output ("  Exe:      {0}" -f $ExePath)
Write-Output ("  Image:    {0} ({1} MB, {2} bytes)" -f $ImagePath, (Format-MetricValue -Value ($imageInfo.Length / 1MB) -Format "0.00"), $imageInfo.Length)
Write-Output ("  Date:     {0}" -f $date)
Write-Output ("  Runs:     {0}" -f $Runs)
Write-Output ""

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("airglance-bench-" + [guid]::NewGuid().ToString("N"))
$runRecords = @()
$missingFirstFrame = $false

try {
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null

    for ($runNumber = 1; $runNumber -le $Runs; $runNumber++) {
        $stdoutPath = Join-Path $tempRoot ("run-{0}.stdout.txt" -f $runNumber)
        $stderrPath = Join-Path $tempRoot ("run-{0}.stderr.txt" -f $runNumber)
        $errorMessage = $null
        $process = $null
        $stdout = ""
        $stderr = ""
        $metrics = @{}

        Write-Host ("Run {0}/{1}..." -f $runNumber, $Runs)

        $stopwatch = [Diagnostics.Stopwatch]::StartNew()
        try {
            $argumentLine = '--bench=startup "{0}"' -f $ImagePath
            $process = Start-Process `
                -FilePath $ExePath `
                -ArgumentList $argumentLine `
                -WorkingDirectory (Split-Path -Parent $ExePath) `
                -RedirectStandardOutput $stdoutPath `
                -RedirectStandardError $stderrPath `
                -PassThru `
                -ErrorAction Stop

            $finished = $process.WaitForExit($runTimeoutMilliseconds)
            if (-not $finished) {
                $errorMessage = "process did not exit within $runTimeoutMilliseconds ms"
                try {
                    $process.Kill()
                    [void]$process.WaitForExit(5000)
                }
                catch {
                    # The process may already have exited between the timeout and Kill().
                }
            }
        }
        catch {
            $errorMessage = $_.Exception.Message
        }
        finally {
            $stopwatch.Stop()
        }

        if (Test-Path -LiteralPath $stdoutPath -PathType Leaf) {
            $stdout = Get-Content -LiteralPath $stdoutPath -Raw -ErrorAction SilentlyContinue
            if ($null -eq $stdout) {
                $stdout = ""
            }
        }

        if (Test-Path -LiteralPath $stderrPath -PathType Leaf) {
            $stderr = Get-Content -LiteralPath $stderrPath -Raw -ErrorAction SilentlyContinue
            if ($null -eq $stderr) {
                $stderr = ""
            }
        }

        $outputErrors = @()
        foreach ($line in ($stdout -split "\r?\n")) {
            $trimmed = $line.Trim()
            if ($trimmed -match '^(?<name>first_window_ms|first_frame_ms|decode_ms|image_mp|peak_ws_mb)=(?<value>-?[0-9]+(?:\.[0-9]+)?)$') {
                $number = 0.0
                if ([double]::TryParse(
                        $matches["value"],
                        [Globalization.NumberStyles]::Float,
                        [Globalization.CultureInfo]::InvariantCulture,
                        [ref]$number)) {
                    $metrics[$matches["name"]] = $number
                }
            }
            elseif ($trimmed -match '^error=(?<message>.*)$') {
                $outputErrors += $matches["message"]
            }
        }

        if ($null -eq $errorMessage -and $outputErrors.Count -gt 0) {
            $errorMessage = ($outputErrors -join "; ")
        }

        if ($null -eq $errorMessage -and -not [string]::IsNullOrWhiteSpace($stderr)) {
            $errorMessage = $stderr.Trim()
        }

        $firstWindow = $null
        if ($metrics.ContainsKey("first_window_ms")) {
            $firstWindow = $metrics["first_window_ms"]
        }

        $firstFrame = $null
        if ($metrics.ContainsKey("first_frame_ms")) {
            $firstFrame = $metrics["first_frame_ms"]
        }
        else {
            $missingFirstFrame = $true
            if ($null -eq $errorMessage -or [string]::IsNullOrWhiteSpace($errorMessage)) {
                $stdoutSummary = $stdout.Trim()
                if ($stdoutSummary.Length -gt 500) {
                    $stdoutSummary = $stdoutSummary.Substring(0, 500)
                }
                $errorMessage = "first_frame_ms was not reported. stdout: '$stdoutSummary'"
            }
        }

        $decode = $null
        if ($metrics.ContainsKey("decode_ms")) {
            $decode = $metrics["decode_ms"]
        }

        $imageMp = $null
        if ($metrics.ContainsKey("image_mp")) {
            $imageMp = $metrics["image_mp"]
        }

        $peakWs = $null
        if ($metrics.ContainsKey("peak_ws_mb")) {
            $peakWs = $metrics["peak_ws_mb"]
        }

        $wallMs = [math]::Round($stopwatch.Elapsed.TotalMilliseconds, 1)
        $runRecords += [pscustomobject]@{
            run             = $runNumber
            first_window_ms = $firstWindow
            first_frame_ms  = $firstFrame
            decode_ms       = $decode
            image_mp        = $imageMp
            peak_ws_mb      = $peakWs
            wall_ms         = $wallMs
            error           = $errorMessage
        }

        if ($null -ne $firstFrame) {
            Write-Host ("  first_frame_ms={0}  wall_ms={1}" -f (Format-MetricValue -Value $firstFrame), $wallMs)
        }
        else {
            Write-Host "  no first_frame_ms value"
        }

        if ($null -ne $process) {
            $process.Dispose()
        }
    }
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

$firstWindowStats = Get-MetricStats -Records $runRecords -PropertyName "first_window_ms"
$firstFrameStats = Get-MetricStats -Records $runRecords -PropertyName "first_frame_ms"
$decodeStats = Get-MetricStats -Records $runRecords -PropertyName "decode_ms"
$peakStats = Get-MetricStats -Records $runRecords -PropertyName "peak_ws_mb"

Write-Output ""
Write-Output "Raw runs:"
$tableRows = foreach ($record in $runRecords) {
    [pscustomobject]@{
        Run             = $record.run
        first_window_ms = Format-MetricValue -Value $record.first_window_ms
        first_frame_ms  = Format-MetricValue -Value $record.first_frame_ms
        decode_ms       = Format-MetricValue -Value $record.decode_ms
        image_mp        = Format-MetricValue -Value $record.image_mp
        peak_ws_mb      = Format-MetricValue -Value $record.peak_ws_mb
        wall_ms         = Format-MetricValue -Value $record.wall_ms
    }
}
Write-Output ($tableRows | Format-Table -AutoSize | Out-String -Width 200)

Write-Output "Summary (valid values only):"
Write-Output ("  first_window_ms : median {0} ms, p95 {1} ms, n={2}" -f (Format-MetricValue -Value $firstWindowStats.median), (Format-MetricValue -Value $firstWindowStats.p95), $firstWindowStats.samples)
Write-Output ("  first_frame_ms  : median {0} ms, p95 {1} ms, n={2}" -f (Format-MetricValue -Value $firstFrameStats.median), (Format-MetricValue -Value $firstFrameStats.p95), $firstFrameStats.samples)
Write-Output ("  decode_ms       : median {0} ms, p95 {1} ms, n={2}" -f (Format-MetricValue -Value $decodeStats.median), (Format-MetricValue -Value $decodeStats.p95), $decodeStats.samples)
Write-Output ("  peak_ws_mb      : median {0} MB, p95 {1} MB, n={2}" -f (Format-MetricValue -Value $peakStats.median), (Format-MetricValue -Value $peakStats.p95), $peakStats.samples)

if ($Runs -eq 10 -and $is24MpImage) {
    Write-Output ""
    Write-Output "24 MP startup target comparisons (median):"
    if ($null -ne $firstFrameStats.median) {
        $firstFrameVerdict = "MISS"
        if ($firstFrameStats.median -le 500) {
            $firstFrameVerdict = "PASS"
        }
        Write-Output ("  first frame: {0} ms (target <= 500 ms) -> {1}" -f (Format-MetricValue -Value $firstFrameStats.median), $firstFrameVerdict)
    }
    else {
        Write-Output "  first frame: no valid samples (target <= 500 ms)"
    }

    if ($null -ne $peakStats.median) {
        $memoryVerdict = "MISS"
        if ($peakStats.median -le 250) {
            $memoryVerdict = "PASS"
        }
        Write-Output ("  memory:      {0} MB peak working set (target <= 250 MB) -> {1}" -f (Format-MetricValue -Value $peakStats.median), $memoryVerdict)
    }
    else {
        Write-Output "  memory:      no valid samples (target <= 250 MB)"
    }
}

Write-Output ""
Write-Output "Reminder: these numbers are only comparable to the README targets when Exe is a NativeAOT publish."
Write-Output "Build one with packaging/scripts/pack.ps1 (PublishAot=true is the default)."

$summary = [ordered]@{
    runs                   = $runRecords.Count
    valid_first_frame_runs = $firstFrameStats.samples
    first_window_ms        = [ordered]@{
        median  = $firstWindowStats.median
        p95     = $firstWindowStats.p95
        samples = $firstWindowStats.samples
    }
    first_frame_ms         = [ordered]@{
        median  = $firstFrameStats.median
        p95     = $firstFrameStats.p95
        samples = $firstFrameStats.samples
    }
    decode_ms              = [ordered]@{
        median  = $decodeStats.median
        p95     = $decodeStats.p95
        samples = $decodeStats.samples
    }
    peak_ws_mb             = [ordered]@{
        median  = $peakStats.median
        p95     = $peakStats.p95
        samples = $peakStats.samples
    }
}

$result = [ordered]@{
    machine     = $machine
    date        = $date
    exe         = $ExePath
    image       = $ImagePath
    image_bytes = [long]$imageInfo.Length
    image_mb    = [math]::Round($imageInfo.Length / 1MB, 2)
    runs        = $runRecords
    summary     = $summary
}

if (-not [string]::IsNullOrWhiteSpace($OutputJson)) {
    $jsonPath = Resolve-InputPath -Path $OutputJson
    $jsonDirectory = [IO.Path]::GetDirectoryName($jsonPath)
    if (-not [string]::IsNullOrWhiteSpace($jsonDirectory) -and -not (Test-Path -LiteralPath $jsonDirectory -PathType Container)) {
        New-Item -ItemType Directory -Path $jsonDirectory -Force | Out-Null
    }

    $json = $result | ConvertTo-Json -Depth 8
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [IO.File]::WriteAllText($jsonPath, $json + [Environment]::NewLine, $utf8NoBom)
    Write-Output ("JSON written to: {0}" -f $jsonPath)
}

if ($missingFirstFrame) {
    [Console]::Error.WriteLine("error: one or more benchmark runs did not report a first_frame_ms value.")
    exit 1
}

exit 0
