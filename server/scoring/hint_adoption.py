"""
server/scoring/hint_adoption.py
─────────────────────────────────────────────────────────────
힌트 채택도(Hint Adoption Score).

각 hint_type 에 "장려하는 피처 변화 방향"을 태그하고,
직전 → 현재 제출의 피처 벡터가 그 방향과 얼마나 일치하는지를
0.0~100.0 으로 수치화합니다.

이 값은
    ① 현재 제출의 final_score 의 한 축(adoption)으로 사용되고
    ② Part 3 의 reward shaping 에서 reward 의 기본 입력이 됩니다.

핵심 아이디어:
    direction =  +1 → 해당 피처가 "증가"하면 힌트를 따른 것
    direction =  -1 → 해당 피처가 "감소"하면 힌트를 따른 것
    direction =   0 → 해당 피처는 무관

기준 피처 키는 utils.extract_features() 가 반환하는 키와 동일합니다.
"""

# hint_type → {feature_key: target_direction(+1 / -1)}
HINT_TARGET_VECTORS: dict[str, dict[str, int]] = {
    # ── rank 0: 단순 코드형 → 루프 도입 유도 ──────────
    "succ_r0_simple_A":  {"has_loop": +1, "loop_efficiency": +1},
    "succ_r0_simple_B":  {"has_loop": +1, "loop_efficiency": +1},
    "succ_r0_has_if_A":  {"has_loop": +1},
    "succ_r0_has_if_B":  {"has_infinite_while": +1},

    # ── rank 1: 일반 학습자형 → while True / 효율 강화 유도 ──
    "succ_r1_for_A":     {"loop_efficiency": +1, "has_infinite_while": +1},
    "succ_r1_for_B":     {"has_infinite_while": +1},
    "succ_r1_while_A":   {"has_infinite_while": +1},
    "succ_r1_while_B":   {"has_infinite_while": +1},

    # ── rank 2: 효율 최적화형 → 유지가 곧 성공 (방향 유지)
    "succ_r2_for":       {"has_loop": +1, "loop_efficiency": +1},
    "succ_r2_infinite":  {"has_infinite_while": +1, "loop_efficiency": +1},

    # err_* / machine_* 힌트류는 별도 시그널(에러 해결 여부)로 평가하므로
    # 여기서는 빈 매핑(중립). reward shaping 에서 prev_was_error → cur_is_success
    # 전이를 강한 긍정 신호로 사용합니다.
}


def _sign(x: float) -> int:
    """numpy.sign 의 경량 대체 — 외부 의존 없이 동작."""
    if x > 0:
        return +1
    if x < 0:
        return -1
    return 0


def compute_adoption(prev_feat: dict, cur_feat: dict, hint_type: str | None) -> float:
    """
    힌트 방향과 피처 변화 방향의 일치도를 0~100 으로 반환합니다.

    Args:
        prev_feat : 직전 제출의 extract_features() 결과
        cur_feat  : 현재 제출의 extract_features() 결과
        hint_type : 직전 제출에 표시된 hint_type (없으면 None)

    Returns:
        0.0 (정반대) / 50.0 (중립·무관) / 100.0 (완전 일치)
    """
    if not hint_type:
        return 50.0

    target = HINT_TARGET_VECTORS.get(hint_type)
    if not target:
        return 50.0   # err_* / machine_* / 미정의 — 중립

    if not prev_feat or not cur_feat:
        return 50.0

    alignment    = 0.0
    keys_checked = 0
    for key, direction in target.items():
        try:
            delta = float(cur_feat.get(key, 0)) - float(prev_feat.get(key, 0))
        except (TypeError, ValueError):
            continue
        # +1: 방향 일치, -1: 방향 반대, 0: 변화 없음
        alignment    += _sign(delta) * direction
        keys_checked += 1

    if keys_checked == 0:
        return 50.0

    normalized = alignment / keys_checked     # -1.0 ~ +1.0
    return round(50.0 + 50.0 * normalized, 2)  # 0.0 ~ 100.0
