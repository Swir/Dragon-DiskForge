param(
    [ValidateSet('evaluate', 'self-test')]
    [string]$Mode = 'evaluate',

    [string]$EventName = '',

    [string]$RefName = '',

    [string]$CommitMessage = '',

    [bool]$RetainRequested = $false
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Test-BetaCandidateRetentionDecision {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Event,

        [Parameter(Mandatory = $true)]
        [string]$Branch,

        [string]$Message = '',

        [bool]$Requested = $false
    )

    if ($Branch -ne 'main') {
        return $false
    }

    switch ($Event) {
        'push' {
            return $Message.IndexOf('[beta-candidate]', [System.StringComparison]::OrdinalIgnoreCase) -ge 0
        }
        'workflow_dispatch' {
            return $Requested
        }
        default {
            return $false
        }
    }
}

function Assert-Decision {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [bool]$Expected,

        [Parameter(Mandatory = $true)]
        [bool]$Actual
    )

    if ($Expected -ne $Actual) {
        throw "Retention policy self-test failed for '$Name': expected $Expected, got $Actual."
    }
}

if ($Mode -eq 'self-test') {
    Assert-Decision -Name 'main push with marker' -Expected $true -Actual (Test-BetaCandidateRetentionDecision -Event 'push' -Branch 'main' -Message 'Release hardening [beta-candidate]')
    Assert-Decision -Name 'marker is case-insensitive' -Expected $true -Actual (Test-BetaCandidateRetentionDecision -Event 'push' -Branch 'main' -Message 'Release hardening [BETA-CANDIDATE]')
    Assert-Decision -Name 'main push without marker' -Expected $false -Actual (Test-BetaCandidateRetentionDecision -Event 'push' -Branch 'main' -Message 'Routine hardening')
    Assert-Decision -Name 'non-main push with marker' -Expected $false -Actual (Test-BetaCandidateRetentionDecision -Event 'push' -Branch 'feature/example' -Message '[beta-candidate]')
    Assert-Decision -Name 'manual main retain request' -Expected $true -Actual (Test-BetaCandidateRetentionDecision -Event 'workflow_dispatch' -Branch 'main' -Requested $true)
    Assert-Decision -Name 'manual main verification-only request' -Expected $false -Actual (Test-BetaCandidateRetentionDecision -Event 'workflow_dispatch' -Branch 'main' -Requested $false)
    Assert-Decision -Name 'manual non-main retain request' -Expected $false -Actual (Test-BetaCandidateRetentionDecision -Event 'workflow_dispatch' -Branch 'release/test' -Requested $true)
    Assert-Decision -Name 'pull request never retains' -Expected $false -Actual (Test-BetaCandidateRetentionDecision -Event 'pull_request' -Branch 'main' -Message '[beta-candidate]' -Requested $true)

    Write-Host 'Beta candidate retention policy self-test passed.'
    exit 0
}

$decision = Test-BetaCandidateRetentionDecision -Event $EventName -Branch $RefName -Message $CommitMessage -Requested $RetainRequested
Write-Output $decision.ToString().ToLowerInvariant()
