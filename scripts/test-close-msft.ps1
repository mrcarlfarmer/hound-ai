try {
    $r = Invoke-RestMethod -Uri 'http://localhost:5000/api/portfolio/positions/MSFT/close' -Method Post -ContentType 'application/json' -Body '{}'
    "OK"; $r | ConvertTo-Json
} catch {
    $resp = $_.Exception.Response
    if ($resp) {
        $code = [int]$resp.StatusCode
        $stream = $resp.GetResponseStream()
        $reader = New-Object System.IO.StreamReader($stream)
        $body = $reader.ReadToEnd()
        "HTTP $code"
        $body
    } else {
        $_.Exception.Message
    }
}
