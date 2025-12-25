<#
.SYNOPSIS
    Upload NNF Coding Standards to Azure Functions API

.DESCRIPTION
    This script reads the NnfCodingStandards.yaml file and uploads it to the
    NNF Standards API, which stores it in Azure Table Storage.

.PARAMETER FunctionAppUrl
    The base URL of the Azure Functions app (e.g., https://your-function-app.azurewebsites.net)

.PARAMETER UpdatedBy
    Your email/alias for tracking who made the update

.PARAMETER ChangeDescription
    Description of what changed in this version
#>

param(
    [Parameter(Mandatory = $false)]
    [string]$FunctionAppUrl = "http://localhost:7071",
    
    [Parameter(Mandatory = $false)]
    [string]$UpdatedBy = "local-dev",
    
    [Parameter(Mandatory = $false)]
    [string]$ChangeDescription = "Updated NNF Coding Standards"
)

$ErrorActionPreference = "Stop"

# Path to the YAML file
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$yamlPath = Join-Path $scriptDir "AiReviewer.Shared\Standards\NnfCodingStandards.yaml"

# If running from repo root
if (-not (Test-Path $yamlPath)) {
    $yamlPath = ".\AiReviewer.Shared\Standards\NnfCodingStandards.yaml"
}

if (-not (Test-Path $yamlPath)) {
    Write-Error "Could not find NnfCodingStandards.yaml at: $yamlPath"
    exit 1
}

Write-Host "Reading YAML from: $yamlPath" -ForegroundColor Cyan
$yamlContent = Get-Content -Path $yamlPath -Raw -Encoding UTF8

# Prepare the request body
$body = @{
    yamlContent = $yamlContent
    updatedBy = $UpdatedBy
    changeDescription = $ChangeDescription
} | ConvertTo-Json -Depth 10

$apiUrl = "$($FunctionAppUrl.TrimEnd('/'))/api/standards/nnf"

Write-Host ""
Write-Host "Uploading NNF Standards..." -ForegroundColor Yellow
Write-Host "  URL: $apiUrl" -ForegroundColor Gray
Write-Host "  Updated By: $UpdatedBy" -ForegroundColor Gray
Write-Host "  Description: $ChangeDescription" -ForegroundColor Gray
Write-Host ""

try {
    $response = Invoke-RestMethod -Uri $apiUrl `
        -Method PUT `
        -Body $body `
        -ContentType "application/json; charset=utf-8" `
        -ErrorAction Stop

    Write-Host "SUCCESS!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Response:" -ForegroundColor Cyan
    $response | ConvertTo-Json -Depth 5 | Write-Host
}
catch {
    $statusCode = $_.Exception.Response.StatusCode.value__
    $errorMessage = $_.ErrorDetails.Message
    
    Write-Host "FAILED!" -ForegroundColor Red
    Write-Host "Status Code: $statusCode" -ForegroundColor Red
    
    if ($errorMessage) {
        Write-Host "Error: $errorMessage" -ForegroundColor Red
    }
    else {
        Write-Host "Error: $_" -ForegroundColor Red
    }
    
    exit 1
}

Write-Host ""
Write-Host "To verify, run:" -ForegroundColor Cyan
Write-Host "  Invoke-RestMethod -Uri '$apiUrl' -Method GET | ConvertTo-Json -Depth 3" -ForegroundColor Gray
