$toolsDir = 'C:\Users\Ak417ytrfg\.nuget\packages\microsoft.windowsappsdk\1.5.240802000\tools\net472'
[System.AppDomain]::CurrentDomain.add_AssemblyResolve([System.ResolveEventHandler]{
    param($s, $e)
    $name = ($e.Name -split ',')[0]
    $path = Join-Path $toolsDir ($name + '.dll')
    if (Test-Path $path) {
        return [System.Reflection.Assembly]::LoadFrom($path)
    }
    return $null
})

$exePath = Join-Path $toolsDir 'XamlCompiler.exe'
$asm = [System.Reflection.Assembly]::LoadFrom($exePath)
$cxType = $asm.GetType('Microsoft.UI.Xaml.Markup.Compiler.Executable.CompileXaml')
$cx = [Activator]::CreateInstance($cxType)
$argsArray = [string[]]@('F:\TeleClean\src\TeleClean.App\obj\x64\Debug\net8.0-windows10.0.19041.0\win-x64\input.json', 'F:\TeleClean\src\TeleClean.App\obj\x64\Debug\net8.0-windows10.0.19041.0\win-x64\output.json')

$ret = $cxType.GetMethod('Run').Invoke($cx, @(,$argsArray))
Write-Host ("Return: " + $ret)

$log = $cxType.GetMethod('get_Log', [System.Reflection.BindingFlags]'Public,NonPublic,Instance').Invoke($cx, $null)
$entries = $log.GetType().GetProperty('Entries').GetValue($log)
Write-Host ("Entries count: " + $entries.Count)
foreach ($item in $entries) {
    Write-Host ($item.Message + " | File: " + $item.File + " | Line: " + $item.LineNumber)
}
