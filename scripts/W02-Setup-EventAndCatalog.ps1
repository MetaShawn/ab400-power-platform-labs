<#
.SYNOPSIS
  W02: create the function custom API, the business-event custom API and the business-events catalog
  in Dev2 with the Dataverse Web API, inside the AB400 solution. Safe to re-run: existing rows are skipped.

.NOTES
  Location: <repo>\scripts\W02-Setup-EventAndCatalog.ps1
  Auth:     Azure CLI token for the Dataverse resource (az login --allow-no-subscriptions first).
  Creates:
    customapi         ab4_GetCreditHeadroom          Function, Global, step type None, plug-in GetCreditHeadroomApi
    customapi         ab4_OnCreditIncreaseRequested  Action, Global, step type Async Only, no plug-in, no response
    catalog           ab4_AB400Lab                   root (represents the solution)
    catalog           ab4_CreditEvents               category under the root
    catalog           ab4_LabTables                  category under the root
    catalogassignment ab4_OnCreditIncreaseRequested  custom API -> Credit Events
    catalogassignment ab4_Account                    account table -> Lab Tables
#>
param(
    [string]$OrgUrl = 'https://orgaa835af7.crm3.dynamics.com',
    [string]$Solution = 'AB400'
)

$ErrorActionPreference = 'Stop'
$api = "$OrgUrl/api/data/v9.2"

$token = az account get-access-token --resource $OrgUrl --query accessToken -o tsv
if (-not $token) { throw 'No token returned. Run: az login --allow-no-subscriptions' }

$headers = @{
    'Authorization'            = "Bearer $token"
    'OData-MaxVersion'         = '4.0'
    'OData-Version'            = '4.0'
    'Accept'                   = 'application/json'
    'MSCRM.SolutionUniqueName' = $Solution   # every row created below is added to AB400
}

function Get-FirstId([string]$Query, [string]$IdProperty) {
    $result = Invoke-RestMethod -Method Get -Uri "$api/$Query" -Headers $headers
    if ($result.value.Count -gt 0) { return $result.value[0].$IdProperty }
    return $null
}

function New-Row([string]$EntitySet, [hashtable]$Body) {
    $json = $Body | ConvertTo-Json -Depth 10
    $response = Invoke-WebRequest -Method Post -Uri "$api/$EntitySet" -Headers $headers `
        -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($json)) -UseBasicParsing
    $entityId = $response.Headers['OData-EntityId']
    if ($entityId -is [array]) { $entityId = $entityId[0] }   # PowerShell 7 returns string[]
    return [regex]::Match($entityId, '\(([0-9a-fA-F-]{36})\)').Groups[1].Value
}

$customizable = @{ Value = $true }   # dev environment; set to false before shipping managed (W11)

# --- 1. Function custom API: ab4_GetCreditHeadroom -------------------------------------------
$typeId = Get-FirstId "plugintypes?`$select=plugintypeid&`$filter=typename eq 'Ab4.Plugins.GetCreditHeadroomApi'" 'plugintypeid'
if (-not $typeId) { throw 'Plug-in type Ab4.Plugins.GetCreditHeadroomApi not found. Update the Ab4.Plugins package first.' }

if (Get-FirstId "customapis?`$select=customapiid&`$filter=uniquename eq 'ab4_GetCreditHeadroom'" 'customapiid') {
    Write-Host 'ab4_GetCreditHeadroom exists, skipped.'
} else {
    $id = New-Row 'customapis' @{
        uniquename                      = 'ab4_GetCreditHeadroom'
        name                            = 'ab4_GetCreditHeadroom'
        displayname                     = 'Get Credit Headroom'
        description                     = 'Returns how far an account credit limit can rise before it needs finance approval.'
        bindingtype                     = 0       # Global
        isfunction                      = $true   # HTTP GET, must return at least one response property
        isprivate                       = $false
        allowedcustomprocessingsteptype = 0       # None: nobody else can register steps on this message
        iscustomizable                  = $customizable
        'PluginTypeId@odata.bind'       = "plugintypes($typeId)"
        CustomAPIRequestParameters      = @(
            @{ uniquename = 'AccountId'; name = 'ab4_GetCreditHeadroom.AccountId'; displayname = 'Account Id'
               description = 'The account to check.'; type = 12; isoptional = $false; iscustomizable = $customizable }   # 12 = Guid
        )
        CustomAPIResponseProperties     = @(
            @{ uniquename = 'CurrentLimit'; name = 'ab4_GetCreditHeadroom.CurrentLimit'; displayname = 'Current Limit'
               description = 'Current credit limit, 0 when blank.'; type = 2; iscustomizable = $customizable }            # 2 = Decimal
            @{ uniquename = 'AutoApproveCeiling'; name = 'ab4_GetCreditHeadroom.AutoApproveCeiling'; displayname = 'Auto-approve Ceiling'
               description = 'Highest limit that is approved without finance.'; type = 2; iscustomizable = $customizable }
            @{ uniquename = 'Headroom'; name = 'ab4_GetCreditHeadroom.Headroom'; displayname = 'Headroom'
               description = 'Ceiling minus current limit, never below 0.'; type = 2; iscustomizable = $customizable }
        )
    }
    Write-Host "Created ab4_GetCreditHeadroom ($id)."
}

# --- 2. Business-event custom API: ab4_OnCreditIncreaseRequested -------------------------------
$eventApiId = Get-FirstId "customapis?`$select=customapiid&`$filter=uniquename eq 'ab4_OnCreditIncreaseRequested'" 'customapiid'
if ($eventApiId) {
    Write-Host 'ab4_OnCreditIncreaseRequested exists, skipped.'
} else {
    $eventApiId = New-Row 'customapis' @{
        uniquename                      = 'ab4_OnCreditIncreaseRequested'
        name                            = 'ab4_OnCreditIncreaseRequested'
        displayname                     = 'On Credit Increase Requested'
        description                     = 'Business event: a credit increase above the auto-approve ceiling was requested.'
        bindingtype                     = 0       # Global
        isfunction                      = $false  # events are actions
        isprivate                       = $false
        allowedcustomprocessingsteptype = 1       # Async Only: subscribers can listen, nobody can change or cancel it
        iscustomizable                  = $customizable
        # No PluginTypeId and no response properties: the event only carries data to subscribers.
        CustomAPIRequestParameters      = @(
            @{ uniquename = 'AccountId'; name = 'ab4_OnCreditIncreaseRequested.AccountId'; displayname = 'Account Id'
               description = 'Account the request is for.'; type = 12; isoptional = $false; iscustomizable = $customizable }
            @{ uniquename = 'AccountName'; name = 'ab4_OnCreditIncreaseRequested.AccountName'; displayname = 'Account Name'
               description = 'Account name at the time of the request.'; type = 10; isoptional = $false; iscustomizable = $customizable }  # 10 = String
            @{ uniquename = 'CurrentLimit'; name = 'ab4_OnCreditIncreaseRequested.CurrentLimit'; displayname = 'Current Limit'
               description = 'Credit limit before the request.'; type = 2; isoptional = $true; iscustomizable = $customizable }
            @{ uniquename = 'RequestedLimit'; name = 'ab4_OnCreditIncreaseRequested.RequestedLimit'; displayname = 'Requested Limit'
               description = 'Limit the requester asked for.'; type = 2; isoptional = $false; iscustomizable = $customizable }
            @{ uniquename = 'Justification'; name = 'ab4_OnCreditIncreaseRequested.Justification'; displayname = 'Justification'
               description = 'Why the increase is needed.'; type = 10; isoptional = $true; iscustomizable = $customizable }
        )
    }
    Write-Host "Created ab4_OnCreditIncreaseRequested ($eventApiId)."
}

# --- 3. Catalog: root + two categories ----------------------------------------------------------
function Get-OrCreateCatalog([string]$UniqueName, [string]$Name, [string]$Description, [string]$ParentId) {
    $existing = Get-FirstId "catalogs?`$select=catalogid&`$filter=uniquename eq '$UniqueName'" 'catalogid'
    if ($existing) { Write-Host "Catalog $UniqueName exists, skipped."; return $existing }
    $body = @{
        uniquename     = $UniqueName
        name           = $Name
        displayname    = $Name
        description    = $Description
        iscustomizable = $customizable
    }
    if ($ParentId) { $body['ParentCatalogId@odata.bind'] = "catalogs($ParentId)" }
    $newId = New-Row 'catalogs' $body
    Write-Host "Created catalog $UniqueName ($newId)."
    return $newId
}

$rootId    = Get-OrCreateCatalog 'ab4_AB400Lab'     'AB400 Lab'     'Root business-events catalog for the AB400 lab solution.' $null
$creditId  = Get-OrCreateCatalog 'ab4_CreditEvents' 'Credit Events' 'Credit approval events raised by the AB400 lab.'          $rootId
$tablesId  = Get-OrCreateCatalog 'ab4_LabTables'    'Lab Tables'    'Tables whose events the AB400 lab exposes.'                $rootId

# --- 4. Catalog assignments (always to a category, never to the root) ---------------------------
if (Get-FirstId "catalogassignments?`$select=catalogassignmentid&`$filter=name eq 'ab4_OnCreditIncreaseRequested'" 'catalogassignmentid') {
    Write-Host 'Assignment ab4_OnCreditIncreaseRequested exists, skipped.'
} else {
    $null = New-Row 'catalogassignments' @{
        name                     = 'ab4_OnCreditIncreaseRequested'
        iscustomizable           = $customizable
        'CustomAPIId@odata.bind' = "customapis($eventApiId)"
        'CatalogId@odata.bind'   = "catalogs($creditId)"
    }
    Write-Host 'Assigned ab4_OnCreditIncreaseRequested to Credit Events.'
}

$accountEntityId = Get-FirstId "entities?`$select=entityid&`$filter=name eq 'Account'" 'entityid'
if (Get-FirstId "catalogassignments?`$select=catalogassignmentid&`$filter=name eq 'ab4_Account'" 'catalogassignmentid') {
    Write-Host 'Assignment ab4_Account exists, skipped.'
} else {
    $null = New-Row 'catalogassignments' @{
        name                   = 'ab4_Account'
        iscustomizable         = $customizable
        'EntityId@odata.bind'  = "entities($accountEntityId)"
        'CatalogId@odata.bind' = "catalogs($tablesId)"
    }
    Write-Host 'Assigned account to Lab Tables.'
}

# --- 5. Show what the Power Automate trigger will see --------------------------------------------
$check = Invoke-RestMethod -Method Get -Headers $headers -Uri ("$api/catalogassignments?`$select=name" +
    "&`$expand=CatalogId(`$select=uniquename;`$expand=ParentCatalogId(`$select=uniquename)),CustomAPIId(`$select=uniquename),EntityId(`$select=name)" +
    "&`$filter=startswith(name,'ab4_')")
$check.value | ForEach-Object {
    [pscustomobject]@{
        Assignment = $_.name
        Category   = $_.CatalogId.uniquename
        Root       = $_.CatalogId.ParentCatalogId.uniquename
        Object     = if ($_.CustomAPIId) { "customapi $($_.CustomAPIId.uniquename)" } else { "entity $($_.EntityId.name)" }
    }
} | Format-Table -AutoSize