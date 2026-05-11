"""
server/ml/reward.py
─────────────────────────────────────────────────────────────
연속형 보상(reward shaping).

기존 밴딧은 success/failure 이진값만 사용해
"힌트를 따랐다" 와 "우연히 성공" 을 구분하지 못했습니다.
여기서는 0.0~1.0 의 연속값으로 보상을 산출해
Thompson Sampling 의 Beta 업데이트 입력으로 사용합니다.

판정 규칙(우선순위):
    1. 같은 종류의 에러 재발     → 0.0  (힌트 완전 무시)
    2. 에러 → 성공 전환          → 1.0  (힌트가 문제 해결을 도움)
    3. 성공 간 이동              → 0.0~1.0 (AST 채택도 + rank 보너스)
"""

from scoring.hint_adoption import compute_adoption


def compute_reward(
    prev_feat:        dict,
    cur_feat:         dict,
    hint_type:        str | None,
    prev_rank:        int,
    cur_rank:         int,
    prev_was_error:   bool,
    cur_is_success:   bool,
    cur_error_type:   str | None,
    prev_error_type:  str | None,
) -> float:
    """
    Args:
        prev_feat        : 직전 제출의 extract_features() 결과
        cur_feat         : 현재 제출의 extract_features() 결과
        hint_type        : 직전 제출에 표시되었던 hint_type (None 가능)
        prev_rank        : 직전 제출의 cluster_rank (-1 가능)
        cur_rank         : 현재 제출의 cluster_rank (-1 가능)
        prev_was_error   : 직전 제출이 실패였는지
        cur_is_success   : 현재 제출이 성공인지
        cur_error_type   : 현재 제출의 에러 타입 (성공이면 None)
        prev_error_type  : 직전 제출의 에러 타입 (성공이면 None)

    Returns:
        0.0 ~ 1.0 연속값.
        hint_type 이 None 이면 (이전 힌트 정보 없음) 0.5 (중립) 반환.
    """
    if not hint_type:
        return 0.5

    # ── (1) 강한 부정: 같은 에러 재발 ───────────────────────
    if cur_error_type and prev_error_type and cur_error_type == prev_error_type:
        return 0.0

    # ── (2) 강한 긍정: 에러 → 성공 전환 ────────────────────
    if prev_was_error and cur_is_success:
        return 1.0

    # ── (3) 성공 간 이동: AST 채택도 + rank 보너스 ─────────
    adoption = compute_adoption(prev_feat, cur_feat, hint_type) / 100.0  # 0.0~1.0

    rank_bonus = 0.0
    if prev_rank >= 0 and cur_rank >= 0:
        if cur_rank > prev_rank:
            rank_bonus = +0.2
        elif cur_rank < prev_rank:
            rank_bonus = -0.2

    reward = adoption + rank_bonus
    return round(max(0.0, min(1.0, reward)), 4)
