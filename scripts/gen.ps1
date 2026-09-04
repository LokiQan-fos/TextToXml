# Regenerates Templates/P60.xsd and src/Kape22Importer/Kape22File.cs from Templates/P60.xml.
#
# Templates/P60.xml is the single source of truth for the P60 format (AR-5). This script derives the
# two dependent artifacts mechanically so an evolution of the descriptor never has to be transcribed
# by hand (risk R-5): one xs:element / one DTO property per <value>, in descriptor order (R-4),
# xs:int + minOccurs="0" + int? for datatype="int" (PRD D27), xs:string + string otherwise (D6).
#
# There is no build- or CI-time code generator: xsd.exe does not exist on .NET 10 and the output is
# small and stable. Run this only when Templates/P60.xml changes, then commit all three files together.
# Kape22Importer.Tests pins the output against the descriptor (AC-FR1-13) and the schema
# (Kape22File_MirrorsP60Xsd), so a stale regeneration fails the build.
#
# Usage:
#   pwsh scripts/gen.ps1            Rewrites the two files.
#   pwsh scripts/gen.ps1 -Check     Exits non-zero if either file is out of date (no write).

[CmdletBinding()]
param(
    [switch] $Check
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$descriptorPath = Join-Path $repoRoot 'Templates\P60.xml'
$xsdPath = Join-Path $repoRoot 'Templates\P60.xsd'
$dtoPath = Join-Path $repoRoot 'src\Kape22Importer\Kape22File.cs'
$newline = "`r`n"

[xml] $descriptor = Get-Content -LiteralPath $descriptorPath
$blocs = 'header', 'message', 'footer'

function Get-Fields([string] $bloc) {
    # Every <value> of the Bloc becomes one schema element: none is schema-ignored, since Story 1.6
    # emits all of them into the normalized XML (a string Champ stays present even when blank, AC-FR5-6).
    $descriptor.commande.$bloc.value | ForEach-Object {
        [pscustomobject]@{
            Id   = $_.Id
            Type = if ($_.datatype) { $_.datatype } else { 'string' }
        }
    }
}

function Build-Xsd {
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add('<?xml version="1.0" encoding="utf-8"?>')
    $lines.Add('<!--')
    $lines.Add('  Static schema for the P60 (KAPE22) normalized XML document produced by TextToXml (AR-3, D10).')
    $lines.Add('  Generated from Templates/P60.xml by scripts/gen.ps1 - do not edit by hand. One xs:element per')
    $lines.Add('  <value>, same order as the descriptor (xs:sequence, R-4), xs:int for datatype="int" and')
    $lines.Add('  xs:string otherwise (D6). A typed element is minOccurs="0" because Step 1 omits a blank typed')
    $lines.Add('  Champ (PRD D27); a string element is always emitted, even empty (AC-FR5-6). The Kape22File DTO')
    $lines.Add('  is generated from the same source and committed (AR-4). No target namespace: the normalized')
    $lines.Add('  XML is namespace-free.')
    $lines.Add('-->')
    $lines.Add('<xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" elementFormDefault="unqualified">')
    $lines.Add('  <xs:element name="file">')
    $lines.Add('    <xs:complexType>')
    $lines.Add('      <xs:sequence>')
    foreach ($bloc in $blocs) {
        $lines.Add("        <xs:element name=""$bloc"" type=""$bloc"" />")
    }
    $lines.Add('      </xs:sequence>')
    $lines.Add('    </xs:complexType>')
    $lines.Add('  </xs:element>')
    foreach ($bloc in $blocs) {
        $lines.Add('')
        $lines.Add("  <xs:complexType name=""$bloc"">")
        $lines.Add('    <xs:sequence>')
        foreach ($field in Get-Fields $bloc) {
            if ($field.Type -eq 'int') {
                $lines.Add("      <xs:element name=""$($field.Id)"" type=""xs:int"" minOccurs=""0"" />")
            }
            else {
                $lines.Add("      <xs:element name=""$($field.Id)"" type=""xs:string"" />")
            }
        }
        $lines.Add('    </xs:sequence>')
        $lines.Add('  </xs:complexType>')
    }
    $lines.Add('</xs:schema>')
    return ($lines -join $newline) + $newline
}

function Build-Dto {
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add('using System.Xml.Serialization;')
    $lines.Add('')
    $lines.Add('namespace Kape22Importer;')
    $lines.Add('')
    $lines.Add('// DTO for the P60 (KAPE22) normalized XML document, generated from Templates/P60.xml by')
    $lines.Add('// scripts/gen.ps1 - do not edit by hand (AR-4). Members follow the P60.xsd xs:sequence /')
    $lines.Add('// descriptor <value> order, NOT CC-4 alphabetical order, so the file stays diffable against')
    $lines.Add('// P60.xml and P60.xsd (risk R-5). A typed Champ is int? because Step 1 omits a blank one')
    $lines.Add('// (PRD D27); an omitted element deserializes to null. Every class is partial: a property')
    $lines.Add('// added by hand goes in a second, alphabetically sorted partial declaration (R-5).')
    $lines.Add('')
    $lines.Add('[XmlRoot("file")]')
    $lines.Add('public sealed partial class Kape22File')
    $lines.Add('{')
    for ($i = 0; $i -lt $blocs.Count; $i++) {
        $bloc = $blocs[$i]
        $class = 'Kape22File' + [char]::ToUpper($bloc[0]) + $bloc.Substring(1)
        $lines.Add("    [XmlElement(""$bloc"")]")
        $lines.Add("    public $class $([char]::ToUpper($bloc[0]) + $bloc.Substring(1)) { get; set; } = new();")
        if ($i -ne $blocs.Count - 1) { $lines.Add('') }
    }
    $lines.Add('}')
    foreach ($bloc in $blocs) {
        $class = 'Kape22File' + [char]::ToUpper($bloc[0]) + $bloc.Substring(1)
        $lines.Add('')
        $lines.Add("public sealed partial class $class")
        $lines.Add('{')
        $fields = @(Get-Fields $bloc)
        for ($i = 0; $i -lt $fields.Count; $i++) {
            $field = $fields[$i]
            $lines.Add("    [XmlElement(""$($field.Id)"")]")
            if ($field.Type -eq 'int') {
                $lines.Add("    public int? $($field.Id) { get; set; }")
            }
            else {
                $lines.Add("    public string $($field.Id) { get; set; } = string.Empty;")
            }
            if ($i -ne $fields.Count - 1) { $lines.Add('') }
        }
        $lines.Add('}')
    }
    return ($lines -join $newline) + $newline
}

$targets = @(
    [pscustomobject]@{ Path = $xsdPath; Content = Build-Xsd },
    [pscustomobject]@{ Path = $dtoPath; Content = Build-Dto }
)

$stale = @()
foreach ($target in $targets) {
    $current = if (Test-Path -LiteralPath $target.Path) {
        [System.IO.File]::ReadAllText($target.Path)
    }
    else {
        ''
    }

    if ($current -ceq $target.Content) {
        Write-Host "up to date: $($target.Path)"
        continue
    }

    $stale += $target.Path
    if (-not $Check) {
        [System.IO.File]::WriteAllText($target.Path, $target.Content)
        Write-Host "regenerated: $($target.Path)"
    }
}

if ($Check -and $stale.Count -gt 0) {
    Write-Error "Out of date, run 'pwsh scripts/gen.ps1': $($stale -join ', ')"
    exit 1
}
