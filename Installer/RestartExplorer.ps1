# Restart Windows Explorer
try {
    Get-Process explorer | Stop-Process -Force
    Start-Sleep -Seconds 1
    Start-Process explorer.exe
} catch {
    # Ignore errors
}
