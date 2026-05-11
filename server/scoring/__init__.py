"""
server/scoring/
─────────────────────────────────────────────────────────────
Scoring 2.0 — 다차원 점수 벡터 패키지

Layer 1 (Rule) 의 결정론 점수 모듈을 모아둡니다.

모듈 구성:
    aggregator.py      — final_score() 가중합
    personal_score.py  — 개인 성장 z-score
    hint_adoption.py   — 직전 힌트 채택도(AST 방향 일치도)
    antipattern.py     — AST 기반 안티패턴 페널티

기존 calculate_score() 는 main.py 에 그대로 남기고,
이 패키지는 그 위에 레이어를 쌓는 구조로 동작합니다.
"""
