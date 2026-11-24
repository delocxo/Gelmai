[CmdletBinding(DefaultParameterSetName = 'Single')]
param(
    [Parameter(Mandatory, Position = 0, ParameterSetName = 'Single')]
    [string]$File,

    [Parameter(Mandatory, ParameterSetName = 'All')]
    [switch]$All
)

$sourceDirectory = Join-Path $PSScriptRoot 'Source\Dlls'
$outputPath = Join-Path $PSScriptRoot 'nlib'

if ($PSCmdlet.ParameterSetName -eq 'All') {
    $sourcePaths = @(Get-ChildItem -LiteralPath $sourceDirectory -Filter '*.cs' -File |
        Sort-Object Name | Select-Object -ExpandProperty FullName)
    if ($sourcePaths.Count -eq 0) {
        Write-Error "No DLL source files found in $sourceDirectory"
        exit 1
    }
}
else {
    if (-not $File.EndsWith('.cs', [StringComparison]::OrdinalIgnoreCase)) {
        $File += '.cs'
    }

    $sourcePaths = @(Join-Path $sourceDirectory $File)
    if (-not (Test-Path -LiteralPath $sourcePaths[0] -PathType Leaf)) {
        Write-Error "DLL source file not found: $($sourcePaths[0])"
        exit 1
    }
}

$buildExitCode = 0
foreach ($sourcePath in $sourcePaths) {
    dotnet build $sourcePath -p:OutputType=Library -p:PublishAot=false --output $outputPath
    if ($LASTEXITCODE -ne 0) {
        $buildExitCode = $LASTEXITCODE
    }
}

exit $buildExitCode
