"""
hint_routing 시나리오 테스트 — 발표 전 smoke test.

실행 (server/ 디렉터리):
    python -m unittest tests.test_hint_routing -v
"""

from __future__ import annotations

import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from ml.hint_routing import (
    effective_hint_rank,
    filter_variants,
    hint_applicable,
    resolve_success_hint_group,
)
from utils import extract_features


def _feat(code: str) -> dict:
    return extract_features(code)


# ── 대표 제출 코드 스니펫 ─────────────────────────────────
CODE_RANGE = """\
for i in range(10):
    mining()
"""

CODE_WHILE_FINITE = """\
while resCommon < 100:
    mining()
"""

CODE_WHILE_TRUE = """\
while True:
    mining()
"""

CODE_WHILE_TRUE_BREAK = """\
while True:
    if resCommon < 10:
        break
    mining()
"""

CODE_WHILE_TRUE_BREAK_IF = """\
while True:
    if resCommon < 10:
        break
    if resCommon >= 50:
        mining()
"""

CODE_COUNT = """\
for i in count():
    mining()
"""

CODE_COUNT_IF = """\
for i in count():
    if i % 2 == 0:
        mining()
"""

CODE_COUNT_BREAK = """\
for i in count():
    mining()
    break
"""

CODE_COUNT_IF_BREAK = """\
for i in count():
    if resCommon < 10:
        break
    if resCommon >= 50:
        mining()
"""

CODE_SIMPLE = """\
mining()
producting()
"""


class TestEffectiveHintRank(unittest.TestCase):
    def test_rank1_infinite_loop_promoted_to_rank2(self):
        f = _feat(CODE_COUNT)
        self.assertEqual(effective_hint_rank(1, f), 2)

    def test_rank2_unchanged(self):
        f = _feat(CODE_COUNT)
        self.assertEqual(effective_hint_rank(2, f), 2)

    def test_rank1_finite_for_unchanged(self):
        f = _feat(CODE_RANGE)
        self.assertEqual(effective_hint_rank(1, f), 1)


class TestResolveSuccessHintGroup(unittest.TestCase):
    """cluster_rank + AST 피처 → 힌트 풀(group_key) 매핑."""

    def test_rank0_simple_no_loop(self):
        f = _feat(CODE_SIMPLE)
        self.assertEqual(resolve_success_hint_group(0, f), "succ_r0_simple")

    def test_rank1_for_range(self):
        f = _feat(CODE_RANGE)
        self.assertEqual(resolve_success_hint_group(1, f), "succ_r1_for")

    def test_rank1_while_finite(self):
        f = _feat(CODE_WHILE_FINITE)
        self.assertEqual(resolve_success_hint_group(1, f), "succ_r1_while")

    def test_rank1_mastery_when_already_infinite(self):
        f = _feat(CODE_COUNT)
        self.assertEqual(resolve_success_hint_group(1, f), "succ_r1_mastery")

    def test_rank2_for_range_high_tier(self):
        f = _feat(CODE_RANGE)
        self.assertEqual(resolve_success_hint_group(2, f), "succ_r2_for_range")

    def test_rank2_while_true_with_break_goes_to_mastery_when_if_present(self):
        # break 조건용 if 가 루프 안에 있으면 if+break 모두 갖춘 것으로 처리
        f = _feat(CODE_WHILE_TRUE_BREAK)
        self.assertEqual(resolve_success_hint_group(2, f), "succ_r2_while_mastery")

    def test_rank2_while_true_no_if_suggest_break(self):
        f = _feat(CODE_WHILE_TRUE)
        self.assertEqual(
            resolve_success_hint_group(2, f, antipattern_tags=["infinite_no_break"]),
            "succ_r2_while_add_break",
        )

    def test_rank2_while_mastery_with_break_and_if(self):
        f = _feat(CODE_WHILE_TRUE_BREAK_IF)
        self.assertEqual(resolve_success_hint_group(2, f), "succ_r2_while_mastery")

    def test_rank2_count_suggest_break(self):
        f = _feat(CODE_COUNT)
        self.assertEqual(resolve_success_hint_group(2, f), "succ_r2_count_add_break")

    def test_rank2_count_if_mastery_without_break(self):
        f = _feat(CODE_COUNT_IF)
        self.assertEqual(resolve_success_hint_group(2, f), "succ_r2_count_if_mastery")

    def test_rank2_count_with_break_only_suggest_if(self):
        f = _feat(CODE_COUNT_BREAK)
        self.assertEqual(resolve_success_hint_group(2, f), "succ_r2_count_add_if")

    def test_rank2_count_break_with_if_goes_ceiling(self):
        f = _feat(CODE_COUNT_IF_BREAK)
        self.assertEqual(resolve_success_hint_group(2, f), "succ_r2_count_ceiling")


class TestFeatureExtraction(unittest.TestCase):
    """extract_features 가 시나리오별 플래그를 올바르게 세팅하는지."""

    def test_count_sets_infinite_for(self):
        f = _feat(CODE_COUNT)
        self.assertEqual(f["has_infinite_for"], 1)
        self.assertEqual(f["has_infinite_loop"], 1)
        self.assertEqual(f["uses_itertools"], 1)

    def test_range_not_infinite(self):
        f = _feat(CODE_RANGE)
        self.assertEqual(f["has_infinite_for"], 0)
        self.assertEqual(f["has_infinite_loop"], 0)
        self.assertGreater(f["max_range_n"], 0)

    def test_while_true_infinite(self):
        f = _feat(CODE_WHILE_TRUE)
        self.assertEqual(f["has_infinite_while"], 1)
        self.assertEqual(f["has_break_in_loop"], 0)

    def test_count_if_break_flags(self):
        f = _feat(CODE_COUNT_IF_BREAK)
        self.assertEqual(f["has_if_inside_loop"], 1)
        self.assertEqual(f["has_break_in_loop"], 1)


class TestFilterVariants(unittest.TestCase):
    """이미 달성한 upsell 변형은 filter_variants 로 제외."""

    def test_rank1_for_all_blocked_when_already_infinite(self):
        f = _feat(CODE_COUNT)
        variants = filter_variants("succ_r1_for", f)
        self.assertEqual(len(variants), 0)

    def test_rank2_for_range_blocks_infinite_upsell_when_infinite(self):
        f = _feat(CODE_COUNT)
        variants = filter_variants("succ_r2_for_range", f)
        hint_types = {ht for _, ht in variants}
        self.assertEqual(len(hint_types), 0)

    def test_rank2_count_add_break_blocked_when_break_exists(self):
        f = _feat(CODE_COUNT_BREAK)
        self.assertFalse(hint_applicable("succ_r2_count_add_break_A", f))

    def test_rank2_while_add_break_blocked_when_break_exists(self):
        f = _feat(CODE_WHILE_TRUE_BREAK)
        variants = filter_variants("succ_r2_while_add_break", f)
        self.assertEqual(len(variants), 0)

    def test_rank0_simple_blocks_when_loop_exists(self):
        f = _feat(CODE_RANGE)
        variants = filter_variants("succ_r0_simple", f)
        self.assertEqual(len(variants), 0)


class TestEndToEndHintPipeline(unittest.TestCase):
    """effective_hint_rank → resolve → filter 파이프라인."""

    def _pipeline(self, code: str, cluster_rank: int, tags: list[str] | None = None):
        f = _feat(code)
        hint_rank = effective_hint_rank(cluster_rank, f)
        group = resolve_success_hint_group(hint_rank, f, tags)
        variants = filter_variants(group, f)
        return hint_rank, group, variants

    def test_misclassified_infinite_at_rank1_gets_rank2_pool(self):
        hint_rank, group, variants = self._pipeline(CODE_COUNT, cluster_rank=1)
        self.assertEqual(hint_rank, 2)
        self.assertEqual(group, "succ_r2_count_add_break")
        self.assertGreater(len(variants), 0)
        texts = " ".join(t for t, _ in variants)
        self.assertNotIn("count():", texts.lower())  # count upsell 금지

    def test_range_at_rank1_still_offers_count_upsell(self):
        _, group, variants = self._pipeline(CODE_RANGE, cluster_rank=1)
        self.assertEqual(group, "succ_r1_for")
        hint_types = {ht for _, ht in variants}
        self.assertIn("succ_r1_for_B", hint_types)

    def test_while_true_at_rank2_has_actionable_hint(self):
        _, group, variants = self._pipeline(
            CODE_WHILE_TRUE, cluster_rank=2, tags=["infinite_no_break"],
        )
        self.assertEqual(group, "succ_r2_while_add_break")
        self.assertGreater(len(variants), 0)


if __name__ == "__main__":
    unittest.main()
