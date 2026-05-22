"""
server/ml/hint_routing.py
─────────────────────────────────────────────────────────────
성공 힌트 풀 선택 + 현재 코드 피처 기반 변형 필터.

cluster_rank(KMeans) 와 별도로 코드 AST 피처로 힌트 그룹을 정하고,
이미 달성한 목표(무한 for 등)를 다시 권하는 변형은 제외합니다.
"""

from __future__ import annotations

from user_messages import HINT_VARIANTS

# hint_type → features[key] 가 truthy 이면 해당 변형 제외
HINT_BLOCK_WHEN: dict[str, list[str]] = {
    # rank 0 — 이미 루프 사용 중이면 기초 upsell 불필요
    "succ_r0_simple_A": ["has_loop"],
    "succ_r0_simple_B": ["has_loop"],
    "succ_r0_has_if_A": ["has_loop"],
    # rank 1 — 무한 루프 upsell (이미 달성 시)
    "succ_r1_for_A": ["has_infinite_loop"],
    "succ_r1_for_B": ["has_infinite_for", "has_infinite_while", "has_infinite_loop"],
    "succ_r1_while_A": ["has_infinite_while", "has_infinite_loop"],
    "succ_r1_while_B": ["has_infinite_while", "has_infinite_loop"],
    # rank 2 — 유한 루프 풀에서 무한 upsell
    "succ_r2_for_range_A": ["has_infinite_loop"],
    "succ_r2_for_range_B": ["has_infinite_for", "has_infinite_while", "has_infinite_loop"],
    # rank 2 — break / if 이미 있으면 '추가하세요' 힌트 제외
    "succ_r2_infinite_while_A": ["has_break_in_loop"],
    "succ_r2_infinite_while_B": ["has_break_in_loop"],
    "succ_r2_infinite_for_count_B": ["has_break_in_loop"],
    "succ_r2_while_add_break_A": ["has_break_in_loop"],
    "succ_r2_while_add_break_B": ["has_break_in_loop"],
    "succ_r2_count_add_break_A": ["has_break_in_loop"],
    "succ_r2_count_add_if_A": ["has_if_inside_loop"],
    "succ_r2_while_add_if_A": ["has_if_inside_loop"],
}


def hint_applicable(hint_type: str, features: dict) -> bool:
    for key in HINT_BLOCK_WHEN.get(hint_type, []):
        if features.get(key):
            return False
    return True


def filter_variants(group_key: str, features: dict) -> list[tuple[str, str]]:
    raw = HINT_VARIANTS.get(group_key, [])
    return [(text, ht) for text, ht in raw if hint_applicable(ht, features)]


def resolve_success_hint_group(
    cluster_rank: int,
    features: dict,
    antipattern_tags: list[str] | None = None,
) -> str:
    """
    KMeans cluster_rank + 현재 제출 AST 피처로 힌트 변형 풀(group_key) 결정.
    """
    tags = antipattern_tags or []

    has_inf_for = bool(features.get("has_infinite_for"))
    has_inf_while = bool(features.get("has_infinite_while"))
    has_inf = bool(features.get("has_infinite_loop"))
    has_break = bool(features.get("has_break_in_loop"))
    has_if_loop = bool(features.get("has_if_inside_loop"))
    no_break_antipattern = "infinite_no_break" in tags

    if cluster_rank == 0:
        return "succ_r0_has_if" if features.get("if_count", 0) > 0 else "succ_r0_simple"

    if cluster_rank == 1:
        if has_inf:
            return "succ_r1_mastery"
        if features.get("while_count", 0) > 0 and not has_inf_while:
            return "succ_r1_while"
        return "succ_r1_for"

    if cluster_rank == 2:
        if has_inf_for:
            if has_if_loop and has_break:
                return "succ_r2_count_ceiling"
            if has_if_loop:
                return "succ_r2_count_if_mastery"
            if no_break_antipattern or not has_break:
                return "succ_r2_count_add_break"
            return "succ_r2_count_add_if"

        if has_inf_while:
            if no_break_antipattern or not has_break:
                return "succ_r2_while_add_break"
            if not has_if_loop:
                return "succ_r2_while_add_if"
            return "succ_r2_while_mastery"

        return "succ_r2_for_range"

    return "succ_r2_ceiling"


def effective_hint_rank(cluster_rank: int, features: dict) -> int:
    """KMeans 오분류 보정 — 무한 루프인데 rank 1 이면 힌트는 rank 2 풀 사용."""
    if cluster_rank == 1 and features.get("has_infinite_loop"):
        return 2
    return cluster_rank
