"""
server/ml/
─────────────────────────────────────────────────────────────
강화학습 / Contextual Bandit 패키지

Layer 3 (Policy) 의 정책·보상·컨텍스트 인코더를 모아둡니다.

모듈 구성:
    reward.py            — 연속형 보상(reward shaping)
    bandit_thompson.py   — Beta 분포 기반 Thompson Sampling
    context_encoder.py   — 유저 상태 8차원 벡터화
    contextual_policy.py — RandomForest 기반 정책 + Thompson 폴백
    policy_trainer.py    — ml_worker 가 주기 호출하는 RF 재학습

기존 ε-greedy 밴딧과 병행 가능하도록 설계되었으며,
RF 모델 미학습 / 데이터 부족 시 자동으로 Thompson 으로 폴백합니다.
"""
