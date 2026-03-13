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

# 상위 폴더(server)에 있는 모듈들 불러오기


from config import DB_CONFIG, MODEL_PATH
from utils import extract_features

warnings.filterwarnings('ignore')

def train():
    now = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    print(f"워커 시작 (시간 : {now})")
    
    conn = pymysql.connect(**DB_CONFIG)
    try:
        query = "SELECT source_code FROM code_logs WHERE is_success = 1"
        df = pd.read_sql(query, conn)
    finally:
        conn.close()

    if len(df) < 10:
        print(f"데이터 부족 (현재 {len(df)}개)")
        return
    
    print("특징 추출 진행 중...")
    # 🌟 utils.py의 함수 사용!
    extracted_list = df['source_code'].apply(extract_features)
    feature_df = pd.DataFrame(extracted_list.tolist())

    print("모델 학습 중...")
    try:
        kmeans = KMeans(n_clusters=3, random_state=42, n_init=10)
        feature_df['cluster_id'] = kmeans.fit_predict(feature_df)

        print("군집화 결과 요약")
        summary = feature_df.groupby('cluster_id').mean().round(1)
        for cluster_id, row in summary.iterrows():
            print(f"🚪 [방 번호: {cluster_id}] | For: {row['for_count']} | While: {row['while_count']} ...")
        
        # config에 적어둔 경로로 모델 저장
        joblib.dump(kmeans, MODEL_PATH)
        print(f"학습 완료 및 모델 저장 성공!")

    except Exception as e:
        print(f"ML 학습 중 에러 발생: {e}")