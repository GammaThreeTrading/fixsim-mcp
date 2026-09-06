# Deploy fixsim-mcp to Azure App Service on the existing FIXSIMSandbox plan (no new plan, no extra cost).
# Run from the repo root with an az-logged-in shell:  .\deploy\deploy.ps1
# Idempotent: creates the web app on first run, redeploys on later runs.
param(
  [string]$App = "fixsim-mcp",
  [string]$ResourceGroup = "FIXSIMSandbox",
  [string]$PlanApp = "fixsim-sandbox",        # borrow this app's App Service plan
  [string]$SandboxCallerKey = ""               # optional: must match Sandbox:CallerKey on fixsim-sandbox
)
$ErrorActionPreference = "Stop"
$plan = az webapp show -n $PlanApp -g $ResourceGroup --query appServicePlanId -o tsv
if (-not $plan) { throw "Could not read the App Service plan of $PlanApp" }

$exists = az webapp show -n $App -g $ResourceGroup --query name -o tsv 2>$null
if (-not $exists) {
  Write-Host "Creating $App on plan $plan"
  az webapp create -n $App -g $ResourceGroup --plan $plan --runtime "dotnet:8" | Out-Null
  az webapp update -n $App -g $ResourceGroup --https-only true | Out-Null
}

$settings = @(
  "FixSim__BaseUrl=https://portal.fixsim.com/",
  "FixSim__Profile=production",
  "Sandbox__Enabled=true",
  "Sandbox__BaseUrl=https://sandbox.fixsim.com/",
  "ASPNETCORE_ENVIRONMENT=Production"
)
if ($SandboxCallerKey) { $settings += "Sandbox__CallerKey=$SandboxCallerKey" }
az webapp config appsettings set -n $App -g $ResourceGroup --settings $settings | Out-Null

$pub = Join-Path $PSScriptRoot "..\publish"
if (Test-Path $pub) { Remove-Item -Recurse -Force $pub }
dotnet publish (Join-Path $PSScriptRoot "..\src\FixSim.Mcp\FixSim.Mcp.csproj") -c Release -o $pub
$zip = Join-Path $PSScriptRoot "..\publish.zip"
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path (Join-Path $pub "*") -DestinationPath $zip
az webapp deploy -n $App -g $ResourceGroup --src-path $zip --type zip | Out-Null
$host_ = az webapp show -n $App -g $ResourceGroup --query defaultHostName -o tsv
Write-Host "Deployed: https://$host_/  (MCP endpoint: https://$host_/mcp)"
Write-Host "Next: CNAME mcp.fixsim.com -> $host_ , then: az webapp config hostname add -n $App -g $ResourceGroup --hostname mcp.fixsim.com ; az webapp config ssl create ..."
