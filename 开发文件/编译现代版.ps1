param([string]$OutputName = '归位整理助手.exe')
$workspacePath = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $workspacePath
try {
    & 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:winexe /main:ModernProgram "/out:$OutputName" /resource:开发文件\Modern.xaml,Modern.xaml /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Xml.Linq.dll /reference:System.Xaml.dll /reference:C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationFramework.dll /reference:C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationCore.dll /reference:C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\WindowsBase.dll '开发文件\Organizer.cs' '开发文件\Modern.cs' '开发文件\Recycle.cs' '开发文件\OpenActions.cs' '开发文件\Rename.cs' '开发文件\CategoryTree.cs'
    if ($LASTEXITCODE -ne 0) { throw '编译失败' }
} finally { Pop-Location }
