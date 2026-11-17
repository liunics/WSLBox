param([string]$OutputDirectory = 'build', [switch]$IncludeTests)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if ($OutputDirectory -notmatch '\A[A-Za-z0-9_-][A-Za-z0-9_.-]*\z') { throw 'Use a single project-local build directory name.' }
$src = Join-Path $root 'src'
$build = Join-Path $root $OutputDirectory
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path $compiler)) { throw 'Windows .NET Framework compiler was not found.' }
New-Item -ItemType Directory -Force -Path $build | Out-Null
$output = Join-Path $build 'WSLBox.exe'
$metadata = Join-Path (Join-Path $root '.local/build-artifacts') $OutputDirectory
$cache = Join-Path $root '.local/icon-cache'
New-Item -ItemType Directory -Force -Path $metadata,$cache | Out-Null
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('WSLBox-build-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $inputMethods = [IO.File]::ReadAllText((Join-Path $src 'input_methods.py'), [Text.Encoding]::UTF8)
    $helper = [IO.File]::ReadAllText((Join-Path $src 'thumbnails.py'), [Text.Encoding]::UTF8)
    $backend = [IO.File]::ReadAllText((Join-Path $src 'backend.py'), [Text.Encoding]::UTF8)
    $runtime = Join-Path $temporary 'backend.py'
    $locale = Join-Path $src 'locales\strings.json'
    $catalog = [IO.File]::ReadAllText($locale, [Text.Encoding]::UTF8) | ConvertFrom-Json
    $backendCatalog = @{}
    foreach ($property in $catalog.PSObject.Properties) { if ($property.Name.StartsWith('backend.')) { $backendCatalog[$property.Name] = $property.Value } }
    $localeData = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes(($backendCatalog | ConvertTo-Json -Depth 5 -Compress)))
    $preamble = "import json,base64`nWSLBOX_TRANSLATIONS=json.loads(base64.b64decode('" + $localeData + "'))`n"
    [IO.File]::WriteAllText($runtime, $preamble + $inputMethods + "`n" + $helper + "`n" + $backend, (New-Object Text.UTF8Encoding($false)))
    $sources = @('Core.cs', 'IconArtwork.cs', 'L10n.cs', 'OpenWorker.cs', 'Shell.cs', 'ShellContextMenu.cs', 'Program.cs', 'BoxWindow.cs', 'WindowGeometry.cs', 'FileOrdering.cs', 'FileActions.cs', 'InlineRename.cs', 'OpenWithDialog.cs', 'Dialogs.cs', 'WslSetup.cs', 'DesktopToggle.cs', 'Theme.cs', 'Branding.cs', 'SnapPreview.cs', 'UpdateService.cs', 'WindowEffects.cs', 'StartupRegistration.cs') | ForEach-Object { Join-Path $src $_ }
    $defines = @()
    if ($IncludeTests) { $sources += Join-Path $root 'tests\SelfTest.cs'; $defines = @('/define:LOCAL_TESTS') }
    $framework = Split-Path $compiler -Parent
    $wpf = Join-Path $framework 'WPF'
    $references = @('PresentationCore.dll', 'PresentationFramework.dll', 'WindowsBase.dll') | ForEach-Object { '/reference:' + (Join-Path $wpf $_) }
    $versionSource = [IO.File]::ReadAllText((Join-Path $src 'Branding.cs'))
    $brandingArtwork = $versionSource -replace '(?m)^\s*public const string (AssemblyVersion|Version) = "[^"]*";', ''
    $iconInputs = @('IconArtwork.cs', 'IconBuilder.cs') | ForEach-Object {
        (Get-FileHash -LiteralPath (Join-Path $src $_) -Algorithm SHA256).Hash
    }
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $iconKey = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes(
            ($iconInputs -join '|') + '|' + $brandingArtwork))).Replace('-', '')
    } finally { $sha.Dispose() }
    $cachedIcon = Join-Path $cache ($iconKey + '.ico')
    $icon = Join-Path $temporary 'WSLBox.ico'
    if (Test-Path -LiteralPath $cachedIcon -PathType Leaf) {
        Copy-Item -LiteralPath $cachedIcon -Destination $icon
        Write-Host 'Reusing cached application icon.'
    } else {
        $builder = Join-Path $temporary 'IconBuilder.exe'
        & $compiler /nologo /target:exe /platform:anycpu /codepage:65001 /utf8output "/out:$builder" /reference:System.dll /reference:System.Drawing.dll /reference:System.Xaml.dll /reference:System.Runtime.Serialization.dll "/resource:$locale,WSLBox.Strings" @references (Join-Path $src 'L10n.cs') (Join-Path $src 'IconArtwork.cs') (Join-Path $src 'Branding.cs') (Join-Path $src 'IconBuilder.cs')
        if ($LASTEXITCODE -ne 0) { throw "Icon builder failed: $LASTEXITCODE" }
        & $builder $icon
        if ($LASTEXITCODE -ne 0) { throw "Icon rendering failed: $LASTEXITCODE" }
        $candidate = Join-Path $cache ([Guid]::NewGuid().ToString('N') + '.tmp')
        try {
            Copy-Item -LiteralPath $icon -Destination $candidate
            try { [IO.File]::Move($candidate, $cachedIcon) }
            catch [IO.IOException] { if (-not (Test-Path -LiteralPath $cachedIcon -PathType Leaf)) { throw } }
        } finally {
            if (Test-Path -LiteralPath $candidate) { Remove-Item -LiteralPath $candidate -Force }
        }
    }
    & $compiler /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 /utf8output /warn:4 "/out:$output" "/win32icon:$icon" "/resource:$icon,WSLBox.AppIcon" "/resource:$runtime,WSLBox.Backend" "/resource:$locale,WSLBox.Strings" "/win32manifest:$(Join-Path $src 'app.manifest')" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:Accessibility.dll /reference:System.Runtime.Serialization.dll /reference:System.Xaml.dll @references @defines @sources
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $LASTEXITCODE" }
    $version = [regex]::Match($versionSource, 'const string Version = "([^"]+)"').Groups[1].Value
    $inputs = @{}
    $buildInputs = @(Get-ChildItem -Path $src -Filter '*.cs' -File) + @('src/backend.py','src/input_methods.py','src/thumbnails.py','src/locales/strings.json','src/app.manifest','.github/build.ps1' | ForEach-Object { Get-Item -LiteralPath (Join-Path $root $_) })
    if ($IncludeTests) { $buildInputs += Get-Item -LiteralPath (Join-Path $root 'tests/SelfTest.cs') }
    foreach ($file in $buildInputs) { $inputs[$file.FullName.Substring($root.Length + 1).Replace('\','/')] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    $buildInfo = @{ version = $version; exe_sha256 = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash.ToLowerInvariant(); backend_sha256 = (Get-FileHash -LiteralPath $runtime -Algorithm SHA256).Hash.ToLowerInvariant(); inputs = $inputs }
    Copy-Item -LiteralPath $runtime -Destination (Join-Path $metadata 'backend.py') -Force
    [IO.File]::WriteAllText((Join-Path $metadata 'build-info.json'), ($buildInfo | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding($false)))
    Write-Host "Built standalone: $output"
} finally {
    Remove-Item -LiteralPath $temporary -Recurse -Force
}
