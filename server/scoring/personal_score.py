"""
server/scoring/personal_score.py
─────────────────────────────────────────────────────────────
개인 성장 점수(personal_delta_score).

같은 base=70 이라도
    평균 60 짜리 유저에겐 "성장",
    평균 85 짜리 유저에겐 "퇴보"
로 평가되도록, 최근 N개 성공 제출의 z-score 를 0~100 으로 압축합니다.

데이터 부족(prev < 3)은 50.0 (중립) 폴백.
DB 컬럼 누락 등 어떤 예외에도 50.0 을 반환해 호출 측 안전을 보장합니다.
"""

import math


# 최근 N개 성공 제출의 base score 를 기준으로 사용
_HISTORY_LIMIT = 10
# tanh 압축 시의 분모(σ scale). 1.5 → ±3σ 가 거의 0~100 양 끝에 매핑
_TANH_SCALE = 1.5


def personal_delta_score(cursor, user_pk: int, cur_base: float) -> float:
    """
    유저의 최근 N개 성공 제출(base score) 대비
    현재 제출의 위치를 z-score 로 환산해 0~100 으로 반환합니다.

    Args:
        cursor   : pymysql DictCursor (외부에서 주입, 트랜잭션 공유)
        user_pk  : 유저 PK
        cur_base : 현재 제출의 base score (calculate_score 결과)

    Returns:
        0.0 (퇴보) ~ 100.0 (큰 성장). 데이터 부족 시 50.0.
    """
    try:
        cursor.execute(
            "SELECT score FROM code_logs "
            "WHERE user_pk=%s AND is_success=1 "
            "ORDER BY created_at DESC LIMIT %s",
            (user_pk, _HISTORY_LIMIT),
        )
        rows = cursor.fetchall() or []
    except Exception:
        return 50.0

    prev_scores = [float(r["score"]) for r in rows if r.get("score") is not None]
    if len(prev_scores) < 3:
        return 50.0

    mean = sum(prev_scores) / len(prev_scores)
    var  = sum((x - mean) ** 2 for x in prev_scores) / len(prev_scores)
    std  = math.sqrt(var) or 1.0

    z = (cur_base - mean) / std
    # tanh 로 -∞~+∞ → -1~+1 압축, 다시 0~100 매핑
    return round(50.0 + 50.0 * math.tanh(z / _TANH_SCALE), 2)
