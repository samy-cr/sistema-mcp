# Runs all test scenarios in tests\scenarios against the MCP server (over stdio, like a real client),
# checks the expectations in "_test", generates the .ssm files and writes a protocol.
# Usage: .\run-scenarios.ps1 [-Exe <SistemaMcp.exe>] [-Out <folder>] [-Filter P0*]
param(
    [string]$Exe = "$PSScriptRoot\..\publish\SistemaMcp\SistemaMcp.exe",
    [string]$Out = "$PSScriptRoot\..\testlauf\$(Get-Date -Format 'yyyyMMdd-HHmm')",
    [string]$Filter = "*"
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\McpSession.ps1"

New-Item -ItemType Directory -Force $Out | Out-Null
$Out = (Resolve-Path $Out).Path
$scenarioDir = (Resolve-Path "$PSScriptRoot\..\tests\scenarios").Path
$session = Start-McpSession $Exe
$results = New-Object System.Collections.ArrayList

function Add-Result($id, $title, $ok, $details) {
    [void]$results.Add([pscustomobject]@{ id = $id; title = $title; ok = [bool]$ok; details = (@($details) -join '; ') })
    $mark = if ($ok) { 'OK  ' } else { 'FAIL' }
    Write-Output ("[{0}] {1,-28} {2}" -f $mark, $id, $title)
    foreach ($d in @($details)) { Write-Output "       $d" }
}

function Test-Report($report, $expect) {
    $problems = @()
    if ($null -ne $expect.complete -and $report.complete -ne $expect.complete) { $problems += "complete=$($report.complete), expected $($expect.complete)" }
    foreach ($f in @($expect.questions)) { if ($f -and -not ($report.questions | Where-Object { $_.field -eq $f })) { $problems += "question '$f' missing" } }
    foreach ($e in @($expect.errors)) { if ($e -and -not ($report.errors | Where-Object { $_.text -like "*$e*" })) { $problems += "error '$e' missing" } }
    foreach ($w in @($expect.warnings)) { if ($w -and -not ($report.warnings | Where-Object { $_.text -like "*$w*" })) { $problems += "warning '$w' missing" } }
    foreach ($kind in 'estimatePl', 'plr', 'plrReached') {
        if (-not $expect.$kind) { continue }
        foreach ($p in $expect.$kind.PSObject.Properties) {
            $sf = $report.estimate | Where-Object { $_.id -eq $p.Name }
            $actual = switch ($kind) { 'estimatePl' { $sf.pl } 'plr' { $sf.plr } 'plrReached' { $sf.plrReached } }
            if ("$actual" -ne "$($p.Value)") { $problems += "$kind $($p.Name) = '$actual', expected '$($p.Value)'" }
        }
    }
    $problems
}

try {
    $status = Invoke-McpTool $session 'sistema_status' @{}
    Write-Output "Server $($status.server), SISTEMA: $($status.sistemaInstallDir), Bibliotheken: $($status.libraryComponents) Bauteile"

    foreach ($file in Get-ChildItem $scenarioDir -Filter "$Filter.json" | Sort-Object Name) {
        $meta = (Get-Content $file.FullName -Raw -Encoding UTF8 | ConvertFrom-Json)._test
        $id = $file.BaseName
        if ($meta.requires -eq 'turck') {
            $src = Invoke-McpTool $session 'library_list_sources' @{}
            if (-not ($src.sources | Where-Object { $_.id -eq 'turck' })) { [void](Invoke-McpTool $session 'library_download' @{ manufacturerOrUrl = 'turck' }) }
        }
        $sw = [Diagnostics.Stopwatch]::StartNew()
        $report = Invoke-McpTool $session 'sistema_validate_spec' @{ spec = $file.FullName }
        $problems = @(Test-Report $report $meta.expect)
        $info = @()
        if ($report.complete -and $meta.kind -eq 'positive') {
            $target = Join-Path $Out "$id.ssm"
            $created = Invoke-McpTool $session 'sistema_create_project' @{ spec = $file.FullName; outputPath = $target; overwrite = $true }
            if (-not $created.created) { $problems += "create failed: $($created.reason)" }
            else {
                $read = Invoke-McpTool $session 'sistema_read_project' @{ path = $target; includeTexts = $true }
                $specJson = Get-Content $file.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
                if (@($read.safetyFunctions).Count -ne @($specJson.safetyFunctions).Count) { $problems += "read back: number of safety functions differs" }
                if ($specJson.project.name -and $read.project.name -ne $specJson.project.name) { $problems += "read back: project name differs ('$($read.project.name)')" }
                $info += "-> $id.ssm"
            }
        }
        $info += "{0} Fragen, {1} Fehler, {2} Warnungen, {3} ms" -f @($report.questions).Count, @($report.errors).Count, @($report.warnings).Count, $sw.ElapsedMilliseconds
        Add-Result $id $meta.title ($problems.Count -eq 0) (@($problems) + $info)
        $report | ConvertTo-Json -Depth 30 | Set-Content (Join-Path $Out "$id.report.json") -Encoding UTF8
    }

    if ($Filter -eq '*') {
        $r = Invoke-McpTool $session 'sistema_validate_spec' @{ spec = '{ "project": { "name": "kaputt" ' }
        Add-Result 'X01_malformed_json' 'Defektes JSON -> lesbare Fehlermeldung' ($r.error -like '*could not be read*') @("$($r.error)")

        $r = Invoke-McpTool $session 'sistema_validate_spec' @{ spec = 'C:\gibt\es\nicht.json' }
        Add-Result 'X02_missing_file' 'Nicht vorhandene Spezifikationsdatei' ($null -ne $r.error) @("$($r.error)")

        $target = Join-Path $Out 'P01_cat_b.ssm'
        if (Test-Path $target) {
            $before = (Get-Item $target).LastWriteTimeUtc
            $r = Invoke-McpTool $session 'sistema_create_project' @{ spec = "$scenarioDir\P01_cat_b.json"; outputPath = $target }
            $ok = (-not $r.created) -and ((Get-Item $target).LastWriteTimeUtc -eq $before)
            Add-Result 'X03_no_overwrite' 'Bestehende Datei wird ohne overwrite nicht ueberschrieben' $ok @("$($r.reason)")
        }

        $target = Join-Path $Out 'X04_should_not_exist.ssm'
        $r = Invoke-McpTool $session 'sistema_create_project' @{ spec = "$scenarioDir\N01_incomplete.json"; outputPath = $target }
        Add-Result 'X04_incomplete_no_file' 'Unvollstaendige Spezifikation erzeugt keine Datei' ((-not $r.created) -and -not (Test-Path $target)) @("$($r.reason)")

        $sfs = foreach ($i in 1..30) {
            @{ id = "SF-$i"; name = "Sicherheitsfunktion $i"; plr = 'd'; trigger = "Ausloeser $i"; reaction = "Reaktion $i"; safeState = "Zustand $i"
               subsystems = @(
                 @{ name = "Eingang $i"; category = '3'; categoryRequirementsMet = $true; ccfMeasures = @('separation','diversity','overvoltage','emc','environment')
                    channels = @{ ch1 = @(@{ name = "S$i.1"; technology = 'electromechanical'; b10d = 20000000; nop = 10000; dcMeasure = 'in-plausibility' })
                                  ch2 = @(@{ name = "S$i.2"; technology = 'electromechanical'; b10d = 1000000; nop = 10000; dcMeasure = 'in-plausibility' }) } },
                 @{ name = "Logik $i"; direct = @{ pl = 'e'; pfh = 1e-9; category = '4' } },
                 @{ name = "Ausgang $i"; category = '3'; categoryRequirementsMet = $true; ccfMeasures = @('separation','overvoltage','emc','environment','well-tried','fmea')
                    channels = @{ ch1 = @(@{ name = "Q$i.1"; technology = 'electromechanical'; b10d = 1300000; nop = 10000; dcMeasure = 'out-direct' })
                                  ch2 = @(@{ name = "Q$i.2"; technology = 'electromechanical'; b10d = 1300000; nop = 10000; dcMeasure = 'out-direct' }) } }) }
        }
        $big = @{ project = @{ name = 'X05 Grossprojekt 30 SF' }; safetyFunctions = @($sfs) } | ConvertTo-Json -Depth 20 -Compress
        $target = Join-Path $Out 'X05_large_project.ssm'
        $sw = [Diagnostics.Stopwatch]::StartNew()
        $r = Invoke-McpTool $session 'sistema_create_project' @{ spec = $big; outputPath = $target; overwrite = $true }
        $ms = $sw.ElapsedMilliseconds
        $read = if ($r.created) { Invoke-McpTool $session 'sistema_read_project' @{ path = $target } } else { $null }
        Add-Result 'X05_large_project' 'Grossprojekt 30 SF / 90 Subsysteme / 120 Bauteile' ($r.created -and @($read.safetyFunctions).Count -eq 30) @("$ms ms", "$((Get-Item $target -ErrorAction SilentlyContinue).Length) Bytes")

        $m = Invoke-McpTool $session 'library_list_manufacturers' @{}
        Add-Result 'X06_manufacturers' 'Herstellerliste vollstaendig' (@($m.manufacturers).Count -ge 38) @("$(@($m.manufacturers).Count) Hersteller, direkt: $(@($m.manufacturers | Where-Object direct).Count)")
        $s = Invoke-McpTool $session 'library_search' @{ query = '6814053' }
        Add-Result 'X07_search_partno' 'Suche nach Bestellnummer' ($s.total -ge 1) @("$($s.total) Treffer")
        $g = Invoke-McpTool $session 'library_get_component' @{ id = 'turck/TBPN-L1-FDIO1-2IOL' }
        Add-Result 'X08_get_component' 'Bauteildetails mit Anwendungsfaellen' (@($g.useCases).Count -eq 5) @("$(@($g.useCases).Count) Anwendungsfaelle")
        $d = Invoke-McpTool $session 'library_download' @{ manufacturerOrUrl = 'siemens' }
        Add-Result 'X09_portal_download' 'Portal-Hersteller -> Hinweis auf manuellen Download' ($d.status -in 'manual-download-required', 'failed') @("$($d.status): $($d.message)")
        $d = Invoke-McpTool $session 'library_download' @{ manufacturerOrUrl = 'http://example.com/x.zip' }
        Add-Result 'X10_no_http' 'Unverschluesseltes http wird abgelehnt' ($d.status -eq 'failed') @("$($d.message)")
        $mz = Invoke-McpTool $session 'sistema_list_measures' @{ language = 'de' }
        Add-Result 'X11_measures' 'CCF-Summe 100 Punkte, DC-Liste vorhanden' ((($mz.ccfMeasures | Measure-Object points -Sum).Sum -eq 100) -and @($mz.dcMeasures).Count -ge 25) @("$(@($mz.dcMeasures).Count) DC-Massnahmen")
    }
}
finally { Stop-McpSession $session }

$okCount = @($results | Where-Object ok).Count
$md = @("# Testlauf $(Get-Date -Format 'dd.MM.yyyy HH:mm')", "", "Server: $Exe", "", "**$okCount von $($results.Count) bestanden**", "",
        "| Test | Beschreibung | Ergebnis | Details |", "|---|---|---|---|")
$md += $results | ForEach-Object { "| $($_.id) | $($_.title) | $(if ($_.ok) { 'OK' } else { '**FEHLER**' }) | $($_.details -replace '\|', '/') |" }
$md += "", "## Naechster Schritt", "", "Die erzeugten .ssm-Dateien in SISTEMA oeffnen, Meldungen pruefen, mit *Datei > Speichern unter* speichern,", "danach scripts\compare-sistema.ps1 -Folder `"$Out`" ausfuehren."
$md | Set-Content (Join-Path $Out 'protokoll.md') -Encoding UTF8
$results | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $Out 'results.json') -Encoding UTF8
Write-Output ""
Write-Output "$okCount von $($results.Count) bestanden. Protokoll: $Out\protokoll.md"
