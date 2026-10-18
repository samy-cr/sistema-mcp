# After the generated files were opened and saved in SISTEMA: compares SISTEMA's results with the
# expectations of the scenarios and with the server's estimate (which must never be better than SISTEMA).
# Usage: .\compare-sistema.ps1 -Folder ..\testlauf\<run>
param(
    [Parameter(Mandatory)] [string]$Folder,
    [string]$Exe = "$PSScriptRoot\..\publish\SistemaMcp\SistemaMcp.exe"
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\McpSession.ps1"
$Folder = (Resolve-Path $Folder).Path
$scenarioDir = (Resolve-Path "$PSScriptRoot\..\tests\scenarios").Path
$order = 'a', 'b', 'c', 'd', 'e'
$rows = New-Object System.Collections.ArrayList
$session = Start-McpSession $Exe
try {
    foreach ($ssm in Get-ChildItem $Folder -Filter *.ssm | Sort-Object Name) {
        $id = $ssm.BaseName
        $read = Invoke-McpTool $session 'sistema_read_project' @{ path = $ssm.FullName }
        $scenario = Join-Path $scenarioDir "$id.json"
        $expect = if (Test-Path $scenario) { (Get-Content $scenario -Raw -Encoding UTF8 | ConvertFrom-Json)._test.expect } else { $null }
        $reportFile = Join-Path $Folder "$id.report.json"
        $report = if (Test-Path $reportFile) { Get-Content $reportFile -Raw -Encoding UTF8 | ConvertFrom-Json } else { $null }
        if (-not $read.resultsStored) {
            [void]$rows.Add([pscustomobject]@{ file = $id; sf = '-'; plr = '-'; sistema = 'nicht gespeichert'; pfh = ''; estimate = ''; result = 'OFFEN'; note = 'In SISTEMA öffnen und mit "Speichern unter" speichern' })
            continue
        }
        foreach ($sf in $read.safetyFunctions) {
            $key = if ($sf.id) { $sf.id } else { $sf.name }
            $est = ($report.estimate | Where-Object { $_.id -eq $sf.id }).pl
            $notes = @(); $ok = $true
            if ($expect.sistemaPlMin.$key) {
                if ($order.IndexOf("$($sf.pl)") -lt $order.IndexOf($expect.sistemaPlMin.$key)) { $ok = $false; $notes += "erwartet mind. PL $($expect.sistemaPlMin.$key)" }
            }
            if ($est -and $sf.pl -and $order.IndexOf($est) -gt $order.IndexOf("$($sf.pl)")) { $ok = $false; $notes += "Schätzung besser als SISTEMA (nicht konservativ)" }
            if ($est -and $sf.pl -and $order.IndexOf($est) -lt $order.IndexOf("$($sf.pl)")) { $notes += "Schätzung konservativer" }
            if ($null -ne $expect.plrReached.$key -and [bool]$sf.plrReached -ne [bool]$expect.plrReached.$key) { $ok = $false; $notes += "PLr erreicht = $($sf.plrReached)" }
            [void]$rows.Add([pscustomobject]@{
                file = $id; sf = $key; plr = $sf.plr; sistema = $sf.pl; pfh = if ($sf.pfh) { '{0:0.00E+0}' -f $sf.pfh } else { '' }
                estimate = $est; result = if ($ok) { 'OK' } else { 'FEHLER' }; note = $notes -join ', ' })
        }
    }
}
finally { Stop-McpSession $session }

$rows | Format-Table -AutoSize | Out-String -Width 220
$md = @("", "## Vergleich mit SISTEMA ($(Get-Date -Format 'dd.MM.yyyy HH:mm'))", "",
        "| Datei | SF | PLr | PL SISTEMA | PFH SISTEMA | Schätzung | Ergebnis | Hinweis |", "|---|---|---|---|---|---|---|---|")
$md += $rows | ForEach-Object { "| $($_.file) | $($_.sf) | $($_.plr) | $($_.sistema) | $($_.pfh) | $($_.estimate) | $($_.result) | $($_.note) |" }
Add-Content (Join-Path $Folder 'protokoll.md') $md -Encoding UTF8
"Ergänzt in $Folder\protokoll.md"
