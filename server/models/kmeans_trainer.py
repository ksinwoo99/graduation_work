import sys
import os
import pymysql
import pandas as pd
import numpy as np
from sklearn.cluster import KMeans
import joblib
import warnings
from datetime import datetime
from sklearn.preprocessing import StandardScaler

# 상위 폴더(server)에 있는 모듈들 불러오기
sys.path.append(os.path.dirname(os.path.abspath(os.path.dirname(__file__))))
from config import DB_CONFIG, MODEL_PATH
from utils import extract_features

warnings.filterwarnings('ignore')

pd.set_option('display.max_columns', None)
pd.set_option('display.width', 1000)

# ── 학습 하이퍼파라미터 ───────────────────────────────────
_MIN_TRAINING_SAMPLES = 10   # 학습 최소 데이터 수 (미달 시 스킵)
_N_CLUSTERS           = 3    # KMeans 군집 수
_KMEANS_N_INIT        = 10   # KMeans 초기화 반복 횟수
_KMEANS_RANDOM_STATE  = 42

# ── 피처 가중치 ────────────────────────────────────────────
# StandardScaler 정규화 이후에 곱해야 KMeans 거리 계산에 실제 반영됩니다.
# (정규화 이전에 곱하면 스케일러가 다시 평탄화해 효과 없음)
# predict_cluster_rank()에서도 동일 가중치를 반드시 적용해야 합니다.
_LOOP_FEATURE_WEIGHTS: dict[str, float] = {
    'has_loop':           3.0,
    'loop_efficiency':    2.0,
    'has_infinite_while': 2.0,
}

# 군집 rank → 표시 레이블 (학습 로그 및 메타데이터용)
_RANK_LABELS = ["단순 코드형", "일반 학습자형", "효율 최적화형"]


def train():
    now = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    print(f"\n{'=' * 40}")
    print(f"KMeans 학습 시작 ({now})")
    print(f"{'=' * 40}\n")
    
    conn = pymysql.connect(**DB_CONFIG)
    try:
        query = "SELECT source_code, execution_time, score FROM code_logs WHERE is_success = 1"
        cursor = conn.cursor()
        cursor.execute(query)
        data_list = cursor.fetchall()
        df = pd.DataFrame(data_list)
    finally:
        conn.close()

    if len(df) < _MIN_TRAINING_SAMPLES:
        print(f"데이터 부족 (현재 {len(df)}개 / 최소 {_MIN_TRAINING_SAMPLES}개 필요) - 학습 스킵")
        return
    
    print("특징(Feature) 추출 및 스케일링 진행 중...")
    extracted_list = df['source_code'].apply(extract_features)
    feature_df = pd.DataFrame(extracted_list.tolist())
    feature_df['execution_time'] = df['execution_time']
    # score는 클러스터 의미 정렬(rank 0/1/2 결정)에만 사용하며 학습 피처에서 제외합니다.
    # 순환 결합 방지: score 자체가 extract_features() 기반 규칙으로 계산되기 때문에
    # 학습 피처에 포함하면 KMeans가 규칙을 그대로 암기하는 현상이 발생합니다.

    scaler = StandardScaler()
    scaled_features = scaler.fit_transform(feature_df)

    # ── 피처 가중치 적용 ──────────────────────────────────
    # StandardScaler 이후에 곱해야 KMeans 거리 계산에 실제로 반영됩니다.
    col_names   = feature_df.columns.tolist()
    weights_arr = np.ones(len(col_names), dtype=float)
    for col, w in _LOOP_FEATURE_WEIGHTS.items():
        if col in col_names:
            weights_arr[col_names.index(col)] = w

    weighted_scaled = scaled_features * weights_arr

    print("K-Means 모델 학습 중...")
    try:
        kmeans = KMeans(n_clusters=_N_CLUSTERS, random_state=_KMEANS_RANDOM_STATE, n_init=_KMEANS_N_INIT)
        df['cluster_id'] = kmeans.fit_predict(weighted_scaled)

        feature_df['cluster_id'] = df['cluster_id']
        summary = feature_df.groupby('cluster_id').mean().round(4)

        print("\n[군집화 결과 요약]")
        print("-" * 50)
        print(summary)
        print("-" * 50)

        # ── 군집 의미 자동 정렬 ──────────────────────────────────
        # score 평균이 낮은 군집 → rank 0 (단순), 중간 → rank 1 (성장), 높은 → rank 2 (효율)
        # KMeans는 매 학습마다 cluster ID 0/1/2 의 의미가 바뀔 수 있으므로
        # 힌트 텍스트와의 매핑을 score 기준으로 고정.
        # df['score'] 는 학습 피처에서는 제외했지만 정렬 기준으로는 여전히 활용합니다.
        score_means      = df.groupby('cluster_id')['score'].mean()
        sorted_by_score  = score_means.sort_values().index.tolist()   # 점수 낮은 순
        cluster_rank_map = {int(old): new for new, old in enumerate(sorted_by_score)}

        print("\n[군집 의미 자동 정렬]")
        for raw_id, rank in sorted(cluster_rank_map.items()):
            label = _RANK_LABELS[rank]
            print(f"  raw cluster {raw_id} (score 평균 {score_means[raw_id]:.2f}) → rank {rank} [{label}]")

        # ── 학습 메타데이터 구성 ─────────────────────────────────
        # /api/model_status 엔드포인트가 이 값을 읽어 AWS에서 디버깅에 활용
        cluster_summary = {}
        for raw_id, rank in cluster_rank_map.items():
            label = _RANK_LABELS[rank]
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
            'model':           kmeans,
            'scaler':          scaler,
            'cluster_rank':    cluster_rank_map,
            'feature_weights': weights_arr.tolist(),   # predict_cluster_rank()에서 동일하게 적용
            'feature_names':   col_names,              # 가중치-컬럼 매핑 검증용
            'meta':            meta,
        }, MODEL_PATH)
        print(f"\n✅ 학습 완료 및 모델 업데이트 성공! (총 학습 데이터: {len(df)}개)")

    except Exception as e:
        print(f"\n🚨 ML 학습 중 에러 발생: {e}")