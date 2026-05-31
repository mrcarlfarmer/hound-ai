$r = Invoke-RestMethod -Uri 'http://localhost:5000/api/trades/sync' -Method Post -ContentType 'application/json' -Body '{}'
$r | ConvertTo-Json -Depth 5

Write-Host "`n=== Trades after sync ==="
powershell -ExecutionPolicy Bypass -File .\scripts\db-list-trades.ps1
