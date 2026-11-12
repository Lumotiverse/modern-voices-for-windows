# Per scope: in each function (its params and body, nested script blocks included) and at script level
# (outside functions), variable names that differ only by case. Also: functions that assign a local
# whose name matches a script-level variable that the same function also reads (shadowing hazard).
param([string[]]$Files)
foreach ($file in $Files) {
    $tokens = $null; $errors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($file, [ref]$tokens, [ref]$errors)
    $functions = $ast.FindAll({ $args[0] -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true)
    $report = @()
    foreach ($fn in $functions) {
        $vars = $fn.FindAll({ $args[0] -is [System.Management.Automation.Language.VariableExpressionAst] }, $true) |
            ForEach-Object { $_.VariablePath.UserPath } | Where-Object { $_ -notmatch '^(script|global|env|using):' } | Sort-Object -Unique -CaseSensitive
        $groups = $vars | Group-Object { $_.ToLower() } | Where-Object { @($_.Group | Sort-Object -Unique -CaseSensitive).Count -gt 1 }
        foreach ($g in $groups) { $report += "  function $($fn.Name): $(@($g.Group | Sort-Object -Unique -CaseSensitive) -join ' / ')" }
    }
    # script level: variables not inside any function
    $top = $ast.FindAll({ $args[0] -is [System.Management.Automation.Language.VariableExpressionAst] }, $true) | Where-Object {
        $p = $_.Parent; $inFn = $false
        while ($p) { if ($p -is [System.Management.Automation.Language.FunctionDefinitionAst]) { $inFn = $true; break }; $p = $p.Parent }
        -not $inFn } | ForEach-Object { $_.VariablePath.UserPath } | Sort-Object -Unique -CaseSensitive
    $groups = $top | Group-Object { $_.ToLower() } | Where-Object { @($_.Group | Sort-Object -Unique -CaseSensitive).Count -gt 1 }
    foreach ($g in $groups) { $report += "  script level: $(@($g.Group | Sort-Object -Unique -CaseSensitive) -join ' / ')" }
    "$([IO.Path]::GetFileName($file)): " + $(if ($report) { "`n" + ($report -join "`n") } else { 'no clashes' })
}
