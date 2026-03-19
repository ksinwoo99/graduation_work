import sys
import os
sys.path.append(os.path.dirname(os.path.abspath(os.path.dirname(__file__))))
import pymysql
import pandas as pd
import sys
from sklearn.cluster import KMeans
import joblib
import warnings
from datetime import datetime
from sklearn.preprocessing import StandardScaler 

# 상위 폴더(server)에 있는 모듈들 불러오기
from config import DB_CONFIG, MODEL_PATH
from utils import extract_features

# 경고문 무시
warnings.filterwarnings('ignore')

def train():
    now = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    print(f"워커 시작 (시간 : {now})")
    
    conn = pymysql.connect(**DB_CONFIG)
    try:
        
        query = "SELECT source_code, execution_time, score FROM code_logs WHERE is_success = 1"
        df = pd.read_sql(query, conn)
    finally:
        conn.close()

    if len(df) < 10:
        print(f"데이터 부족 (현재 {len(df)}개)")
        return
    
    print("특징 추출 진행 중...")
    extracted_list = df['source_code'].apply(extract_features)
    feature_df = pd.DataFrame(extracted_list.tolist())
    feature_df['execution_time'] = df['execution_time']
    feature_df['score'] = df['score']

    print("데이터 스케일링 중...")
    scaler = StandardScaler()
    scaled_features = scaler.fit_transform(feature_df)

    print("모델 학습 중...")
    try:
        kmeans = KMeans(n_clusters=3, random_state=42, n_init=10)
        df['cluster_id'] = kmeans.fit_predict(scaled_features) 

        print("군집화 결과 요약")
        feature_df['cluster_id'] = df['cluster_id']
        summary = feature_df.groupby('cluster_id').mean().round(2)
        print(summary)
        
        # 모델 저장 시, K-Means 모델과 스케일러(scaler)를 같이 저장해야 함.
        joblib.dump({'model': kmeans, 'scaler': scaler}, MODEL_PATH)
        print(f"학습 완료 및 모델/스케일러 묶음 저장 성공!")

    except Exception as e:
        print(f"ML 학습 중 에러 발생: {e}")