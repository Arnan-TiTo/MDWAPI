try {
    $resp = Invoke-WebRequest -Uri "https://nonsequentially-unsummarized-maurine.ngrok-free.dev/api/line/login" -Headers @{"ngrok-skip-browser-warning"="true"} -UseBasicParsing -MaximumRedirection 0
    Write-Host "Status: $($resp.StatusCode)"
} catch {
    $r = $_.Exception.Response
    Write-Host "Status: $($r.StatusCode.value__)"
    Write-Host "Location: $($r.Headers.Location)"
}
