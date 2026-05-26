"""
server/ml/context_encoder.py
─────────────────────────────────────────────────────────────
유저 상태(컨텍스트) 인코더.

v1 (8차원): 최근 N개 제출 통계만
v2 (14차원): v1 + 현재 제출 AST 피처 6개 (밴딧 선택 시점 코드 상태)

ContextualBandit / policy_trainer 는 CONTEXT_VERSION·CONTEXT_DIM 으로
구 pkl(8차원)과 자동 분리 — 차원 불일치 시 Thompson 폴백.
"""

import math

# ── 버전 / 차원 ───────────────────────────────────────────
CONTEXT_VERSION = 2
_HISTORY_DIM = 8
_SUBMISSION_FEATURE_KEYS = (
    "has_loop",
    "has_infinite_loop",
    "has_infinite_for",
    "has_infinite_while",
    "has_if_inside_loop",
    "has_break_in_loop",
)
CONTEXT_DIM = _HISTORY_DIM + len(_SUBMISSION_FEATURE_KEYS)  # 14

# 인코딩에 사용할 최근 제출 수
_HISTORY_LIMIT = 5


def _safe_mean(xs: list[float]) -> float:
    if not xs:
        return 0.0
    return sum(xs) / len(xs)


def _safe_std(xs: list[float]) -> float:
    if len(xs) < 2:
        return 0.0
    m = _safe_mean(xs)
    var = sum((x - m) ** 2 for x in xs) / len(xs)
    return math.sqrt(var)


def _consecutive_same_rank(ranks: list[int]) -> int:
    """
    리스트 맨 앞(가장 최근)의 rank 와 같은 값이 연속으로 몇 개 있는지 반환.
    정체(stagnation) 신호.
    """
    if not ranks:
        return 0
    head = ranks[0]
    count = 0
    for r in ranks:
        if r == head:
            count += 1
        else:
            break
    return count


def encode_user_history(rows: list[dict]) -> list[float]:
    """
    최근 제출 row 리스트(DESC: 최신 먼저) → 8차원 히스토리 벡터.

    벡터 구성:
        [0] 평균 score             — 평균 실력
        [1] score 표준편차          — 변동성
        [2] 평균 is_success         — 최근 성공률
        [3] 평균 cluster_rank       — 평균 패턴 등급
        [4] rank 폭(max-min)        — 패턴 다양성
        [5] 평균 ast_complexity     — 평균 복잡도
        [6] 표본 수 / _HISTORY_LIMIT— 데이터 신뢰도
        [7] 최근 동일 rank 연속 횟수— 정체 감지
    """
    if not rows:
        return [0.0] * _HISTORY_DIM

    scores = [float(r["score"]) for r in rows if r.get("score") is not None]
    ranks_all = [int(r["cluster_rank"]) for r in rows if r.get("cluster_rank") is not None]
    ranks = [r for r in ranks_all if r >= 0]
    success = [int(bool(r.get("is_success", 0))) for r in rows]
    complexity = [
        float(r["ast_complexity"])
        for r in rows
        if r.get("ast_complexity") is not None
    ]

    rank_span = (max(ranks) - min(ranks)) if len(ranks) >= 2 else 0

    return [
        _safe_mean(scores),
        _safe_std(scores),
        _safe_mean(success),
        _safe_mean(ranks),
        float(rank_span),
        _safe_mean(complexity),
        len(rows) / float(_HISTORY_LIMIT),
        float(_consecutive_same_rank(ranks)),
    ]


def encode_submission_features(features: dict | None) -> list[float]:
    """현재 제출 AST 피처 6개 → 0/1 float 벡터."""
    f = features or {}
    return [float(bool(f.get(k, 0))) for k in _SUBMISSION_FEATURE_KEYS]


def build_context(
    history_rows: list[dict],
    submission_features: dict | None = None,
) -> list[float]:
    """
    히스토리 8차원 + 현재 제출 6차원 = 14차원 컨텍스트.

    history_rows: created_at DESC (최신 먼저) — encode_user_context 와 동일.
    """
    history = encode_user_history(history_rows)
    submission = encode_submission_features(submission_features)
    return history + submission


def encode_user_context(cursor, user_pk: int, submission_features: dict | None = None) -> list[float]:
    """
    DB에서 유저 최근 N개 제출을 읽어 14차원 컨텍스트를 반환합니다.

    submission_features: 현재 제출 extract_features() 결과 (없으면 0 벡터).
    DB 컬럼 누락 / 데이터 없음 시: 히스토리는 0, 제출 피처만 반영.
    """
    try:
        cursor.execute(
            "SELECT score, cluster_rank, is_success, ast_complexity "
            "FROM code_logs "
            "WHERE user_pk=%s "
            "ORDER BY created_at DESC LIMIT %s",
            (user_pk, _HISTORY_LIMIT),
        )
        rows = cursor.fetchall() or []
    except Exception:
        rows = []

    return build_context(rows, submission_features)
