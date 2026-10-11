param([Parameter(Mandatory=$true)][string]$Config)
$ErrorActionPreference = 'Stop'
$replaced = $false
try {
    $settings = Get-Content -LiteralPath $Config -Raw | ConvertFrom-Json
    $sourcePath = [IO.Path]::GetFullPath($settings.source)
    $targetPath = [IO.Path]::GetFullPath($settings.target)
    $backupPath = [IO.Path]::GetFullPath($settings.backup)
    if ([IO.Path]::GetFileName($targetPath) -ne 'NitroPulse.exe' -or $sourcePath -eq $targetPath -or
        [IO.Path]::GetDirectoryName($backupPath) -ne [IO.Path]::GetDirectoryName($targetPath)) { throw 'Rutas de instalacion invalidas.' }
    $process = Get-Process -Id $settings.pid -ErrorAction SilentlyContinue
    if ($process -and !$process.WaitForExit(120000)) { throw 'NitroPulse no se cerro; no se reemplazo el ejecutable.' }
    $sha = [Security.Cryptography.SHA256]::Create()
    $stream = [IO.File]::OpenRead($sourcePath)
    try { $hash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','') }
    finally { $stream.Dispose(); $sha.Dispose() }
    if ($hash -ne $settings.hash) { throw 'El archivo descargado cambio; instalacion cancelada.' }
    $incoming = $targetPath + '.incoming-' + [Guid]::NewGuid().ToString('N')
    [IO.File]::Copy($sourcePath, $incoming, $false)
    [IO.File]::Replace($incoming, $targetPath, $backupPath)
    $replaced = $true
    Start-Process -FilePath $targetPath -WorkingDirectory ([IO.Path]::GetDirectoryName($targetPath)) -WindowStyle Hidden
    'Actualizacion instalada; copia anterior: ' + $backupPath | Set-Content -LiteralPath ($Config + '.resultado.txt')
} catch {
    $message = $_.Exception.Message
    if ($replaced) {
        try { [IO.File]::Copy($backupPath, $targetPath, $true) } catch { $message += ' No se pudo restaurar la copia anterior: ' + $backupPath }
    }
    $message | Set-Content -LiteralPath ($Config + '.resultado.txt')
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.MessageBox]::Show($message, 'NitroPulse - Actualizacion no instalada') | Out-Null
    exit 1
}
