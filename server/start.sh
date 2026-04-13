#!/bin/bash
echo "가상환경을 활성화하고 서버들을 백그라운드에서 실행합니다..."

# 가상환경 켜기
cd myproject
source venv/bin/activate

# 1. 게임 서버 & 로그인 서버 실행 (루트 폴더)
nohup uvicorn 8000:app --host 0.0.0.0 --port 8000 --workers 4 >> log_8000.out 2>&1 &
nohup uvicorn 8002:app --host 0.0.0.0 --port 8002 --workers 1 >> log_8002.out 2>&1 &

# 2. ML 관련 서버 실행 (server 폴더로 이동)
cd server
nohup uvicorn main:app --host 0.0.0.0 --port 8001 --workers 1 >> log_8001.out 2>&1 &
nohup python -u ml_worker.py >> ml_worker.log 2>&1 &

echo "모든 서버 실행 완료!"