"""
server/ml/policy_trainer.py
─────────────────────────────────────────────────────────────
Contextual Bandit 의 RandomForestRegressor 정책을 주기 재학습.

학습 입력:
    feature : context_vec (8차원) ⊕ hint_one_hot
    target  : reward (0.0~1.0)

학습 데이터:
    code_logs.reward IS NOT NULL 인 행에서
    이전 제출의 컨텍스트와 hint_type 을 묶어 (feature, target) 샘플을 생성.

데이터 부족(_MIN_TRAINING_SAMPLES 미만) → 학습 스킵, 기존 모델 유지.
정책 파일은 main.py 의 ContextualBandit 이 mtime 차이로 자동 핫리로드합니다.

ml_worker.py 가 KMeans 학습 직후 본 모듈의 train_policy() 를 호출합니다.
"""

import math
import os
import sys
import joblib
import numpy as np
from datetime import datetime

import pymysql

# 상위 폴더(server) 모듈 경로 보정 — kmeans_trainer.py 와 동일 방식
sys.path.append(os.path.dirname(os.path.abspath(os.path.dirname(__file__))))
from config import DB_CONFIG  # noqa: E402

from ml.context_encoder import CONTEXT_DIM  # noqa: E402

# ── 학습 하이퍼파라미터 ───────────────────────────────────
_MIN_TRAINING_SAMPLES = 100   # reward 가 기록된 (이전, 현재) 쌍이 이 수 이상이어야 학습
_MODEL_FILE_NAME      = "code_policy_model.pkl"
_RANDOM_STATE         = 42

# context_encoder._HISTORY_LIMIT 과 동일 (운영 인코더 / 학습 인코더 일치성)
_CONTEXT_WINDOW = 5

_BASE_DIR  = os.path.dirname(os.path.abspath(os.path.dirname(__file__)))
MODEL_PATH = os.path.join(_BASE_DIR, _MODEL_FILE_NAME)


def _mean(xs: list[float]) -> float:
    return (sum(xs) / len(xs)) if xs else 0.0


def _std(xs: list[float]) -> float:
    if len(xs) < 2:
        return 0.0
    m = _mean(xs)
    return math.sqrt(sum((x - m) ** 2 for x in xs) / len(xs))


def _trailing_run_length(ranks: list[int]) -> int:
    """리스트 뒤쪽(가장 최근)에서 동일 rank 가 연속된 횟수."""
    if not ranks:
        return 0
    head = ranks[-1]
    count = 0
    for r in reversed(ranks):
        if r == head:
            count += 1
        else:
            break
    return count


def _build_context_for_log(rows_window: list[dict]) -> list[float]:
    """
    학습 시 컨텍스트 재구성 — 한 유저의 시점별 context_vec 을 추정합니다.

    rows_window 는 created_at 오름차순(과거→현재) 의 부분 리스트로
    마지막 원소가 "현재 학습 샘플의 직전 시점" 이어야 합니다.
    벡터 정의는 ml/context_encoder.encode_user_context() 와 동일합니다.
    """
    if not rows_window:
        return [0.0] * CONTEXT_DIM

    scores     = [float(r["score"]) for r in rows_window if r.get("score") is not None]
    ranks      = [int(r["cluster_rank"]) for r in rows_window
                  if r.get("cluster_rank") is not None and r["cluster_rank"] >= 0]
    success    = [int(bool(r.get("is_success", 0))) for r in rows_window]
    complexity = [float(r["ast_complexity"]) for r in rows_window
                  if r.get("ast_complexity") is not None]

    rank_span = (max(ranks) - min(ranks)) if len(ranks) >= 2 else 0

    return [
        _mean(scores),
        _std(scores),
        _mean(success),
        _mean(ranks),
        float(rank_span),
        _mean(complexity),
        len(rows_window) / float(_CONTEXT_WINDOW),
        float(_trailing_run_length(ranks)),
    ]


def _collect_training_samples():
    """
    DB 에서 (context, hint_type, reward) 쌍을 수집합니다.

    Returns:
        (X, y, hint_index)
            X          : np.ndarray  (n_samples, CONTEXT_DIM + n_hints)
            y          : np.ndarray  (n_samples,)
            hint_index : list[str]   one-hot 컬럼 매핑
    """
    conn = pymysql.connect(**DB_CONFIG)
    try:
        cursor = conn.cursor()
        # reward 가 기록된 모든 제출(=직전 힌트에 대한 평가) 을 가져옴
        cursor.execute(
            """
            SELECT user_pk, hint_type, reward, score, cluster_rank,
                   is_success, ast_complexity, created_at
            FROM   code_logs
            WHERE  reward IS NOT NULL AND hint_type IS NOT NULL
            ORDER  BY user_pk ASC, created_at ASC
            """
        )
        all_rows = cursor.fetchall() or []
    finally:
        conn.close()

    if len(all_rows) < _MIN_TRAINING_SAMPLES:
        return None, None, None

    # 유저별 시계열 묶음
    by_user: dict[int, list[dict]] = {}
    for r in all_rows:
        by_user.setdefault(r["user_pk"], []).append(r)

    samples_X: list[list[float]] = []
    samples_y: list[float]       = []
    samples_h: list[str]         = []

    for rows in by_user.values():
        # rows 는 시간순 정렬됨. 각 행의 reward 는 "직전 힌트(hint_type)" 에 대한 평가
        for i, row in enumerate(rows):
            ht = row["hint_type"]
            r  = row["reward"]
            if ht is None or r is None:
                continue
            window = rows[max(0, i - _CONTEXT_WINDOW):i]
            samples_X.append(_build_context_for_log(window))
            samples_y.append(float(r))
            samples_h.append(ht)

    if len(samples_X) < _MIN_TRAINING_SAMPLES:
        return None, None, None

    hint_index = sorted(set(samples_h))
    h_to_idx   = {h: i for i, h in enumerate(hint_index)}

    # one-hot 결합
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
        print(f"[Policy] 학습 데이터 부족 (필요 ≥ {_MIN_TRAINING_SAMPLES}) — 스킵")
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
            "model":       model,
            "hint_index":  hint_index,
            "context_dim": CONTEXT_DIM,
            "trained_at":  now,
            "n_samples":   int(X.shape[0]),
        }, MODEL_PATH)
        print(
            f"[Policy] 학습 완료 — n_samples={X.shape[0]} | "
            f"hint_count={len(hint_index)} | path={MODEL_PATH}"
        )
        return True
    except Exception as e:
        print(f"[Policy] 학습 실패: {e}")
        return False
