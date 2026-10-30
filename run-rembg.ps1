# Chạy dịch vụ tách nền rembg cho backend (cửa sổ riêng, giữ nguyên khi mở).
# Cài lần đầu:  pip install "rembg[cpu,cli]"
# Model mặc định birefnet-general (~930 MB) tự tải ở lần tách nền đầu tiên.
$rembg = Join-Path $env:LOCALAPPDATA "Programs\Python\Python313\Scripts\rembg.exe"
if (-not (Test-Path $rembg)) {
    $rembg = "rembg"
}
& $rembg s --host 127.0.0.1 --port 7000 --no-ui
