param(
    [Parameter(Mandatory, Position = 0)]
    [string]$File
)

if (-not $File.EndsWith('.cs', [StringComparison]::OrdinalIgnoreCase)) {
    $File += '.cs'
}

$sourcePath = Join-Path (Join-Path $PSScriptRoot 'Source\Dlls') $File
$outputPath = Join-Path $PSScriptRoot 'Dlls'

if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
    Write-Error "DLL source file not found: $sourcePath"
    exit 1
}

dotnet build $sourcePath -p:OutputType=Library -p:PublishAot=false --output $outputPath
exit $LASTEXITCODE
