# Reloads the 13 AscoLSI L_P_CONSIGNES_* reference tables of the local test database from production
# (Story 4.12), so the worker's LibelleConsigne lookups run against the same operator-edited values
# the legacy application reads. The steps run in this order: create the tables when missing
# (scripts/schema/03-ascolsi-reference-consignes.sql, idempotent - the integration test fixture drops
# every table on each run), TRUNCATE the 13 test tables, then copy every production row across with
# bcp in native format.
#
# Production is READ-ONLY: the only statement ever sent to it is the SELECT of a bcp queryout, over a
# connection opened with ApplicationIntent=ReadOnly (-K ReadOnly). Every write (CREATE, TRUNCATE,
# bcp in) targets the test database only.
#
# Both connection strings come from tests/Kape22Importer.Tests/appsettings.Test.json: AscoLSI (the
# test database) and AscoLSI_Production. Requires sqlcmd and bcp on PATH (ODBC tools).
#
# Usage:
#   pwsh scripts/sync-reference-consignes.ps1
#   pwsh scripts/sync-reference-consignes.ps1 -TestSettingsPath <path to appsettings.Test.json>

[CmdletBinding()]
param(
    [string] $TestSettingsPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'tests\Kape22Importer.Tests\appsettings.Test.json')
)

$ErrorActionPreference = 'Stop'
$schemaScript = Join-Path $PSScriptRoot 'schema\03-ascolsi-reference-consignes.sql'

# The 13 tables LibelleConsigneResolver reads, in the order ConsigneReferenceData declares them.
$tables = @(
    'L_P_CONSIGNES_CHUTAGE',
    'L_P_CONSIGNES_CODEOUTIL_COUPE',
    'L_P_CONSIGNES_DECOUPE',
    'L_P_CONSIGNES_DEGAZAGE_DETAIL',
    'L_P_CONSIGNES_DEGAZAGE_GLOBAL',
    'L_P_CONSIGNES_LINGOT',
    'L_P_CONSIGNES_MARQUAGE',
    'L_P_CONSIGNES_PITS',
    'L_P_CONSIGNES_POIDSMETRIQUE',
    'L_P_CONSIGNES_PRECHAUFFAGE_PARTICULIER',
    'L_P_CONSIGNES_REFROIDISSEMENT',
    'L_P_CONSIGNES_REFROIDISSOIRS',
    'L_P_CONSIGNES_SMQ'
)

# Splits an ADO.NET connection string into the server, database and login switches sqlcmd and bcp
# take. A connection string without a user id uses Windows authentication, whose switch differs
# between the two tools (-E for sqlcmd, -T for bcp).
function Get-SqlTarget([string] $ConnectionString, [string] $Name) {
    if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
        throw "ConnectionStrings:$Name is not set in $TestSettingsPath."
    }

    $builder = [System.Data.Common.DbConnectionStringBuilder]::new()
    # set_ConnectionString, not a property assignment: PowerShell adapts this builder as a dictionary, so
    # "$builder.ConnectionString = ..." would only add a key named ConnectionString.
    $builder.set_ConnectionString($ConnectionString)

    $value = $null
    $server = $null
    foreach ($key in @('Data Source', 'Server', 'Address', 'Addr', 'Network Address')) {
        if ($builder.TryGetValue($key, [ref] $value)) { $server = [string] $value; break }
    }

    $database = $null
    foreach ($key in @('Initial Catalog', 'Database')) {
        if ($builder.TryGetValue($key, [ref] $value)) { $database = [string] $value; break }
    }

    if (-not $server -or -not $database) {
        throw "ConnectionStrings:$Name must name both a server and a database."
    }

    $user = $null
    $password = $null
    foreach ($key in @('User ID', 'UID', 'User')) {
        if ($builder.TryGetValue($key, [ref] $value)) { $user = [string] $value; break }
    }
    foreach ($key in @('Password', 'PWD')) {
        if ($builder.TryGetValue($key, [ref] $value)) { $password = [string] $value; break }
    }

    # A missing password would leave -P without a value, so the tools would prompt or read the next switch.
    if ($user -and $null -eq $password) {
        throw "ConnectionStrings:$Name has a User ID but no Password."
    }

    $login = if ($user) { @('-U', $user, '-P', $password) } else { @() }
    return [pscustomobject] @{
        BcpLogin    = if ($user) { $login } else { @('-T') }
        Database    = $database
        Server      = $server
        SqlcmdLogin = if ($user) { $login } else { @('-E') }
    }
}

$settings = [System.IO.File]::ReadAllText($TestSettingsPath) | ConvertFrom-Json
$test = Get-SqlTarget $settings.ConnectionStrings.AscoLSI 'AscoLSI'
$production = Get-SqlTarget $settings.ConnectionStrings.AscoLSI_Production 'AscoLSI_Production'

# Production is read-only: refuse to run when the write target is the production source itself.
if ($test.Server -eq $production.Server -and $test.Database -eq $production.Database) {
    throw "ConnectionStrings:AscoLSI points at the production source ($($production.Server)/$($production.Database)); refusing to write to it."
}

# The string check above misses another spelling of the same host (alias, FQDN, IP, port, "."), so each
# server is also asked for its own name and current database. Only a SELECT is sent, and production is
# opened with the read-only intent.
function Get-ServerIdentity($Target, [string[]] $Intent) {
    $identity = & sqlcmd -S $Target.Server -d $Target.Database @($Target.SqlcmdLogin) -C -b @Intent -h -1 -W -Q "SET NOCOUNT ON; SELECT @@SERVERNAME + '/' + DB_NAME();"
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd could not read the identity of $($Target.Server)/$($Target.Database)." }
    return ($identity | Select-Object -First 1).Trim()
}

$testIdentity = Get-ServerIdentity $test @()
if ($testIdentity -eq (Get-ServerIdentity $production @('-K', 'ReadOnly'))) {
    throw "ConnectionStrings:AscoLSI resolves to the production source ($testIdentity); refusing to write to it."
}

function Invoke-TestSql([string[]] $Arguments) {
    & sqlcmd -S $test.Server -d $test.Database @($test.SqlcmdLogin) -C -b @Arguments
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed against the test database ($($test.Server)/$($test.Database))." }
}

# Creates the tables when missing, then empties them, so a production row deleted since the last run
# never survives in the test copy.
Invoke-TestSql @('-i', $schemaScript)
Invoke-TestSql @('-Q', (($tables | ForEach-Object { "TRUNCATE TABLE dbo.$_;" }) -join ' '))

$workDir = Join-Path ([System.IO.Path]::GetTempPath()) "sync-reference-consignes-$PID"
New-Item -ItemType Directory -Path $workDir -Force | Out-Null
try {
    foreach ($table in $tables) {
        $dataFile = Join-Path $workDir "$table.bcp"

        # Read side: a SELECT on production, read-only intent, native format.
        & bcp "SELECT * FROM dbo.$table" queryout $dataFile -N -S $production.Server -d $production.Database @($production.BcpLogin) -K ReadOnly
        if ($LASTEXITCODE -ne 0) { throw "bcp queryout failed for $table on $($production.Server)." }

        # Write side: the test database only. -E keeps the production identity values (the resolver
        # takes the first L_P_CONSIGNES_DEGAZAGE_DETAIL row by Id) and -k keeps NULLs as NULLs.
        & bcp "dbo.$table" in $dataFile -N -E -k -S $test.Server -d $test.Database @($test.BcpLogin)
        if ($LASTEXITCODE -ne 0) { throw "bcp in failed for $table on $($test.Server)/$($test.Database)." }
    }
}
finally {
    Remove-Item -LiteralPath $workDir -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "Reloaded $($tables.Count) L_P_CONSIGNES_* reference tables from $($production.Server)/$($production.Database) into $($test.Server)/$($test.Database)."
