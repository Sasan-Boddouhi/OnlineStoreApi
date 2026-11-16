<#
  Batch 67 (API-05): تولید openapi/v1.json بدون پایگاه داده.
  برنامه در محیط Testing فقط ساخته می‌شود (host اجرا نمی‌شود)؛ Swashbuckle CLI سند را از همان می‌خواند.
  استفاده:  pwsh scripts/generate-openapi.ps1            ← فایل را بازنویسی می‌کند
            pwsh scripts/generate-openapi.ps1 -Check     ← اگر فایل commit‌شده با خروجی تازه فرق دارد، با کد 1 خارج می‌شود
#>
param([switch]$Check)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$project = 'Online Store Application/Online Store Application(API).csproj'
$target = Join-Path $root 'openapi/v1.json'
$out = if ($Check) { Join-Path ([IO.Path]::GetTempPath()) 'openapi-v1.generated.json' } else { $target }

# مقادیر ساختگی؛ فقط برای ساخته شدن DI لازم‌اند و به هیچ سرویس بیرونی وصل نمی‌شوند.
$env:ASPNETCORE_ENVIRONMENT = 'Testing'
# ابزار CLI روی net8 اجرا می‌شود ولی بعضی وابستگی‌های برنامه به System.Runtime 9 ارجاع دارند.
$env:DOTNET_ROLL_FORWARD = 'LatestMajor'
$env:Jwt__Key = 'openapi-generation-only-key-0123456789abcdef'
$env:Jwt__Issuer = 'OnlineStoreApi'
$env:Jwt__Audience = 'OnlineStoreClient'
$env:ConnectionStrings__DefaultConnection = 'Server=(none);Database=openapi;Trusted_Connection=True'
$env:Redis__Configuration = 'localhost:6379'
$env:PaymentGatewaySettings__WebhookSecret = 'openapi-generation-only'

dotnet tool restore | Out-Null
dotnet build $project --configuration Release | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'build failed' }

$dll = Get-ChildItem -Path (Join-Path $root 'Online Store Application/bin/Release') -Recurse -Filter 'Online Store Application(API).dll' | Select-Object -First 1
dotnet swagger tofile --output $out $dll.FullName v1
if ($LASTEXITCODE -ne 0) { throw 'swagger tofile failed' }

# پایانهٔ خط یکسان تا diff بین ویندوز و لینوکس صفر شود.
# Doc comments of CRLF source files end up as escaped \r\n inside JSON strings; Linux checkouts have LF.
$text = ([IO.File]::ReadAllText($out)).Replace("`r`n", "`n").Replace('\r\n', '\n').TrimEnd() + "`n"
[IO.File]::WriteAllText($out, $text, (New-Object Text.UTF8Encoding($false)))

if ($Check) {
    $committed = ([IO.File]::ReadAllText($target)).Replace("`r`n", "`n")
    if ($committed -ne $text) {
        $a = $committed -split "`n"; $b = $text -split "`n"
        $shown = 0
        for ($i = 0; $i -lt [Math]::Max($a.Length, $b.Length) -and $shown -lt 10; $i++) {
            if ($a[$i] -ne $b[$i]) { Write-Host "line $($i + 1)`n  committed: $($a[$i])`n  generated: $($b[$i])"; $shown++ }
        }
        Write-Error 'openapi/v1.json is out of date. Run scripts/generate-openapi.ps1 and commit the result.'
        exit 1
    }
    Write-Host 'openapi/v1.json is up to date.'
}
