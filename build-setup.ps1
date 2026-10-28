# Собирает установщик release\NowPlaying-Setup.exe: свежий NowPlaying.exe + значок + расширение
# упаковываются ресурсами внутрь одного exe. Запуск из папки NowPlaying: & .\build-setup.ps1
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$wm = 'C:\Windows\System32\WinMetadata'
$stage = Join-Path $root 'installer\staging'
$release = Join-Path $root 'release'
New-Item -ItemType Directory -Force $stage, $release | Out-Null

# 1. сама программа
$facades = Get-ChildItem "$env:WINDIR\Microsoft.NET\assembly\GAC_MSIL" -Recurse -Include System.Runtime.dll, System.Threading.Tasks.dll, System.Runtime.InteropServices.WindowsRuntime.dll, System.IO.dll | ForEach-Object { "/r:$($_.FullName)" }
& "$fw\csc.exe" /nologo /codepage:65001 /target:winexe /optimize+ "/out:$stage\NowPlaying.exe" "/win32icon:$root\nowplaying.ico" "/lib:$fw\WPF" `
    /r:PresentationFramework.dll /r:PresentationCore.dll /r:WindowsBase.dll /r:System.Xaml.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:System.Web.Extensions.dll `
    "/r:$wm\Windows.Media.winmd" "/r:$wm\Windows.Foundation.winmd" "/r:$wm\Windows.Storage.winmd" $facades `
    "$root\NowPlaying.cs" "$root\MixerParts.cs" "$root\SoundPanel.cs" "$root\Listens.cs" "$root\StatsPage.cs" "$root\Lang.cs"
if ($LASTEXITCODE -ne 0) { throw 'Не собрался NowPlaying.exe' }

# 2. установщик с файлами внутри
$res = @("/resource:$stage\NowPlaying.exe,payload/NowPlaying.exe", "/resource:$root\nowplaying.ico,payload/nowplaying.ico")
foreach ($f in Get-ChildItem "$root\extension" -File -Recurse) {   # с подпапками (_locales\en, _locales\ru)
    $rel = $f.FullName.Substring("$root\extension\".Length).Replace('\', '/')
    $res += "/resource:$($f.FullName),payload/soundcloud-extension/$rel"
}
& "$fw\csc.exe" /nologo /codepage:65001 /target:winexe /optimize+ "/out:$release\NowPlaying-Setup.exe" "/win32icon:$root\nowplaying.ico" "/lib:$fw\WPF" `
    /r:PresentationFramework.dll /r:PresentationCore.dll /r:WindowsBase.dll /r:System.Xaml.dll /r:System.Core.dll $res "$root\installer\Setup.cs"
if ($LASTEXITCODE -ne 0) { throw 'Не собрался установщик' }
Get-Item "$release\NowPlaying-Setup.exe" | Select-Object Name, Length
