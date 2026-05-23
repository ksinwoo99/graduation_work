"""
발표 1주 전 준비 — hint_routing 테스트 + KMeans k=3 재학습 + pkl 검증.

실행 (server/ 디렉터리):
    python scripts/presentation_prep.py
"""

from __future__ import annotations

import os
import sys
import unittest

import joblib

_SERVER_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, _SERVER_DIR)
os.chdir(_SERVER_DIR)

from config import MODEL_PATH
from models.kmeans_trainer import train as train_kmeans

_NEW_FEATURES = (
    "has_infinite_for",
    "has_if_inside_loop",
    "has_break_in_loop",
    "uses_loop_index",
    "max_range_n",
)


def _run_hint_tests() -> bool:
    print("\n" + "=" * 50)
    print("1/3  hint_routing 시나리오 테스트")
    print("=" * 50)
    loader = unittest.TestLoader()
    suite = loader.loadTestsFromName("tests.test_hint_routing")
    runner = unittest.TextTestRunner(verbosity=2)
    result = runner.run(suite)
    return result.wasSuccessful()


def _run_kmeans_train() -> bool:
    print("\n" + "=" * 50)
    print("2/3  KMeans k=3 재학습 (DB → pkl)")
    print("=" * 50)
    try:
        train_kmeans()
        return os.path.exists(MODEL_PATH)
    except Exception as exc:
        print(f"[ERROR] KMeans 학습 실패: {exc}")
        return False


def _validate_pkl() -> bool:
    print("\n" + "=" * 50)
    print("3/3  code_cluster_model.pkl 검증")
    print("=" * 50)
    if not os.path.exists(MODEL_PATH):
        print(f"[FAIL] pkl 없음: {MODEL_PATH}")
        return False

    saved = joblib.load(MODEL_PATH)
    feature_names = saved.get("feature_names") or []
    meta = saved.get("meta") or {}
    missing = [f for f in _NEW_FEATURES if f not in feature_names]

    print(f"  trained_at  : {meta.get('trained_at', '?')}")
    print(f"  data_count  : {meta.get('data_count', '?')}")
    print(f"  n_features  : {len(feature_names)}")
    print(f"  n_clusters  : {saved.get('model').n_clusters if saved.get('model') else '?'}")

    if missing:
        print(f"[FAIL] 신규 피처 누락: {missing}")
        return False

    summary = meta.get("cluster_summary") or {}
    for rank in sorted(summary.keys()):
        row = summary[rank]
        print(
            f"  rank {rank} [{row.get('label', '?')}] "
            f"n={row.get('count', '?')} score_mean={row.get('score_mean', '?')}"
        )

    print("[OK] pkl 검증 통과")
    return True


def main() -> int:
    ok_tests = _run_hint_tests()
    ok_train = _run_kmeans_train()
    ok_pkl = _validate_pkl() if ok_train else False

    print("\n" + "=" * 50)
    print("결과 요약")
    print("=" * 50)
    print(f"  hint_routing 테스트 : {'PASS' if ok_tests else 'FAIL'}")
    print(f"  KMeans 재학습       : {'PASS' if ok_train else 'FAIL'}")
    print(f"  pkl 검증            : {'PASS' if ok_pkl else 'FAIL'}")

    return 0 if (ok_tests and ok_train and ok_pkl) else 1


if __name__ == "__main__":
    raise SystemExit(main())
