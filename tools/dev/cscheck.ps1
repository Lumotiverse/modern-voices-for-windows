param([string]$File)
$dir = Split-Path ([System.Management.Automation.PSObject].Assembly.Location)
Add-Type -Path (Join-Path $dir 'Microsoft.CodeAnalysis.dll'), (Join-Path $dir 'Microsoft.CodeAnalysis.CSharp.dll') -ErrorAction SilentlyContinue
$opts = [Microsoft.CodeAnalysis.CSharp.CSharpParseOptions]::new([Microsoft.CodeAnalysis.CSharp.LanguageVersion]::CSharp5)
$tree = [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText((Get-Content $File -Raw), $opts)
$d = @($tree.GetDiagnostics())
if ($d.Count) { $d | ForEach-Object { $_.ToString() } } else { 'syntax ok (C# 5)' }
