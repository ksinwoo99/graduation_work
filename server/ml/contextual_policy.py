"""
server/ml/contextual_policy.py
─────────────────────────────────────────────────────────────
Contextual Bandit — 사용자 컨텍스트 + 후보 hint_type 으로 예측 보상을 계산해
가장 보상 기댓값이 높은 변형을 선택합니다.

학습 데이터 누적 전 / 모델 파일 부재 시:
    Thompson Sampling 으로 자동 폴백.

핫리로드 전략:
    main.py 가 호출 직전에 maybe_reload_model() 을 한 번 실행해
    ml_worker 가 갱신한 pkl 을 무중단으로 반영합니다.
"""

import os
import random
import joblib
import numpy as np

from ml.bandit_thompson import ThompsonBandit


class ContextualBandit:
    """
    RandomForestRegressor + Thompson 폴백.

    선택 절차:
        1. 모델 미로드 / hint_index 비어있음 → ThompsonBandit.select(candidates)
        2. ε(=epsilon) 확률로 랜덤 후보 (탐색)
        3. (1-ε) 확률로 RF 예측 보상 최대 후보 (활용)
    """

    def __init__(self, model_path: str, fallback: ThompsonBandit | None = None,
                 epsilon: float = 0.1) -> None:
        self.model_path = model_path
        self.epsilon    = epsilon
        self.fallback   = fallback or ThompsonBandit()

        self.model            = None      # sklearn RandomForestRegressor
        self.hint_index: list[str] = []   # one-hot 매핑용 hint_type 순서
        self.context_dim: int    = 0
        self._loaded_mtime: float = 0.0

    # ── 모델 로드 / 핫리로드 ───────────────────────────────
    def _load(self) -> None:
        try:
            data = joblib.load(self.model_path)
            self.model       = data.get("model")
            self.hint_index  = list(data.get("hint_index", []))
            self.context_dim = int(data.get("context_dim", 0))
            self._loaded_mtime = os.path.getmtime(self.model_path)
            print(
                f"[ContextualPolicy] 로드 완료  "
                f"hint_count={len(self.hint_index)} | context_dim={self.context_dim}"
            )
        except Exception as e:
            self.model      = None
            self.hint_index = []
            print(f"[ContextualPolicy] 로드 실패 (Thompson 폴백): {e}")

    def maybe_reload(self) -> None:
        """pkl 파일 mtime 변경 시에만 재로드 (호출 부담 최소)."""
        if not os.path.exists(self.model_path):
            return
        try:
            mtime = os.path.getmtime(self.model_path)
            if mtime != self._loaded_mtime:
                self._load()
        except Exception as e:
            print(f"[ContextualPolicy] 핫리로드 실패: {e}")

    def is_ready(self) -> bool:
        """RF 모델이 사용 가능한 상태인지."""
        return self.model is not None and bool(self.hint_index)

    # ── 선택 ────────────────────────────────────────────────
    def _onehot(self, hint_type: str) -> np.ndarray:
        vec = np.zeros(len(self.hint_index), dtype=float)
        if hint_type in self.hint_index:
            vec[self.hint_index.index(hint_type)] = 1.0
        return vec

    def select(self, context: list[float], candidates: list[str]) -> str | None:
        if not candidates:
            return None
        self.maybe_reload()

        if not self.is_ready():
            return self.fallback.select(candidates)

        # 컨텍스트 차원이 안 맞으면 안전하게 폴백
        if self.context_dim and len(context) != self.context_dim:
            return self.fallback.select(candidates)

        # 후보 중 hint_index 에 등록된 것이 하나도 없으면 폴백
        registered = [c for c in candidates if c in self.hint_index]
        if not registered:
            return self.fallback.select(candidates)

        # ε-탐색: 후보 중 무작위 — 신규 변형이나 미관측 컨텍스트 보호
        if random.random() < self.epsilon:
            return random.choice(candidates)

        ctx_arr = np.asarray(context, dtype=float)
        best_ht, best_pred = registered[0], -float("inf")
        for ht in registered:
            x = np.concatenate([ctx_arr, self._onehot(ht)]).reshape(1, -1)
            try:
                pred = float(self.model.predict(x)[0])
            except Exception:
                continue
            if pred > best_pred:
                best_pred = pred
                best_ht   = ht
        return best_ht

    # ── 업데이트는 항상 폴백(Thompson) 에 누적 ──────────────
    # RF 는 ml_worker.py 가 batch 로 재학습하므로
    # 실시간 업데이트는 Thompson 만 갱신합니다.
    def update(self, hint_type: str, reward: float) -> None:
        self.fallback.update(hint_type, reward)
