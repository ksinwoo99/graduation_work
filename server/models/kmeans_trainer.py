import sys
import os
import pymysql
import pandas as pd
from sklearn.cluster import KMeans
import joblib
import warnings
from datetime import datetime
from sklearn.preprocessing import StandardScaler 

# 상위 폴더(server)에 있는 모듈들 불러오기
sys.path.append(os.path.dirname(os.path.abspath(os.path.dirname(__file__))))
from config import DB_CONFIG, MODEL_PATH
from utils import extract_features

# 경고문 무시
warnings.filterwarnings('ignore')

# Pandas 터미널 출력 설정
pd.set_option('display.max_columns', None)
pd.set_option('display.width', 1000)

def train():
    now = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    print(f"\n========================================")
    print(f"🚀 ML 백그라운드 워커 구동 시작 ({now})")
    print(f"========================================\n")
    
    conn = pymysql.connect(**DB_CONFIG)
    try:
        query = "SELECT source_code, execution_time, score FROM code_logs WHERE is_success = 1"
        cursor = conn.cursor()
        cursor.execute(query)
        data_list = cursor.fetchall()
        df = pd.DataFrame(data_list)
    finally:
        conn.close()

    if len(df) < 10:
        print(f"⚠️ 데이터 부족 (현재 {len(df)}개 / 최소 10개 필요) - 학습 스킵")
        return
    
    print("특징(Feature) 추출 및 스케일링 진행 중...")
    extracted_list = df['source_code'].apply(extract_features)
    feature_df = pd.DataFrame(extracted_list.tolist())
    feature_df['execution_time'] = df['execution_time']
    feature_df['score'] = df['score']

    scaler = StandardScaler()
    scaled_features = scaler.fit_transform(feature_df)

    print("K-Means 모델 학습 중...")
    try:
        kmeans = KMeans(n_clusters=3, random_state=42, n_init=10)
        df['cluster_id'] = kmeans.fit_predict(scaled_features) 

        print("\n[군집화 결과 요약]")
        print("-" * 50)
        feature_df['cluster_id'] = df['cluster_id']
        summary = feature_df.groupby('cluster_id').mean().round(4)
        print(summary)
        print("-" * 50)
        
        joblib.dump({'model': kmeans, 'scaler': scaler}, MODEL_PATH)
        print(f"\n✅ 학습 완료 및 모델 업데이트 성공! (총 학습 데이터: {len(df)}개)")

    except Exception as e:
        print(f"\n🚨 ML 학습 중 에러 발생: {e}")