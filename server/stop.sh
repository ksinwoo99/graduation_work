#!/bin/bash
echo "Stopping servers..."

pkill -f uvicorn
pkill -f ml_worker.py

echo "Stop complete!"