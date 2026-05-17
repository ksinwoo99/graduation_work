#!/bin/bash
echo "RUN..."

cd myproject
# Activate virtual environment
source venv/bin/activate

# 1. Game & Auth Servers
nohup uvicorn 8000:app --host 0.0.0.0 --port 8000 --workers 4 >> log_8000.out 2>&1 &
nohup uvicorn 8002:app --host 0.0.0.0 --port 8002 --workers 1 >> log_8002.out 2>&1 &

# 2. ML Servers
nohup uvicorn main:app --host 0.0.0.0 --port 8001 --workers 1 >> log_8001.out 2>&1 &
nohup python -u ml_worker.py >> ml_worker.log 2>&1 &

echo "Run complete!"