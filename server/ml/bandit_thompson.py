"""
server/ml/bandit_thompson.py
─────────────────────────────────────────────────────────────
Beta(α, β) 분포 기반 Thompson Sampling 밴딧.

기존 ε-greedy 의 단점:
    표본이 적은 변형이 평균 비교에서 계속 뒤로 밀리는 cold-start 문제.

Thompson Sampling:
    각 변형의 보상 분포를 Beta(α, β) 로 추정하고,
    선택 시 분포에서 한 번 샘플링한 값이 가장 큰 변형을 고른다.
    표본이 적으면 분산이 크게 유지돼 자연스럽게 탐색 빈도가 올라간다.

연속형 reward 매핑:
    α += reward,   β += (1.0 - reward)
    (이진 케이스 reward∈{0,1} 에서 일반 Beta 업데이트와 동일)
"""

import random


class ThompsonBandit:
    """힌트 변형 단위로 Beta(α, β) 를 유지하는 단순 밴딧."""

    def __init__(self) -> None:
        # {hint_type: (alpha, beta)} — 미등록 변형은 (1.0, 1.0) 로 시작 (= 균등 사전분포)
        self.params: dict[str, tuple[float, float]] = {}

    # ── 선택 ────────────────────────────────────────────────
    def select(self, candidates: list[str]) -> str | None:
        """
        후보 변형 각각의 Beta 분포에서 한 번씩 샘플링하고,
        가장 큰 값을 가진 변형을 반환합니다.
        """
        if not candidates:
            return None
        if len(candidates) == 1:
            return candidates[0]

        # numpy 의존을 피하기 위해 random.betavariate 사용
        best_score = -1.0
        best_ht    = candidates[0]
        for ht in candidates:
            a, b = self.params.get(ht, (1.0, 1.0))
            sample = random.betavariate(max(a, 1e-3), max(b, 1e-3))
            if sample > best_score:
                best_score = sample
                best_ht    = ht
        return best_ht

    # ── 업데이트 ────────────────────────────────────────────
    def update(self, hint_type: str, reward: float) -> None:
        """
        reward ∈ [0.0, 1.0] 연속값을 Beta 분포에 누적합니다.

        Args:
            hint_type : 힌트 변형 ID
            reward    : 0.0~1.0 (compute_reward 결과)
        """
        if hint_type is None:
            return
        r = max(0.0, min(1.0, float(reward)))
        a, b = self.params.get(hint_type, (1.0, 1.0))
        self.params[hint_type] = (a + r, b + (1.0 - r))

    # ── 직렬화 / 역직렬화 (DB 동기화용) ──────────────────────
    def load(self, mapping: dict[str, tuple[float, float]]) -> None:
        """
        외부 저장소(DB hint_stats.alpha/beta) 로부터 파라미터를 일괄 로드합니다.
        기존 메모리 상태를 모두 덮어씁니다.
        """
        self.params = {
            ht: (float(a), float(b))
            for ht, (a, b) in mapping.items()
        }

    def get(self, hint_type: str) -> tuple[float, float]:
        """현재 (α, β) 반환. 미등록이면 (1.0, 1.0)."""
        return self.params.get(hint_type, (1.0, 1.0))

    def expected_value(self, hint_type: str) -> float:
        """Beta(α, β) 의 기댓값 = α / (α+β). 점수 시각화용."""
        a, b = self.get(hint_type)
        denom = a + b
        return a / denom if denom > 0 else 0.5
