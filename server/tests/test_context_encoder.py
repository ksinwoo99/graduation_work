"""
context_encoder v2 (14차원) + ContextualBandit Thompson 폴백 테스트.
"""

from __future__ import annotations

import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from ml.bandit_thompson import ThompsonBandit
from ml.context_encoder import (
    CONTEXT_DIM,
    CONTEXT_VERSION,
    build_context,
    encode_submission_features,
    encode_user_history,
)
from ml.contextual_policy import ContextualBandit
from utils import extract_features


class TestContextEncoderV2(unittest.TestCase):
    def test_history_dim_is_8(self):
        rows = [
            {"score": 80, "cluster_rank": 2, "is_success": 1, "ast_complexity": 5},
            {"score": 70, "cluster_rank": 1, "is_success": 1, "ast_complexity": 4},
        ]
        hist = encode_user_history(rows)
        self.assertEqual(len(hist), 8)

    def test_full_context_is_14(self):
        rows = [{"score": 80, "cluster_rank": 2, "is_success": 1, "ast_complexity": 5}]
        code = "for i in count():\n    mining()"
        ctx = build_context(rows, extract_features(code))
        self.assertEqual(len(ctx), CONTEXT_DIM)
        self.assertEqual(CONTEXT_DIM, 14)
        self.assertEqual(ctx[8], 1.0)   # has_loop
        self.assertEqual(ctx[9], 1.0)   # has_infinite_loop
        self.assertEqual(ctx[10], 1.0)  # has_infinite_for

    def test_submission_only_without_history(self):
        ctx = build_context([], extract_features("mining()"))
        self.assertEqual(ctx[:8], [0.0] * 8)
        self.assertEqual(encode_submission_features(extract_features("mining()"))[0], 0.0)


class TestContextualPolicyFallback(unittest.TestCase):
    def test_old_context_dim_falls_back_to_thompson(self):
        bandit = ContextualBandit("/nonexistent.pkl", fallback=ThompsonBandit())
        bandit.model = object()  # type: ignore[assignment]
        bandit.hint_index = ["succ_r1_for_A", "succ_r1_for_B"]
        bandit.context_dim = 8
        bandit.context_version = 1
        self.assertFalse(bandit.is_ready())
        picked = bandit.select([0.0] * 8, ["succ_r1_for_A", "succ_r1_for_B"])
        self.assertIn(picked, ["succ_r1_for_A", "succ_r1_for_B"])

    def test_rf_predict_all_fail_falls_back_to_thompson(self):
        class _BrokenModel:
            def predict(self, x):
                raise RuntimeError("dim mismatch")

        bandit = ContextualBandit("/nonexistent.pkl", fallback=ThompsonBandit(), epsilon=0.0)
        bandit.model = _BrokenModel()
        bandit.hint_index = ["succ_r1_for_A", "succ_r1_for_B"]
        bandit.context_dim = CONTEXT_DIM
        bandit.context_version = CONTEXT_VERSION
        ctx = [0.0] * CONTEXT_DIM
        picked = bandit.select(ctx, ["succ_r1_for_A", "succ_r1_for_B"])
        self.assertIn(picked, ["succ_r1_for_A", "succ_r1_for_B"])


class TestContextVersion(unittest.TestCase):
    def test_version_constant(self):
        self.assertEqual(CONTEXT_VERSION, 2)


if __name__ == "__main__":
    unittest.main()
