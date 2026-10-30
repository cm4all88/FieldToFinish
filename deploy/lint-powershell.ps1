<#
.SYNOPSIS
    Checks PowerShell scripts parse, and flags syntax Windows PowerShell 5.1 (the powershell.exe that
    runs UPDATEFTF on the office machines) cannot run: ternaries, ?? and ?., && / || chains,
    Join-Path with three parts, -AsHashtable, utf8NoBOM, -Parallel, and a bare + between command
    arguments (Info "a" + $b passes only "a").

.EXAMPLE
    pwsh -File deploy/lint-powershell.ps1 -Paths (Get-ChildItem -Recurse -Filter *.ps1).FullName
#>
param([string[]]$Paths)
$bad = 0
foreach ($path in $Paths) {
    $tokens = $null; $errors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$errors)
    foreach ($e in $errors) { $bad++; Write-Host ("{0}:{1}: PARSE {2}" -f $path, $e.Extent.StartLineNumber, $e.Message) }
    $nodes = $ast.FindAll({ param($n) $true }, $true)
    foreach ($n in $nodes) {
        $why = $null
        switch ($n.GetType().Name) {
            'TernaryExpressionAst' { $why = 'ternary ?: (PS7)' }
            'PipelineChainAst' { $why = '&& / || pipeline chain (PS7)' }
            'BinaryExpressionAst' { if ($n.Operator -eq 'QuestionQuestion') { $why = '?? (PS7)' } }
            'AssignmentStatementAst' { if ($n.Operator -eq 'QuestionQuestionEquals') { $why = '??= (PS7)' } }
            'MemberExpressionAst' { if ($n.NullConditional) { $why = '?. (PS7)' } }
            'InvokeMemberExpressionAst' { if ($n.NullConditional) { $why = '?. (PS7)' } }
            'IndexExpressionAst' { if ($n.NullConditional) { $why = '?[] (PS7)' } }
            'CommandAst' {
                $name = $n.GetCommandName()
                if ($name -eq 'Join-Path') {
                    $positional = @($n.CommandElements | Select-Object -Skip 1 | Where-Object { $_ -isnot [System.Management.Automation.Language.CommandParameterAst] })
                    $named = @($n.CommandElements | Where-Object { $_ -is [System.Management.Automation.Language.CommandParameterAst] })
                    if ($positional.Count -gt 2 -and $named.Count -eq 0) { $why = 'Join-Path with 3+ parts (PS6+)' }
                }
                if ($name -eq 'ForEach-Object' -and ($n.CommandElements | Where-Object { $_ -is [System.Management.Automation.Language.CommandParameterAst] -and $_.ParameterName -like 'Parallel' })) { $why = 'ForEach-Object -Parallel (PS7)' }
                if ($name -eq 'ConvertFrom-Json' -and ($n.CommandElements | Where-Object { $_ -is [System.Management.Automation.Language.CommandParameterAst] -and $_.ParameterName -like 'AsHashtable' })) { $why = 'ConvertFrom-Json -AsHashtable (PS6+)' }
                foreach ($el in ($n.CommandElements | Select-Object -Skip 1)) { if ($el -is [System.Management.Automation.Language.StringConstantExpressionAst] -and $el.StringConstantType -eq 'BareWord' -and $el.Value -eq '+') { $why = "a bare + in a command's arguments (string concatenation needs parentheses)" } }
                foreach ($el in $n.CommandElements) { if ($el -is [System.Management.Automation.Language.StringConstantExpressionAst] -and $el.Value -match '^(utf8NoBOM|utf8BOM)$') { $why = "encoding $($el.Value) (PS6+)" } }
            }
        }
        if ($why) { $bad++; Write-Host ("{0}:{1}: {2}: {3}" -f $path, $n.Extent.StartLineNumber, $why, ($n.Extent.Text -split "`n")[0]) }
    }
}
Write-Host ("checked {0} file(s), {1} problem(s)" -f $Paths.Count, $bad)
exit $bad
