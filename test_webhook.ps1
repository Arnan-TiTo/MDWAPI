$body = '{"events":[]}'
$secret = "5fd35017b35f808ab6c74b52de7d3f92"

$hmac = New-Object System.Security.Cryptography.HMACSHA256
$hmac.Key = [System.Text.Encoding]::UTF8.GetBytes($secret)
$hash = $hmac.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($body))
$sig = [Convert]::ToBase64String($hash)

$headers = @{
    "X-Line-Signature" = $sig
    "ngrok-skip-browser-warning" = "true"
}

$resp = Invoke-WebRequest -Uri "https://nonsequentially-unsummarized-maurine.ngrok-free.dev/api/line/webhook" -Method POST -Body $body -ContentType "application/json" -Headers $headers -UseBasicParsing
Write-Host "Status: $($resp.StatusCode)"
Write-Host "Body: $($resp.Content)"
