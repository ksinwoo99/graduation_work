"""
server/models/kmeans_trainer.py
─────────────────────────────────────────────────────────────
코드 로그를 비지도 군집화하여 학습자 패턴 군집 모델을 학습/저장합니다.

학습 파이프라인:
    DB(code_logs is_success=1)
        → extract_features() 로 코드 피처 추출
        → StandardScaler 정규화
        → 가중치 적용 (루프 관련 피처 강조)
        → KMeans(n_clusters=3) 학습
        → score 평균 기준으로 cluster ID → rank(0/1/2) 정렬
        → joblib pkl 로 저장 (main.py 가 mtime 기반 핫리로드)
"""

import os
import sys
import warnings
from datetime import datetime

import joblib
import numpy as np
import pandas as pd
import pymysql
from sklearn.cluster import KMeans
from sklearn.preprocessing import StandardScaler

# 상위 폴더(server) 모듈 경로 보정
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

# 루프 관련 피처를 강조하기 위한 가중치.
# StandardScaler 정규화 이후에 곱해야 KMeans 거리 계산에 실제 반영됩니다.
# (정규화 이전에 곱하면 스케일러가 다시 평탄화해 효과 없음)
# main.predict_cluster_rank() 가 추론 시 동일한 가중치를 적용해야 합니다.
_LOOP_FEATURE_WEIGHTS: dict[str, float] = {
    'has_loop':           3.0,
    'loop_efficiency':    2.0,
    'has_infinite_while': 2.0,
}

# rank → 표시 레이블 (학습 로그 및 메타데이터용)
_RANK_LABELS = ["단순 코드형", "일반 학습자형", "효율 최적화형"]


def _load_success_logs() -> pd.DataFrame:
    """성공한 제출 로그를 DataFrame 으로 로드."""
    conn = pymysql.connect(**DB_CONFIG)
    try:
        cursor = conn.cursor()
        cursor.execute(
            "SELECT source_code, execution_time, score "
            "FROM code_logs WHERE is_success = 1"
        )
        return pd.DataFrame(cursor.fetchall())
    finally:
        conn.close()


def _build_feature_matrix(df: pd.DataFrame) -> pd.DataFrame:
    """
    code_logs DataFrame → KMeans 입력 피처 DataFrame.

    score 컬럼은 군집 의미 정렬(rank 결정) 용도로만 사용하며 학습 피처에서 제외합니다.
    score 자체가 extract_features() 기반 규칙으로 계산되므로, 포함하면 KMeans 가
    규칙을 그대로 암기하는 순환 결합이 발생합니다.
    """
    extracted = df['source_code'].apply(extract_features)
    feature_df = pd.DataFrame(extracted.tolist())
    feature_df['execution_time'] = df['execution_time']
    return feature_df


def _build_weights_array(col_names: list[str]) -> np.ndarray:
    """_LOOP_FEATURE_WEIGHTS 를 컬럼 순서에 맞춘 numpy 배열로 변환."""
    weights = np.ones(len(col_names), dtype=float)
    for col, w in _LOOP_FEATURE_WEIGHTS.items():
        if col in col_names:
            weights[col_names.index(col)] = w
    return weights


def _align_cluster_ranks(df: pd.DataFrame, feature_df: pd.DataFrame) -> dict[int, int]:
    """
    score 평균 오름차순으로 raw cluster ID 를 rank(0/1/2) 에 정렬해 반환합니다.
    KMeans 의 cluster ID 가 학습마다 바뀌어도 힌트 매핑이 일관되도록 보장합니다.
    """
    score_means     = df.groupby('cluster_id')['score'].mean()
    sorted_by_score = score_means.sort_values().index.tolist()
    return {int(old): new for new, old in enumerate(sorted_by_score)}


def _build_cluster_summary(
    feature_df: pd.DataFrame, df: pd.DataFrame,
    cluster_rank_map: dict[int, int],
) -> dict[str, dict]:
    """/api/model_status 에 노출할 군집별 통계 요약."""
    summary     = feature_df.groupby('cluster_id').mean().round(4)
    score_means = df.groupby('cluster_id')['score'].mean()

    out: dict[str, dict] = {}
    for raw_id, rank in cluster_rank_map.items():
        row = summary.loc[raw_id]
        out[str(rank)] = {
            "label":                   _RANK_LABELS[rank],
            "count":                   int((feature_df['cluster_id'] == raw_id).sum()),
            "score_mean":              round(float(score_means[raw_id]), 2),
            "has_loop_mean":           round(float(row.get("has_loop", 0)), 3),
            "loop_efficiency_mean":    round(float(row.get("loop_efficiency", 0)), 3),
            "while_count_mean":        round(float(row.get("while_count", 0)), 3),
            "has_infinite_while_mean": round(float(row.get("has_infinite_while", 0)), 3),
        }
    return out


def train() -> None:
    """KMeans 모델을 학습해 pkl 로 저장합니다."""
    now = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    print(f"\n{'=' * 40}")
    print(f"KMeans 학습 시작 ({now})")
    print(f"{'=' * 40}\n")

    df = _load_success_logs()
    if len(df) < _MIN_TRAINING_SAMPLES:
        print(
            f"데이터 부족 (현재 {len(df)}개 / 최소 {_MIN_TRAINING_SAMPLES}개 필요) "
            f"- 학습 스킵"
        )
        return

    print("특징(Feature) 추출 및 스케일링 진행 중...")
    feature_df  = _build_feature_matrix(df)
    col_names   = feature_df.columns.tolist()
    weights_arr = _build_weights_array(col_names)

    scaler          = StandardScaler()
    scaled_features = scaler.fit_transform(feature_df)
    weighted_scaled = scaled_features * weights_arr

    print("K-Means 모델 학습 중...")
    try:
        kmeans = KMeans(
            n_clusters=_N_CLUSTERS,
            random_state=_KMEANS_RANDOM_STATE,
            n_init=_KMEANS_N_INIT,
        )
        df['cluster_id']         = kmeans.fit_predict(weighted_scaled)
        feature_df['cluster_id'] = df['cluster_id']

        summary = feature_df.groupby('cluster_id').mean().round(4)
        print("\n[군집화 결과 요약]")
        print("-" * 50)
        print(summary)
        print("-" * 50)

        cluster_rank_map = _align_cluster_ranks(df, feature_df)
        score_means      = df.groupby('cluster_id')['score'].mean()

        print("\n[군집 의미 자동 정렬]")
        for raw_id, rank in sorted(cluster_rank_map.items()):
            label = _RANK_LABELS[rank]
            print(
                f"  raw cluster {raw_id} (score 평균 {score_means[raw_id]:.2f}) "
                f"→ rank {rank} [{label}]"
            )

        meta = {
            "trained_at":      now,
            "data_count":      len(df),
            "cluster_summary": _build_cluster_summary(feature_df, df, cluster_rank_map),
        }

        joblib.dump({
            'model':           kmeans,
            'scaler':          scaler,
            'cluster_rank':    cluster_rank_map,
            'feature_weights': weights_arr.tolist(),
            'feature_names':   col_names,
            'meta':            meta,
        }, MODEL_PATH)
        print(f"\n[OK] 학습 완료 및 모델 업데이트 성공! (총 학습 데이터: {len(df)}개)")

    except Exception as e:
        print(f"\n[ERROR] ML 학습 중 에러 발생: {e}")