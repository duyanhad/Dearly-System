# Dịch vụ tách nền rembg chạy trên Render (giống hệt bản local).
# POST /api/remove { file, model } -> PNG trong suốt.
FROM python:3.11-slim

ENV PYTHONUNBUFFERED=1 \
    U2NET_HOME=/models \
    OMP_NUM_THREADS=1

# libgl1/libglib: opencv cần cho rembg
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgl1 libglib2.0-0 \
    && rm -rf /var/lib/apt/lists/*

RUN pip install --no-cache-dir "rembg[cli]" onnxruntime

# Model nhỏ (u2netp ~4.7 MB) để vừa RAM 512 MB của gói free; tải sẵn lúc build.
RUN mkdir -p /models \
    && python -c "from rembg import new_session; new_session('u2netp')"

EXPOSE 10000
CMD ["sh", "-c", "rembg s --host 0.0.0.0 --port ${PORT:-10000}"]
