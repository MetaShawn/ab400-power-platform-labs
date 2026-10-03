<#
.SYNOPSIS
  W02: exercise the custom APIs in Dev2 through the Web API.

.NOTES
  Location: <repo>\scripts\W02-Test-CustomApis.ps1
  Auth:     Azure CLI token (az login --allow-no-subscriptions).
  Creates one test account named "W02 Test <timestamp>" and prints the account id at the end.
#>
param(
    [string]$OrgUrl = 'https://orgaa835af7.crm3.dynamics.com'
)

$ErrorActionPreference = 'Stop'
$api = "$OrgUrl/api/data/v9.2"

$token = az account get-access-token --resource $OrgUrl --query accessToken -o tsv
if (-not $token) { throw 'No token returned. Run: az login --allow-no-subscriptions' }

$headers = @{
    'Authorization'    = "Bearer $token"
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
    'Accept'           = 'application/json'
}

function Invoke-Dv([string]$Method, [string]$Path, [hashtable]$Body, [hashtable]$ExtraHeaders) {
    $h = $headers.Clone()
    if ($ExtraHeaders) { foreach ($k in $ExtraHeaders.Keys) { $h[$k] = $ExtraHeaders[$k] } }
    $params = @{ Method = $Method; Uri = "$api/$Path"; Headers = $h }
    if ($Body) {
        $params.ContentType = 'application/json; charset=utf-8'
        $params.Body = [Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 5))
    }
    try {
        return Invoke-RestMethod @params
    } catch {
        $status = $null
        if ($_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode }
        $msg = $_.ErrorDetails.Message
        if ($msg) { try { $msg = ($msg | ConvertFrom-Json).error.message } catch { } }
        Write-Host "  -> HTTP $status : $msg" -ForegroundColor Yellow
        return $null
    }
}

function Show([string]$Title) { Write-Host "`n=== $Title ===" -ForegroundColor Cyan }

# 1. Test account at 50,000 (W01 steps also fire: account number, follow-up task)
Show '1. Create test account, creditlimit 50,000'
$name = 'W02 Test ' + (Get-Date -Format 'yyyyMMdd-HHmmss')
$account = Invoke-Dv 'POST' 'accounts?$select=accountid,name,creditlimit' @{ name = $name; creditlimit = 50000 } @{ Prefer = 'return=representation' }
$id = $account.accountid
$account | Select-Object name, accountid, creditlimit | Format-List

# 2. Function: GET, parameters in the URL via a parameter alias
Show '2. GET ab4_GetCreditHeadroom (function, global)'
Invoke-Dv 'GET' "ab4_GetCreditHeadroom(AccountId=@a)?@a=$id" |
    Select-Object CurrentLimit, AutoApproveCeiling, Headroom | Format-List

# 3. Bound action under the ceiling: approved, data changes
Show '3. POST ab4_RequestCreditIncrease 80,000 (bound action, expect Approved=True)'
Invoke-Dv 'POST' "accounts($id)/Microsoft.Dynamics.CRM.ab4_RequestCreditIncrease" @{ RequestedLimit = 80000; Justification = 'Seasonal volume' } |
    Select-Object Approved, NewCreditLimit, Outcome | Format-List

Show '4. GET ab4_GetCreditHeadroom again (expect CurrentLimit 80,000, Headroom 20,000)'
Invoke-Dv 'GET' "ab4_GetCreditHeadroom(AccountId=@a)?@a=$id" |
    Select-Object CurrentLimit, AutoApproveCeiling, Headroom | Format-List

# 5. Over the ceiling: nothing changes, the business event is emitted from inside the plug-in
Show '5. POST ab4_RequestCreditIncrease 125,000 (expect Approved=False, event emitted, flow creates a task)'
Invoke-Dv 'POST' "accounts($id)/Microsoft.Dynamics.CRM.ab4_RequestCreditIncrease" @{ RequestedLimit = 125000; Justification = 'New distribution contract' } |
    Select-Object Approved, NewCreditLimit, Outcome | Format-List

# 6. Business rule failure: the InvalidPluginExecutionException message comes back as the HTTP error
Show '6. POST ab4_RequestCreditIncrease 60,000 (expect HTTP 400: must be higher than the current limit)'
$null = Invoke-Dv 'POST' "accounts($id)/Microsoft.Dynamics.CRM.ab4_RequestCreditIncrease" @{ RequestedLimit = 60000 }

# 7. Binding matters: the unbound URL for a bound API does not exist
Show '7. POST /ab4_RequestCreditIncrease without the account segment (expect HTTP 404)'
$null = Invoke-Dv 'POST' 'ab4_RequestCreditIncrease' @{ RequestedLimit = 90000 }

# 8. External-system pattern: call the event API directly, no plug-in involved
Show '8. POST ab4_OnCreditIncreaseRequested directly (expect HTTP 204, flow creates a second task)'
$sent = Invoke-Dv 'POST' 'ab4_OnCreditIncreaseRequested' @{
    AccountId      = $id
    AccountName    = $name
    RequestedLimit = 200000
    Justification  = 'Simulated ERP event'
}
Write-Host '  -> sent (204 returns no body)'

# 9. What the pipeline recorded
Show '9. Look at these in a browser signed in to Dev2'
Write-Host "Account : $OrgUrl/main.aspx?pagetype=entityrecord&etn=account&id=$id"
Write-Host "Trace   : $api/plugintracelogs?`$select=typename,messagename,depth,messageblock,exceptiondetails,createdon&`$orderby=createdon desc&`$top=15"
Write-Host "Notes   : $api/annotations?`$select=subject,notetext,createdon&`$filter=_objectid_value eq $id"
Write-Host "Tasks   : $api/tasks?`$select=subject,description,createdon&`$filter=_regardingobjectid_value eq $id&`$orderby=createdon"