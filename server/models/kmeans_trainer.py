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

        feature_df['cluster_id'] = df['cluster_id']
        summary = feature_df.groupby('cluster_id').mean().round(4)

        print("\n[군집화 결과 요약]")
        print("-" * 50)
        print(summary)
        print("-" * 50)

        # ── 군집 의미 자동 정렬 ──────────────────────────────────
        # score 평균이 낮은 군집 → rank 0 (단순), 중간 → rank 1 (성장), 높은 → rank 2 (효율)
        # KMeans는 매 학습마다 cluster ID 0/1/2 의 의미가 바뀔 수 있으므로
        # 힌트 텍스트와의 매핑을 score 기준으로 고정
        score_means      = feature_df.groupby('cluster_id')['score'].mean()
        sorted_by_score  = score_means.sort_values().index.tolist()   # 점수 낮은 순
        cluster_rank_map = {int(old): new for new, old in enumerate(sorted_by_score)}

        print("\n[군집 의미 자동 정렬]")
        for raw_id, rank in sorted(cluster_rank_map.items()):
            label = ["단순 코드형", "성장형", "효율 최적화형"][rank]
            print(f"  raw cluster {raw_id} (score 평균 {score_means[raw_id]:.2f}) → rank {rank} [{label}]")

        # ── 학습 메타데이터 구성 ─────────────────────────────────
        # /api/model_status 엔드포인트가 이 값을 읽어 AWS에서 디버깅에 활용
        cluster_summary = {}
        for raw_id, rank in cluster_rank_map.items():
            label = ["단순 코드형", "성장형", "효율 최적화형"][rank]
            count = int((feature_df['cluster_id'] == raw_id).sum())
            row   = summary.loc[raw_id]
            cluster_summary[str(rank)] = {
                "label":                label,
                "count":                count,
                "score_mean":           round(float(score_means[raw_id]), 2),
                "has_loop_mean":        round(float(row.get("has_loop", 0)), 3),
                "loop_efficiency_mean": round(float(row.get("loop_efficiency", 0)), 3),
                "while_count_mean":     round(float(row.get("while_count", 0)), 3),
                "has_infinite_while_mean": round(float(row.get("has_infinite_while", 0)), 3),
            }

        meta = {
            "trained_at":  now,
            "data_count":  len(df),
            "cluster_summary": cluster_summary,
        }

        joblib.dump({
            'model':        kmeans,
            'scaler':       scaler,
            'cluster_rank': cluster_rank_map,
            'meta':         meta,
        }, MODEL_PATH)
        print(f"\n✅ 학습 완료 및 모델 업데이트 성공! (총 학습 데이터: {len(df)}개)")

    except Exception as e:
        print(f"\n🚨 ML 학습 중 에러 발생: {e}")