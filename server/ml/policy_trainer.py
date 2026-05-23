"""
server/ml/policy_trainer.py
─────────────────────────────────────────────────────────────
Contextual Bandit 의 RandomForestRegressor 정책을 주기 재학습.

학습 입력:
    feature : context_vec (14차원 = 히스토리 8 + 현재 제출 6) ⊕ hint_one_hot
    target  : reward (0.0~1.0)

학습 데이터:
    code_logs.reward IS NOT NULL 인 행에서
    직전 제출의 컨텍스트·hint_type·현재 제출 source_code 로 샘플 생성.

데이터 부족(_MIN_TRAINING_SAMPLES 미만) → 학습 스킵, 기존 모델 유지.
정책 파일은 main.py 의 ContextualBandit 이 mtime 차이로 자동 핫리로드합니다.

ml_worker.py 가 KMeans 학습 직후 본 모듈의 train_policy() 를 호출합니다.
"""

import os
import sys
import joblib
import numpy as np
from datetime import datetime

import pymysql

# 상위 폴더(server) 모듈 경로 보정 — kmeans_trainer.py 와 동일 방식
sys.path.append(os.path.dirname(os.path.abspath(os.path.dirname(__file__))))
from config import DB_CONFIG  # noqa: E402

from ml.context_encoder import (  # noqa: E402
    CONTEXT_DIM,
    CONTEXT_VERSION,
    _HISTORY_LIMIT,
    build_context,
)
from utils import extract_features  # noqa: E402

# ── 학습 하이퍼파라미터 ───────────────────────────────────
_MIN_TRAINING_SAMPLES = 100   # reward 가 기록된 (이전, 현재) 쌍이 이 수 이상이어야 학습
_MODEL_FILE_NAME      = "code_policy_model.pkl"
_RANDOM_STATE         = 42

_BASE_DIR  = os.path.dirname(os.path.abspath(os.path.dirname(__file__)))
MODEL_PATH = os.path.join(_BASE_DIR, _MODEL_FILE_NAME)


def _collect_training_samples():
    """
    DB 에서 (context, hint_type, reward) 쌍을 수집합니다.

    reward 행 i 는 직전 제출(i-1)에 표시된 hint_type 에 대한 평가이므로
    hint_type ← rows[i-1], context ← rows[i-1] 이전 히스토리 + rows[i-1] 코드 피처.

    Returns:
        (X, y, hint_index)
            X          : np.ndarray  (n_samples, CONTEXT_DIM + n_hints)
            y          : np.ndarray  (n_samples,)
            hint_index : list[str]   one-hot 컬럼 매핑
    """
    conn = pymysql.connect(**DB_CONFIG)
    try:
        cursor = conn.cursor()
        cursor.execute(
            """
            SELECT user_pk, hint_type, reward, score, cluster_rank,
                   is_success, ast_complexity, created_at, source_code
            FROM   code_logs
            WHERE  reward IS NOT NULL
            ORDER  BY user_pk ASC, created_at ASC
            """
        )
        all_rows = cursor.fetchall() or []
    finally:
        conn.close()

    if len(all_rows) < _MIN_TRAINING_SAMPLES:
        return None, None, None

    by_user: dict[int, list[dict]] = {}
    for r in all_rows:
        by_user.setdefault(r["user_pk"], []).append(r)

    samples_X: list[list[float]] = []
    samples_y: list[float]       = []
    samples_h: list[str]         = []

    for rows in by_user.values():
        for i in range(1, len(rows)):
            reward_row = rows[i]
            hint_row   = rows[i - 1]
            ht = hint_row.get("hint_type")
            r  = reward_row.get("reward")
            if not ht or r is None:
                continue

            # hint_row 제출 직전 히스토리 (ASC → DESC 로 뒤집어 인코더와 동일)
            history_asc = rows[max(0, i - 1 - _HISTORY_LIMIT): i - 1]
            history_desc = list(reversed(history_asc))
            try:
                sub_features = extract_features(hint_row.get("source_code") or "")
            except Exception:
                sub_features = {}

            samples_X.append(build_context(history_desc, sub_features))
            samples_y.append(float(r))
            samples_h.append(ht)

    if len(samples_X) < _MIN_TRAINING_SAMPLES:
        return None, None, None

    hint_index = sorted(set(samples_h))
    h_to_idx   = {h: i for i, h in enumerate(hint_index)}

    X = np.zeros((len(samples_X), CONTEXT_DIM + len(hint_index)), dtype=float)
    for row_i, (ctx, ht) in enumerate(zip(samples_X, samples_h)):
        X[row_i, :CONTEXT_DIM] = ctx
        X[row_i, CONTEXT_DIM + h_to_idx[ht]] = 1.0

    y = np.asarray(samples_y, dtype=float)
    return X, y, hint_index


def train_policy() -> bool:
    """
    Contextual policy(RandomForestRegressor) 를 학습/저장합니다.

    Returns:
        True  학습 및 저장 성공
        False 데이터 부족 / 에러
    """
    now = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    print(f"\n{'=' * 40}")
    print(f"ContextualPolicy 학습 시작 ({now})")
    print(f"{'=' * 40}")

    X, y, hint_index = _collect_training_samples()
    if X is None:
        print(f"[Policy] 학습 데이터 부족 (필요 >= {_MIN_TRAINING_SAMPLES}) - 스킵")
        return False

    try:
        from sklearn.ensemble import RandomForestRegressor
        model = RandomForestRegressor(
            n_estimators=80,
            max_depth=8,
            min_samples_leaf=3,
            random_state=_RANDOM_STATE,
            n_jobs=-1,
        )
        model.fit(X, y)
        joblib.dump({
            "model":           model,
            "hint_index":      hint_index,
            "context_dim":     CONTEXT_DIM,
            "context_version": CONTEXT_VERSION,
            "trained_at":      now,
            "n_samples":       int(X.shape[0]),
        }, MODEL_PATH)
        print(
            f"[Policy] 학습 완료 - n_samples={X.shape[0]} | "
            f"hint_count={len(hint_index)} | context_dim={CONTEXT_DIM} | "
            f"context_version={CONTEXT_VERSION} | path={MODEL_PATH}"
        )
        return True
    except Exception as e:
        print(f"[Policy] 학습 실패: {e}")
        return False


if __name__ == "__main__":
    ok = train_policy()
    raise SystemExit(0 if ok else 1)
