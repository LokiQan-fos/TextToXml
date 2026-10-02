# Replays the real GpaoImportP60 worker flow end-to-end: drop Fichier(s) in a scratch inbox, start the
# actual Launcher (MicroServices.sln) which spawns its own local MQTT broker and starts every worker
# in-process, let the real InboxScanner tick pick the Fichier(s) up, and verify the outcome from the
# outside - the Launcher /workers API, the archived files on disk, and the rows written to the local
# AscoLSI_Test database, one exported XML per Fichier, and the worker's rows in the test Logs database
# (the worker is stopped through the Launcher API first, so its batched Logs rows are flushed).
# Optionally follows up with Kape22ProductionDataParityTests (already in this repo) filtered to the
# same Fichier(s), to confirm the mapped columns match what the legacy
# application wrote for them in production (read-only).
#
# This is the manual procedure run ad hoc against Epic 3 (Story 3.4/3.5/3.6); kept here so it can be
# replayed as-is or extended for the workers that later epics add, instead of re-deriving it each time.
#
# GpaoImportP60.json (SVN-tracked in MicroServicesRoot) is edited for the duration of the run only -
# its original content is restored in a finally block, including on error or Ctrl+C. The Launcher and
# its broker subprocess are always torn down the same way. By default the scratch inbox, the Launcher
# log and the rows this run inserted into AscoLSI_Test are cleaned up too; pass -KeepArtifacts to leave
# them for inspection.
#
# Requires: the MicroServices.sln checkout (MicroServicesRoot), a local SQL Server reachable at the
# connection strings configured in tests/Kape22Importer.Tests/appsettings.Test.json (AscoLSI,
# MQTTnetServices for the Launcher's Logs sink, and AscoLSI_Production for -SkipProductionCompare:$false),
# and sqlcmd on PATH.
#
# Usage:
#   pwsh scripts/e2e-worker-import.ps1
#   pwsh scripts/e2e-worker-import.ps1 -Fichiers P60_847_682_090,P60_847_682_091
#   pwsh scripts/e2e-worker-import.ps1 -SkipProductionCompare
#   pwsh scripts/e2e-worker-import.ps1 -KeepArtifacts

[CmdletBinding()]
param(
    [string[]] $Fichiers = @('P60_847_682_081', 'P60_847_682_082'),
    [string] $MicroServicesRoot = 'C:\Users\Administrateur\Documents\MicroServices',
    [string] $InboxPath = 'C:\Temp\GpaoImportP60_E2E',
    [int] $PollTimeoutSeconds = 30,
    [switch] $SkipProductionCompare,
    [switch] $KeepArtifacts
)

$ErrorActionPreference = 'Stop'
# Reused MSBuild nodes spawned by the builds below outlive this script and inherit its redirected stdout,
# so GpaoImportP60WorkerEndToEndTests (which reads that stream to the end) would hang until they idle out.
$env:MSBUILDDISABLENODEREUSE = '1'
$repoRoot = Split-Path -Parent $PSScriptRoot
$testProject = Join-Path $repoRoot 'tests\Kape22Importer.Tests'
$testSettingsPath = Join-Path $testProject 'appsettings.Test.json'
$p60Dir = Join-Path $repoRoot 'P60'
$configPath = Join-Path $MicroServicesRoot 'GPAO\ImportP60\GpaoImportP60.json'
$launcherProject = Join-Path $MicroServicesRoot 'Launcher\Launcher.csproj'
$launcherBinDir = Join-Path $MicroServicesRoot 'Launcher\bin\Debug\net10.0'
$logFile = Join-Path (Split-Path -Parent $InboxPath) 'GpaoImportP60_E2E_launcher.log'
$errLogFile = Join-Path (Split-Path -Parent $InboxPath) 'GpaoImportP60_E2E_launcher.err.log'
# Story 6.3 (AC-FR26-5): the worker refuses to start without an absolute export folder distinct from the inbox.
$exportPath = Join-Path (Split-Path -Parent $InboxPath) 'GpaoImportP60_E2E_Export'

foreach ($required in @($configPath, $launcherProject, $testSettingsPath, $p60Dir)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "Required path not found: $required (check -MicroServicesRoot)."
    }
}

foreach ($fichier in $Fichiers) {
    if (-not (Test-Path -LiteralPath (Join-Path $p60Dir $fichier))) {
        throw "Fichier not found in P60/: $fichier"
    }
}

$testSettings = [System.IO.File]::ReadAllText($testSettingsPath) | ConvertFrom-Json
$ascoLsiTest = $testSettings.ConnectionStrings.AscoLSI
$mqttLogTest = $testSettings.ConnectionStrings.MQTTnetServices
if ([string]::IsNullOrWhiteSpace($ascoLsiTest)) {
    throw "ConnectionStrings:AscoLSI is not set in $testSettingsPath."
}
# Story 6.4-bis: this value only feeds MICROSERVICE_LOG_CONNECTION_STRING (step 3). Assigning a blank value
# to that variable removes it, and SharedLogger then falls back to the production Logs server.
if ([string]::IsNullOrWhiteSpace($mqttLogTest)) {
    throw "ConnectionStrings:MQTTnetServices is not set in $testSettingsPath."
}
$logsDb = [System.Data.Common.DbConnectionStringBuilder]::new()
# A plain property assignment would add a "ConnectionString" key through PowerShell's dictionary adapter.
$logsDb.set_ConnectionString($mqttLogTest)
# DbConnectionStringBuilder resolves no synonyms, so "Data Source"/"Initial Catalog" would yield an empty -S/-d.
if (-not $logsDb.ContainsKey('Server') -or -not $logsDb.ContainsKey('Database') -or
    [string]::IsNullOrWhiteSpace($logsDb['Server']) -or [string]::IsNullOrWhiteSpace($logsDb['Database'])) {
    throw "ConnectionStrings:MQTTnetServices in $testSettingsPath must use the Server= and Database= keywords."
}
# The Logs sink of this run must never target production: only a *_Test database is accepted.
if ($logsDb['Database'] -notlike '*_Test') {
    throw "ConnectionStrings:MQTTnetServices in $testSettingsPath targets '$($logsDb['Database'])', not a *_Test database."
}
$logsSqlcmd = @('-S', $logsDb['Server'], '-d', $logsDb['Database'], '-C', '-b', '-h', '-1', '-W')

# Returns the single integer a "SET NOCOUNT ON; SELECT <n>" sqlcmd call printed; throws on anything else,
# so blank output never turns into 0.
function ConvertFrom-SqlScalar([object[]] $Output, [string] $What) {
    $line = $Output | Where-Object { "$_" -match '^\s*\d+\s*$' } | Select-Object -First 1
    if ($null -eq $line) { throw "The $What query returned no integer: '$($Output -join ' ')'." }
    return [long]"$line".Trim()
}

# Story 6.8: polls the test Logs table for rows after $logsIdBefore matching $Where, for at most
# $PollTimeoutSeconds; returns their count, or throws naming the Logs database when none landed.
function Wait-LogsRow([string] $Where, [string] $What) {
    $deadline = (Get-Date).AddSeconds($PollTimeoutSeconds)
    while ($true) {
        $output = & sqlcmd @logsSqlcmd -Q "SET NOCOUNT ON; IF OBJECT_ID('dbo.Logs') IS NULL SELECT 0 ELSE SELECT COUNT(*) FROM dbo.Logs WHERE Id > $logsIdBefore AND $Where"
        if ($LASTEXITCODE -ne 0) { throw "The $What Logs query failed." }
        $count = ConvertFrom-SqlScalar $output "$What Logs"
        if ($count -gt 0) { return $count }
        if ((Get-Date) -ge $deadline) {
            throw "No $What row was written to $($logsDb['Database']) within $PollTimeoutSeconds s."
        }
        Start-Sleep -Seconds 1
    }
}

function Invoke-Sql([string] $Query) {
    & sqlcmd -S localhost -d AscoLSI_Test -C -Q $Query
}

# Start-Process's -RedirectStandardOutput keeps its own handle open on $logFile for as long as the
# Launcher runs, so a plain ReadAllText/Get-Content call can lose a race against it (file locked by
# another process). Opened explicitly with FileShare.ReadWrite so polling the live log never races it.
function Read-LiveLog([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return '' }
    $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    try {
        return (New-Object System.IO.StreamReader($stream)).ReadToEnd()
    }
    finally {
        $stream.Dispose()
    }
}

# --- 0. Story 4.12: reload the 13 L_P_CONSIGNES_* reference tables from production (SELECT only) so
# the worker resolves LibelleConsigne against the same values the legacy application reads. ---
& (Join-Path $PSScriptRoot 'sync-reference-consignes.ps1') -TestSettingsPath $testSettingsPath

# --- 1. Scratch inbox: drop the requested Fichiers where the worker will see them. ---
if (Test-Path -LiteralPath $InboxPath) { Remove-Item -LiteralPath $InboxPath -Recurse -Force }
# Exports left by an earlier -KeepArtifacts run would otherwise be listed as this run's.
if (Test-Path -LiteralPath $exportPath) { Remove-Item -LiteralPath $exportPath -Recurse -Force }
New-Item -ItemType Directory -Path $InboxPath | Out-Null
foreach ($fichier in $Fichiers) {
    Copy-Item -LiteralPath (Join-Path $p60Dir $fichier) -Destination $InboxPath
}
Write-Host "Dropped $($Fichiers.Count) Fichier(s) into $InboxPath"

# --- 2. Point the worker at the test database and the scratch inbox, for this run only. ---
$originalConfig = [System.IO.File]::ReadAllText($configPath)
$patchedConfig = $originalConfig | ConvertFrom-Json
$patchedConfig.ConnectionStrings.AscoLSI = $ascoLsiTest
$patchedConfig.Import.InboxPath = $InboxPath
$patchedConfig.Import | Add-Member -NotePropertyName XmlExportPath -NotePropertyValue $exportPath -Force
[System.IO.File]::WriteAllText($configPath, ($patchedConfig | ConvertTo-Json -Depth 5))

$launcherProcess = $null
try {
    # --- 3. Build and start the real Launcher; it spawns its own local MQTT broker. ---
    dotnet build $launcherProject -c Debug --nologo -v minimal
    if ($LASTEXITCODE -ne 0) { throw 'Launcher build failed.' }

    # Story 6.4-bis: the last Logs row before the Launcher starts, so step 5 can prove this run logged to
    # the test Logs database. The table may not exist yet, since the Serilog sink creates it on first use.
    $logsIdBefore = & sqlcmd @logsSqlcmd -Q "SET NOCOUNT ON; IF OBJECT_ID('dbo.Logs') IS NULL SELECT 0 ELSE SELECT ISNULL(MAX(Id), 0) FROM dbo.Logs"
    if ($LASTEXITCODE -ne 0) { throw 'The Logs baseline query failed.' }
    $logsIdBefore = ConvertFrom-SqlScalar $logsIdBefore 'Logs baseline'

    # MICROSERVICE_LOG_CONNECTION_STRING keeps the Serilog/SQL sink off the unreachable production Logs
    # server for this local run. It is the only Logs-sink routing: since Story 6.4 the worker JSON has no
    # MQTTnetServices key. Assigning '' to LAUNCHER_API_KEY removes the variable, and the Launcher then
    # defaults to an empty key, the unauthenticated local mode.
    $env:MICROSERVICE_LOG_CONNECTION_STRING = $mqttLogTest
    $env:LAUNCHER_API_KEY = ''
    $launcherProcess = Start-Process -FilePath 'dotnet' -ArgumentList 'Launcher.dll' `
        -WorkingDirectory $launcherBinDir -RedirectStandardOutput $logFile -RedirectStandardError $errLogFile `
        -PassThru -WindowStyle Hidden
    Write-Host "Launcher started (PID $($launcherProcess.Id)), waiting for the first import tick..."

    # --- 4. Poll the Launcher log until every dropped Fichier has an outcome line, or time out. ---
    $deadline = (Get-Date).AddSeconds($PollTimeoutSeconds)
    $pending = [System.Collections.Generic.HashSet[string]]::new([string[]]$Fichiers)
    while ($pending.Count -gt 0 -and (Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 1
        $content = Read-LiveLog $logFile
        foreach ($fichier in @($pending)) {
            if ($content -match [regex]::Escape($fichier) + '.*processed') {
                [void]$pending.Remove($fichier)
            }
        }
    }
    if ($pending.Count -gt 0) {
        throw "Timed out waiting for: $($pending -join ', '). See $logFile"
    }

    # --- 5. Report what actually happened, from the outside, and assert the exports, the persisted
    # labels and the worker's Logs rows (after a graceful worker stop that flushes them). ---
    Write-Host "`n--- Launcher /workers ---"
    (Invoke-RestMethod 'http://127.0.0.1:5050/workers') | Where-Object Name -eq 'GpaoImportP60' | Format-List

    Write-Host "--- Archived files ---"
    # Write-Host per line, not a piped Format-Table: the latter buffers and can print after the dotnet
    # test output that follows, out of order, since dotnet.exe writes to the console directly.
    Get-ChildItem -LiteralPath (Join-Path $InboxPath 'archive') -Recurse -File | ForEach-Object { Write-Host $_.FullName }

    # Story 6.8: exactly one <Fichier>_<yyyyMMddHHmmss>.xml per seeded Fichier, and nothing else.
    Write-Host "`n--- Exported XML files (FR-26) ---"
    $exports = @(if (Test-Path -LiteralPath $exportPath) { Get-ChildItem -LiteralPath $exportPath -File })
    $exports | ForEach-Object { Write-Host $_.FullName }
    foreach ($fichier in $Fichiers) {
        $count = @($exports | Where-Object Name -match ('^' + [regex]::Escape($fichier) + '_\d{14}\.xml$')).Count
        if ($count -ne 1) { throw "Expected 1 exported XML for $fichier in $exportPath, found $count." }
    }
    if ($exports.Count -ne $Fichiers.Count) {
        throw "Expected $($Fichiers.Count) exported XML file(s) in $exportPath, found $($exports.Count): $($exports.Name -join ', ')."
    }

    Write-Host "`n--- L_D_KAPE22 rows inserted this run ---"
    $numeros = $Fichiers | ForEach-Object { ($_ -split '_')[-1] }
    Invoke-Sql "SELECT * FROM L_D_KAPE22 WHERE NumeroFichier IN ('$($numeros -join "','")')"

    Write-Host "--- L_D_LOG_COMMANDE rows inserted this run ---"
    Invoke-Sql "SELECT Id, Commande, Message, [OF], [Date] FROM L_D_LOG_COMMANDE WHERE [OF] IN (SELECT RTRIM([OF]) FROM L_D_KAPE22 WHERE NumeroFichier IN ('$($numeros -join "','")'))"

    # Story 4.12: every persisted L_D_CONSIGNES row of these OFs must carry a label.
    $consignesFilter = "[OF] IN (SELECT RIGHT('000000000000' + RTRIM([OF]), 12) FROM L_D_KAPE22 WHERE NumeroFichier IN ('$($numeros -join "','")'))"
    Write-Host "--- L_D_CONSIGNES labels ---"
    Invoke-Sql "SELECT [OF], CodeOperation, TypeConsigne, ConsigneGPAO, CodeConsigne, LibelleConsigne FROM L_D_CONSIGNES WHERE $consignesFilter ORDER BY [OF], CodeOperation, TypeConsigne, ConsigneGPAO"
    # -b and the exit code check keep a failed query from reading as a zero count; the row total makes a
    # run that persisted nothing fail too.
    $counts = & sqlcmd -S localhost -d AscoLSI_Test -C -b -h -1 -W -s ',' -Q "SET NOCOUNT ON; SELECT COUNT(*), SUM(CASE WHEN LibelleConsigne IS NULL THEN 1 ELSE 0 END), SUM(CASE WHEN LibelleConsigne = '?' THEN 1 ELSE 0 END), SUM(CASE WHEN ConsigneGPAO = 1 AND SizeCodeConsigne <> 0 THEN 1 ELSE 0 END), (SELECT COUNT(*) FROM L_D_CONSIGNES c WHERE $consignesFilter AND c.SizeCodeConsigne <> 0 AND NOT EXISTS (SELECT 1 FROM L_D_CONSIGNES m WHERE m.[OF] = c.[OF] AND m.CodeOperation = c.CodeOperation AND m.TypeConsigne = c.TypeConsigne AND m.ConsigneGPAO <> c.ConsigneGPAO)), SUM(CASE WHEN ConsigneGPAO = 0 AND (TypeConsigne = 13 OR (TypeConsigne = 24 AND SizeCodeConsigne = 18)) AND LibelleConsigne = '?' THEN 1 ELSE 0 END), SUM(CASE WHEN ConsigneGPAO = 0 AND SizeCodeConsigne = 0 THEN 1 ELSE 0 END) FROM L_D_CONSIGNES WHERE $consignesFilter"
    if ($LASTEXITCODE -ne 0) { throw 'The L_D_CONSIGNES label check query failed.' }
    $total, $nullLibelles, $unresolvedLibelles, $received, $unpaired, $unresolvedComposites, $svtWorking = ($counts | Select-Object -First 1).Split(',')
    if ([int]$total -eq 0) { throw 'No L_D_CONSIGNES row was persisted for the Fichiers of this run.' }
    if ([int]$nullLibelles -ne 0) { throw "$nullLibelles L_D_CONSIGNES row(s) persisted with a NULL LibelleConsigne." }
    # The resolver never returns NULL, so the check above alone cannot fail. Every row at "?" means the
    # worker resolved nothing, for example from an empty reference snapshot.
    if ([int]$unresolvedLibelles -eq [int]$total) { throw "All $total L_D_CONSIGNES row(s) persisted with LibelleConsigne '?'." }
    # Story 4.13 (AC-FR19-6): every decoded row pairs with its other ConsigneGPAO value on the same key (the
    # SVT row, SizeCodeConsigne 0, has no working copy), and every working composite row (type 13, and type
    # 24 of the size-18 block) carries its composite label, never "?". The pairing check skips size 0, so
    # the SVT row is checked on its own: it never gets a ConsigneGPAO=0 row.
    if ([int]$received -eq 0) { throw 'No decoded ConsigneGPAO=1 L_D_CONSIGNES row was persisted for the Fichiers of this run.' }
    if ([int]$unpaired -ne 0) { throw "$unpaired decoded L_D_CONSIGNES row(s) without their ConsigneGPAO=0/1 counterpart." }
    if ([int]$unresolvedComposites -ne 0) { throw "$unresolvedComposites ConsigneGPAO=0 composite row(s) persisted with LibelleConsigne '?'." }
    if ([int]$svtWorking -ne 0) { throw "$svtWorking SVT L_D_CONSIGNES row(s) persisted with ConsigneGPAO=0." }

    # Story 6.8: the worker itself must have journaled to the test Logs database: at least one row written
    # by the worker during its tick (AbstractService prefixes them with its padded Name; the Launcher's
    # WorkerLifecycle rows are excluded) and exactly one "processed: archived" row per seeded Fichier, all
    # after the baseline.
    # The shared Logs sink batches its writes and is never disposed, so the Stop-Process teardown below
    # would drop its pending rows. The worker is stopped through the Launcher API first: its last act is
    # the "Worker stopped." row on the worker's own logger, and the sink writes in order, so once that row
    # has landed every earlier worker row has too.
    Write-Host "--- Logs rows written this run ($($logsDb['Database'])) ---"
    Invoke-RestMethod -Method Post 'http://127.0.0.1:5050/workers/GpaoImportP60/stop' -TimeoutSec $PollTimeoutSeconds | Out-Null
    [void](Wait-LogsRow "LEFT(Message, 15) = '[GpaoImportP60 ' AND CHARINDEX('Worker stopped.', Message) > 0" "'GpaoImportP60 Worker stopped.'")
    $workerRows = Wait-LogsRow "LEFT(Message, 15) = '[GpaoImportP60 ' AND CHARINDEX('WorkerLifecycle', Message) = 0" 'GpaoImportP60 tick'
    Write-Host "$workerRows GpaoImportP60 tick row(s)"
    foreach ($fichier in $Fichiers) {
        # The exact rendered InboxScanner message, so the retry-cap row ("no longer processed"), a rejected
        # outcome or a longer Fichier name containing this one never matches. The double quotes Serilog
        # renders are spelled CHAR(34): a literal one would split the sqlcmd -Q argument.
        $escaped = $fichier.Replace("'", "''")
        $processed = Wait-LogsRow "CHARINDEX(CONCAT('Fichier ', CHAR(34), '$escaped', CHAR(34), ' processed: ', CHAR(34), 'archived', CHAR(34)), Message) > 0" "'$fichier processed: archived'"
        if ($processed -ne 1) { throw "Expected 1 '$fichier processed: archived' Logs row, found $processed." }
    }
}
finally {
    # --- 6. Always tear down the Launcher and its broker, and always restore the tracked config. On the
    # happy path the worker is already stopped and its Logs rows flushed (step 5). ---
    if ($launcherProcess -and -not $launcherProcess.HasExited) {
        Stop-Process -Id $launcherProcess.Id -Force -ErrorAction SilentlyContinue
    }
    $broker = Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" |
        Where-Object { $_.CommandLine -like '*MqttNetServer.dll*' }
    foreach ($proc in $broker) {
        Stop-Process -Id $proc.ProcessId -Force -ErrorAction SilentlyContinue
    }

    [System.IO.File]::WriteAllText($configPath, $originalConfig)
    Write-Host "Restored $configPath"

    if (-not $KeepArtifacts) {
        $numeros = $Fichiers | ForEach-Object { ($_ -split '_')[-1] }
        Invoke-Sql "DELETE FROM L_D_LOG_COMMANDE WHERE [OF] IN (SELECT RTRIM([OF]) FROM L_D_KAPE22 WHERE NumeroFichier IN ('$($numeros -join "','")')); DELETE FROM L_D_KAPE22 WHERE NumeroFichier IN ('$($numeros -join "','")');" | Out-Null
        Remove-Item -LiteralPath $InboxPath -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $exportPath -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $logFile, $errLogFile -Force -ErrorAction SilentlyContinue
        Write-Host 'Cleaned up scratch inbox, logs and inserted test rows (-KeepArtifacts to skip this).'
    }
}

# --- 7. Optional: reuse the existing production-parity Theory, scoped to the Fichiers just run. ---
if (-not $SkipProductionCompare) {
    # E2E_BUILD_ALREADY_DONE=1 is set by GpaoImportP60WorkerEndToEndTests (via ProcessStartInfo.Environment)
    # to signal that this exact project is already built and loaded by the outer testhost. Rebuilding it
    # here would race that lock and can hang or deadlock the whole outer run instead of just this test, so
    # --no-build is passed only in that case. In standalone usage the variable is absent, so this step
    # builds the test project itself, matching the script's own documented standalone entry point.
    $dotnetTestArgs = @('test', $testProject)
    if ($env:E2E_BUILD_ALREADY_DONE -eq '1') {
        $dotnetTestArgs += '--no-build'
    }

    foreach ($fichier in $Fichiers) {
        # DisplayName carries the Theory parameter ("fichierName: ..."), FullyQualifiedName does not -
        # filtering the class by FullyQualifiedName and the Fichier by DisplayName is what actually narrows
        # to one test case; a FullyQualifiedName~ filter on the Fichier alone matches nothing.
        dotnet @dotnetTestArgs --filter "FullyQualifiedName~Kape22ProductionDataParityTests&DisplayName~$fichier" --nologo -v minimal
        # B-4 (Story 4.10): guarded the same way as the Launcher build step above - a forced
        # production-parity failure must stop the script instead of continuing silently.
        if ($LASTEXITCODE -ne 0) { throw "Production-parity test failed for $fichier." }
    }
}
