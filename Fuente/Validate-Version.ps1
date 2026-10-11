param(
    [ValidateSet('Validate', 'Record')][string]$Mode = 'Validate',
    [Parameter(Mandatory=$true)][string]$ProjectDirectory,
    [Parameter(Mandatory=$true)][string]$Version
)
$ErrorActionPreference = 'Stop'
try {
    if ($Version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw 'La version debe tener tres o cuatro numeros, por ejemplo 1.0.11.' }
    $current = [version]$Version
    $current = [version]::new($current.Major, $current.Minor, $current.Build, [Math]::Max(0, $current.Revision))
    $root = [IO.Path]::GetFullPath($ProjectDirectory).TrimEnd('\', '/')
    $stamp = Join-Path $root 'version-publicada.json'
    $lines = @(Get-ChildItem -LiteralPath $root -Recurse -File | Where-Object {
        $_.FullName -notmatch '\\(bin|obj)\\' -and $_.Extension -in @('.cs','.csproj','.ps1','.ico','.exe','.json') -and $_.FullName -ne $stamp
    } | Sort-Object FullName | ForEach-Object {
        $fileSha = [Security.Cryptography.SHA256]::Create()
        $stream = [IO.File]::OpenRead($_.FullName)
        try { $hash = [BitConverter]::ToString($fileSha.ComputeHash($stream)).Replace('-','') }
        finally { $stream.Dispose(); $fileSha.Dispose() }
        $_.FullName.Substring($root.Length + 1).Replace('\','/') + ':' + $hash
    })
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $fingerprint = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes(($lines -join "`n")))).Replace('-','') }
    finally { $sha.Dispose() }
    if (Test-Path -LiteralPath $stamp) {
        $prior = Get-Content -LiteralPath $stamp -Raw | ConvertFrom-Json
        $previous = [version]$prior.version
        if ($current -lt $previous) { throw 'No se permite una version menor que la ultima publicada.' }
        if ($current -eq $previous -and $fingerprint -ne $prior.fingerprint) {
            throw 'El codigo cambio desde la ultima publicacion. Incrementa <Version> en NitroPulse.csproj antes de compilar.'
        }
    }
    if ($Mode -eq 'Record') {
        @{version=$current.ToString(); fingerprint=$fingerprint} | ConvertTo-Json | Set-Content -LiteralPath ($stamp + '.tmp') -Encoding UTF8
        Move-Item -LiteralPath ($stamp + '.tmp') -Destination $stamp -Force
    }
    Write-Output "Version $Version validada ($Mode)."
} catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }
