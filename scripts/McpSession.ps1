# Minimal MCP stdio client for test scripts (one server process, many tool calls).

function Start-McpSession([string]$Exe) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = (Resolve-Path $Exe).Path
    $psi.UseShellExecute = $false
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.StandardOutputEncoding = [Text.Encoding]::UTF8
    $p = [Diagnostics.Process]::Start($psi)
    $p.BeginErrorReadLine()   # drain stderr (logs) so the server never blocks
    $w = New-Object IO.StreamWriter($p.StandardInput.BaseStream, (New-Object Text.UTF8Encoding($false)))
    $w.AutoFlush = $true
    $s = [pscustomobject]@{ Process = $p; Writer = $w; NextId = 1 }
    [void](Send-McpRequest $s 'initialize' @{ protocolVersion = '2025-06-18'; capabilities = @{}; clientInfo = @{ name = 'scenario-test'; version = '1' } })
    $w.WriteLine('{"jsonrpc":"2.0","method":"notifications/initialized"}')
    $s
}

function Send-McpRequest($s, [string]$method, $params) {
    $id = $s.NextId; $s.NextId++
    $msg = @{ jsonrpc = '2.0'; id = $id; method = $method; params = $params } | ConvertTo-Json -Depth 40 -Compress
    $s.Writer.WriteLine($msg)
    while ($true) {
        $line = $s.Process.StandardOutput.ReadLine()
        if ($null -eq $line) { throw "Server terminated." }
        $resp = $line | ConvertFrom-Json
        if ($resp.id -eq $id) { return $resp }
    }
}

# Calls a tool and returns the parsed JSON result (or an object with .text if the tool returned plain text).
function Invoke-McpTool($s, [string]$name, [hashtable]$arguments) {
    $resp = Send-McpRequest $s 'tools/call' @{ name = $name; arguments = $arguments }
    if ($resp.error) { throw "MCP error in ${name}: $($resp.error.message)" }
    $text = ($resp.result.content | ForEach-Object { $_.text }) -join "`n"
    if ($resp.result.isError) { return [pscustomobject]@{ error = $text } }
    try { return $text | ConvertFrom-Json } catch { return [pscustomobject]@{ text = $text } }
}

function Stop-McpSession($s) {
    try { $s.Writer.Close() } catch { }
    if (-not $s.Process.WaitForExit(5000)) { $s.Process.Kill() }
}
