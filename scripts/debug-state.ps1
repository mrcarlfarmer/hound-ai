Write-Host "=== /api/portfolio/positions ==="
$pos = Invoke-RestMethod -Uri 'http://localhost:5000/api/portfolio/positions'
$pos | Format-Table symbol, quantity, side, marketValue, currentPrice -AutoSize

Write-Host "`n=== /api/trades ==="
$trades = Invoke-RestMethod -Uri 'http://localhost:5000/api/trades?pageSize=50'
$trades | Format-Table symbol, action, fillStatus, filledQuantity, averageFillPrice, executionTime, orderId -AutoSize

Write-Host "`n=== Alpaca orders (last 30) ==="
# Get full Alpaca order list to see what Hound is missing
$body = '{}'
$sync = Invoke-RestMethod -Uri 'http://localhost:5000/api/trades/sync' -Method Post -ContentType 'application/json' -Body $body
$sync | ConvertTo-Json
