# 터미널에 복사해서 실행 : pip install -r requirements.txt
import pymysql
import os
from dotenv import load_dotenv

load_dotenv()

# DB 접속 정보
DB_CONFIG = {
    "host": os.getenv("DB_HOST"),
    "user": os.getenv("DB_USER"),
    "password": os.getenv("DB_PASSWORD"),
    "database": os.getenv("DB_NAME"),
    "port": 3306,
    "charset": "utf8mb4",
    "cursorclass": pymysql.cursors.DictCursor
}

# ML 모델 파일 경로
MODEL_PATH = 'code_cluster_model.pkl'