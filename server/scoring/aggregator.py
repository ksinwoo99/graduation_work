"""
server/scoring/aggregator.py
─────────────────────────────────────────────────────────────
다차원 점수 벡터를 단일 final_score 로 결합합니다.

전체 점수 = base*0.50 + personal*0.20 + adoption*0.15 - antipattern_pen*0.15
            (antipattern_pen 만 음의 기여)
          + BASE_SCORE_BUFF (정상 코드 과소평가 보정)

각 서브스코어 범위:
    base            0~100 — 기존 calculate_score()
    personal_delta  0~100 — 개인 히스토리 대비 성장 z-score
    adoption        0~100 — 직전 힌트의 AST 채택 정도
    antipattern_pen 0~100+ (clip) — 안티패턴 감점 합계

clip: 최종 결과는 0~100 으로 클램프 후 소수점 둘째 자리 반올림.
      BASE_SCORE_BUFF 적용 후에도 100점을 초과하지 않도록 보장합니다.
"""

SCORE_WEIGHTS: dict[str, float] = {
    "base":        0.50,   # 현재 calculate_score (교육 목표 기반)
    "personal":    0.20,   # 개인 히스토리 대비 성장 델타
    "adoption":    0.15,   # 직전 힌트 채택 정도
    "antipattern": 0.15,   # 안티패턴 감점 (음수 기여)
}

# 신규 유저의 personal/adoption 이 50(중립) 으로 고정되면서 가중합 상한이 ~85 로 묶이는
# 구조적 과소평가를 보정하기 위한 상수. 0~100 클립으로 만점은 그대로 유지됩니다.
BASE_SCORE_BUFF: float = 10.0


def final_score(base: float, personal_delta: float,
                adoption: float, antipattern_pen: float) -> float:
    """
    다차원 가중합으로 최종 점수를 계산합니다.

    Args:
        base            : calculate_score() 의 기본 점수 (0~100)
        personal_delta  : personal_delta_score() 의 개인 성장 점수 (0~100)
        adoption        : compute_adoption() 의 직전 힌트 채택도 (0~100)
        antipattern_pen : antipattern_penalty() 의 감점 합계 (0~100, 그 이상은 clip)

    Returns:
        0.0~100.0 사이의 float (소수점 둘째 자리 반올림)
    """
    raw = (
        SCORE_WEIGHTS["base"]        * base
        + SCORE_WEIGHTS["personal"]    * personal_delta
        + SCORE_WEIGHTS["adoption"]    * adoption
        - SCORE_WEIGHTS["antipattern"] * antipattern_pen
        + BASE_SCORE_BUFF
    )
    return round(max(0.0, min(100.0, raw)), 2)


def score_breakdown(base: float, personal_delta: float,
                    adoption: float, antipattern_pen: float) -> dict:
    """
    각 서브스코어의 가중 기여도를 분해해 반환합니다.
    /api/score_breakdown 엔드포인트 응답용.
    """
    contrib_base        = SCORE_WEIGHTS["base"]        * base
    contrib_personal    = SCORE_WEIGHTS["personal"]    * personal_delta
    contrib_adoption    = SCORE_WEIGHTS["adoption"]    * adoption
    contrib_antipattern = -SCORE_WEIGHTS["antipattern"] * antipattern_pen
    raw = (
        contrib_base + contrib_personal + contrib_adoption + contrib_antipattern
        + BASE_SCORE_BUFF
    )
    final = round(max(0.0, min(100.0, raw)), 2)

    return {
        "weights":      SCORE_WEIGHTS,
        "base_buff":    BASE_SCORE_BUFF,
        "subscores": {
            "base":             round(base,            2),
            "personal_delta":   round(personal_delta,  2),
            "adoption":         round(adoption,        2),
            "antipattern_pen":  round(antipattern_pen, 2),
        },
        "contributions": {
            "base":         round(contrib_base,        2),
            "personal":     round(contrib_personal,    2),
            "adoption":     round(contrib_adoption,    2),
            "antipattern":  round(contrib_antipattern, 2),
            "base_buff":    BASE_SCORE_BUFF,
        },
        "raw":   round(raw,   2),
        "final": final,
    }
