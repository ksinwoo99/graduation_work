#!/bin/bash
echo "모든 서버 프로세스를 종료합니다..."

pkill -f uvicorn
pkill -f ml_worker.py

echo "종료 완료!"