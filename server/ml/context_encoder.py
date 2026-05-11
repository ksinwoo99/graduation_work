"""
server/ml/context_encoder.py
─────────────────────────────────────────────────────────────
유저 상태(컨텍스트) 8차원 인코더.

같은 그룹(succ_r0_simple 등) 내에서도 사용자의 숙련도·성장세·실패율에
따라 최적 힌트가 달라지므로, 사용자 최근 N개 제출을 8차원 벡터로 압축해
ContextualBandit 의 입력으로 사용합니다.

Transformer / DNN 없이 통계 기반으로만 구성 — DB 1쿼리, O(N).
DB 컬럼 누락 / 데이터 부족 시 0 벡터 폴백.
"""

import math


# context vector 차원 수 (인코더 결과의 길이)
CONTEXT_DIM = 8

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


def encode_user_context(cursor, user_pk: int) -> list[float]:
    """
    유저의 최근 N개 제출을 8차원 벡터로 인코딩합니다.

    벡터 구성:
        [0] 평균 score             — 평균 실력
        [1] score 표준편차          — 변동성
        [2] 평균 is_success         — 최근 성공률
        [3] 평균 cluster_rank       — 평균 패턴 등급
        [4] rank 폭(max-min)        — 패턴 다양성
        [5] 평균 ast_complexity     — 평균 복잡도
        [6] 표본 수 / _HISTORY_LIMIT— 데이터 신뢰도
        [7] 최근 동일 rank 연속 횟수— 정체 감지

    DB 컬럼 누락 / 데이터 없음 시: [0]*8 반환.
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
        return [0.0] * CONTEXT_DIM

    if not rows:
        return [0.0] * CONTEXT_DIM

    scores      = [float(r["score"]) for r in rows if r.get("score") is not None]
    ranks_all   = [int(r["cluster_rank"]) for r in rows if r.get("cluster_rank") is not None]
    ranks       = [r for r in ranks_all if r >= 0]
    success     = [int(bool(r.get("is_success", 0))) for r in rows]
    complexity  = [float(r["ast_complexity"])
                   for r in rows if r.get("ast_complexity") is not None]

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
