import os
import pymysql
from dotenv import load_dotenv

load_dotenv()

DB_CONFIG = {
    "host":        os.getenv("DB_HOST"),
    "user":        os.getenv("DB_USER"),
    "password":    os.getenv("DB_PASSWORD"),
    "database":    os.getenv("DB_NAME"),
    "port":        int(os.getenv("DB_PORT", "3306")),
    "charset":     "utf8mb4",
    "cursorclass": pymysql.cursors.DictCursor,
}

# ML 모델 파일 경로 (kmeans_trainer.py 학습 시 server/ 기준 상대 경로)
MODEL_PATH = "code_cluster_model.pkl"
