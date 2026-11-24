<#
.SYNOPSIS
  ALV-N030 (brief 16.2b, S22): runs a command again ONLY when it exits with 75 ("restart requested", the API's code after first-run setup completes).
  Any other exit code stops the loop and is returned unchanged, so the caller can pass it on as its own exit code. Dot-source this file.

.DESCRIPTION
  Invoke-RestartOnRequest -Start { ...; $LASTEXITCODE } [-MaxRestarts 10]
    -Start        a script block that runs the process once and returns its exit code as an [int]; it receives the attempt number (0 = first start).
    -MaxRestarts  a bound, so a process that asks for a restart every time cannot loop forever (after it, the last code 75 is returned).
#>

$RestartRequestedExitCode = 75

function Invoke-RestartOnRequest {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][scriptblock]$Start,
        [ValidateRange(0, 100)][int]$MaxRestarts = 10
    )
    $attempt = 0
    while ($true) {
        $code = [int](& $Start $attempt | Select-Object -Last 1)
        if ($code -ne $RestartRequestedExitCode) { return $code }
        if ($attempt -ge $MaxRestarts) {
            Write-Warning "The process asked for a restart $($attempt + 1) times in a row; not restarting again."
            return $code
        }
        $attempt++
        Write-Host "The API asked to be restarted (exit code $RestartRequestedExitCode, first-run setup completed); starting it again..."
    }
}
