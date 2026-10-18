# Smoke test of the server over stdio: initialize, tools/list and optionally one tool call.
# Usage: .\mcp_test.ps1 [-Tool sistema_read_project] [-ArgsJson '{"path":"..."}' | -ArgsFile args.json]
param(
    [string]$Exe = "$PSScriptRoot\..\src\SistemaMcp\bin\Release\net8.0\win-x86\SistemaMcp.exe",
    [string]$Tool,
    [string]$ArgsJson = '{}',
    [string]$ArgsFile
)

if ($ArgsFile) { $ArgsJson = [IO.File]::ReadAllText((Resolve-Path $ArgsFile).Path) -replace "\r?\n", " " }

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = (Resolve-Path $Exe).Path
$psi.UseShellExecute = $false
$psi.RedirectStandardInput = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.StandardOutputEncoding = [Text.Encoding]::UTF8
$p = [Diagnostics.Process]::Start($psi)
$stdin = New-Object IO.StreamWriter($p.StandardInput.BaseStream, (New-Object Text.UTF8Encoding($false)))
$stdin.AutoFlush = $true

function Send($json) { $stdin.WriteLine($json) }
function Receive { $p.StandardOutput.ReadLine() }

Send '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}'
$init = Receive | ConvertFrom-Json
"Server: $($init.result.serverInfo.name) $($init.result.serverInfo.version)"
Send '{"jsonrpc":"2.0","method":"notifications/initialized"}'

Send '{"jsonrpc":"2.0","id":2,"method":"tools/list"}'
$list = Receive | ConvertFrom-Json
"Tools ($($list.result.tools.Count)): " + (($list.result.tools | ForEach-Object { $_.name }) -join ', ')

if ($Tool) {
    Send ('{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"' + $Tool + '","arguments":' + $ArgsJson + '}}')
    $res = Receive | ConvertFrom-Json
    if ($res.error) { "ERROR: $($res.error.message)" }
    else {
        if ($res.result.isError) { "TOOL ERROR:" }
        $res.result.content | ForEach-Object { $_.text }
    }
}

$stdin.Close()
if (-not $p.WaitForExit(5000)) { $p.Kill() }
