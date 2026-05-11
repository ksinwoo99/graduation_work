# ML 모델 업데이트 노트 — Scoring 2.0 + Contextual Bandit

> **버전**: v2.0
> **적용 일자**: 2026-05-09
> **대상 모듈**: `server/`
> **DB 마이그레이션**: 적용 완료 (code_logs +7 컬럼 / hint_stats +2 컬럼 / user_skill_profile 신설)

---

## 0. 한 줄 요약

> **단일 공식·이진 보상·context-free 밴딧** 으로 운영되던 ML 파이프라인을
> **3계층(Rule → Pattern → Policy)** 구조로 전환하여
> **사용자 숙련도·힌트 채택도·코드 품질**을 동시에 학습 신호로 반영하도록 개선했다.

---

## 1. 왜 바꿨는가 — 기존 시스템의 4대 한계

| # | 한계 | 영향 | 새 시스템의 대응 |
|---|---|---|---|
| L1 | 점수가 모든 사용자에게 동일 공식 | 평균 60짜리 유저의 70점과 평균 85짜리 유저의 70점이 동일 평가 | `personal_delta_score` (z-score → tanh) |
| L2 | 페널티가 실행시간 1개뿐 | 죽은 코드·복붙·과중첩·무한루프 오남용 등 나쁜 패턴을 감점 못 함 | `antipattern_penalty` 7종 결정론 감지 |
| L3 | 보상이 이진(성공/실패) | "힌트를 따랐다"와 "우연히 성공"을 구분 못 함 | `compute_reward` 5단계 연속 보상 |
| L4 | 밴딧이 context-free | 초보 vs 숙련자의 최적 힌트가 같다고 간주 | `ContextualBandit` (RF) + `encode_user_context` |

추가로 cold-start 문제(표본 적은 변형이 계속 뒤로 밀림)도 **Thompson Sampling Beta(α,β)** 도입으로 해결.

---

## 2. 아키텍처 비교

### Before

```
submit_code()
├─ extract_features()       (12 features)
├─ calculate_score()        (정적 공식, 개인/히스토리 무시)
├─ predict_cluster_rank()   (KMeans k=3)
└─ ε-greedy(ε=0.2)          (context-free, 이진 success/fail)
```

### After (3-Layer)

```
┌─────────────── Submission ──────────────────────────────────┐
│                                                              │
│  Layer 1 (Rule, 결정론·즉각 반응·설명 가능)                    │
│  ├─ base_score          (기존 calculate_score 그대로)         │
│  ├─ personal_delta      (개인 z-score · tanh)                │
│  ├─ adoption_score      (직전 힌트 방향과의 AST 일치도)        │
│  ├─ antipattern_pen     (7종 + error_recurrence)             │
│  └─ → final_score = aggregator(...)                          │
│                                                              │
│  Layer 2 (Pattern, 군집)                                      │
│  └─ predict_cluster_rank   (KMeans + 가중 피처, 기존 유지)     │
│                                                              │
│  Layer 3 (Policy, 학습형 선택)                                │
│  ├─ encode_user_context (8차원: 평균/std/성공률/rank폭/정체…) │
│  ├─ ContextualBandit    (RandomForest 정책)                  │
│  │     └─ fallback → ThompsonBandit(Beta α/β)                │
│  ├─ compute_reward      (0.0 / 0.2 / 0.5 / 0.8 / 1.0)        │
│  └─ policy_trainer      (ml_worker 가 ≥100 샘플 시 RF 재학습) │
│                                                              │
└──────────────────────────────────────────────────────────────┘
```

---

## 3. 신규/변경 파일 맵

### 신규 패키지

| 경로 | 역할 | 의존 |
|---|---|---|
| `scoring/__init__.py` | 패키지 마커 | — |
| `scoring/aggregator.py` | `final_score()` 가중합 + `score_breakdown()` 분해 | — |
| `scoring/personal_score.py` | DB 조회 → z-score → tanh(0~100) | pymysql |
| `scoring/hint_adoption.py` | `HINT_TARGET_VECTORS` + `compute_adoption()` | — |
| `scoring/antipattern.py` | AST 7종 감지 + `error_recurrence_penalty()` | ast |
| `ml/__init__.py` | 패키지 마커 | — |
| `ml/bandit_thompson.py` | `ThompsonBandit` (Beta α/β) | random |
| `ml/context_encoder.py` | 8차원 `encode_user_context()` | math |
| `ml/contextual_policy.py` | `ContextualBandit` + 핫리로드 + Thompson 폴백 | sklearn, joblib, numpy |
| `ml/reward.py` | `compute_reward()` 5단계 매핑 | scoring.hint_adoption |
| `ml/policy_trainer.py` | DB → (context, hint, reward) → RF 재학습 | sklearn, pymysql |

### 수정 파일

| 파일 | 변경 |
|---|---|
| `main.py` | 3계층 파이프라인 통합, `submit_code()` 7단계 재구성, 신규 엔드포인트 2개 추가, 옛 `_BANDIT_EPSILON`/`_update_prev_hint_effectiveness` 제거 |
| `ml_worker.py` | KMeans 학습 후 `train_policy()` 추가 호출 |
| `.gitignore` | `__pycache__/`, `server/.env`, `server/*.pkl/log/out` 추가 |

### 정리된 파일 (삭제)

| 파일 | 이유 |
|---|---|
| `set.py` | 1회성 마이그레이션 스크립트 + **하드코딩된 프로덕션 DB 비밀번호** (보안 위험) |
| `~0320.out` | 264KB 옛날 uvicorn 로그 |
| `__pycache__/`, `models/__pycache__/` | Python 바이트코드 캐시 |

---

## 4. `submit_code()` 새 7단계 데이터 흐름

```
┌───────────────────────────────────────────────────────────────┐
│ ① extract_features + calculate_score → base_score             │
│ ② predict_cluster_rank → cluster_rank (실패 시 -1)              │
│ ③ Layer 1                                                      │
│   ├─ personal_delta_score(cursor, user_pk, base_score)         │
│   └─ antipattern_penalty(source) → (pen, tags)                 │
│ ④ _fetch_prev_submission(cursor, user_pk)                      │
│   ├─ adoption_score = compute_adoption(prev_feat, cur_feat,…)  │
│   ├─ prev_reward    = compute_reward(...)                      │
│   ├─ + error_recurrence_penalty(cur, prev) → pen += 20         │
│   └─ ContextualBandit.update(prev_hint, prev_reward)           │
│        → _persist_thompson_update (alpha/beta UPSERT)          │
│ ⑤ score = final_score(base, personal, adoption, antipattern)   │
│ ⑥ user_context = encode_user_context(cursor, user_pk)          │
│   └─ (ai_hint, hint_type) = _generate_hint_typed(..., ctx)     │
│        ContextualBandit.select(ctx, candidates)                │
│        ├─ RF 모델 로드됨? → 예측 보상 max + ε(=0.1) 탐색         │
│        └─ 폴백 → ThompsonBandit.select (Beta 샘플 max)          │
│ ⑦ INSERT code_logs + 신규 7컬럼 graceful UPDATE                 │
└───────────────────────────────────────────────────────────────┘
```

---

## 5. 점수 공식 — Scoring 2.0

```python
SCORE_WEIGHTS = {
    "base":        0.50,   # 기존 교육 목표 점수
    "personal":    0.20,   # 개인 성장 z-score
    "adoption":    0.15,   # 힌트 채택도
    "antipattern": 0.15,   # 음의 기여
}

final_score =  0.50·base + 0.20·personal + 0.15·adoption − 0.15·antipattern_pen
            (clip 0~100, 소수 둘째 자리 반올림)
```

**스케일 직관:**

| 케이스 | base | personal | adoption | antipattern | final |
|---|---|---|---|---|---|
| 신규 유저 첫 제출 (모두 중립) | 70 | 50 | 50 | 0 | 35+10+7.5 = **52.5** |
| 평소 60점 유저가 70점 (성장) | 70 | 80 | 50 | 0 | 35+16+7.5 = **58.5** |
| 평소 80점 유저가 70점 (퇴보) | 70 | 30 | 50 | 0 | 35+6+7.5 = **48.5** |
| 힌트 따라 잘 개선 | 80 | 65 | 95 | 0 | 40+13+14.25 = **67.25** |
| 같은 에러 재발 (힌트 무시) | 50 | 40 | 50 | 20 | 25+8+7.5−3 = **37.5** + Thompson 보상=0 |

같은 base 점수라도 **개인사·힌트 추종도·코드 품질**에 따라 차등화되는 것이 핵심.

---

## 6. 안티패턴 페널티 7종

| 태그 | 가중치 | 감지 방법 |
|---|---|---|
| `dead_code` | 15 | `return/break/continue/raise` 다음에 더 코드가 있음 |
| `duplicate_lines` | 12 | 같은 실행문이 ≥3회 반복 (복붙 회피용) |
| `over_nesting` | 10 | 제어 흐름 최대 중첩 깊이 ≥4 |
| `infinite_no_break` | 10 | `while True:` 안에 `break` 없음 |
| `unused_variable` | 8 | 할당했지만 어디서도 Load 안 된 변수 (`_`로 시작은 제외) |
| `magic_range` | 7 | `range(N)` 의 N ≥ 1000 |
| `error_recurrence` | 20 | 직전 제출과 같은 종류의 에러 재발 (별도 함수) |

문법 오류 코드는 (0.0, []) 반환 → 에러 힌트와 감점이 중복되지 않도록 차단.

---

## 7. 보상(reward) 산출 5단계

```
prev → cur 전이 패턴               → reward
─────────────────────────────────────────────
같은 종류 에러 재발                  → 0.0   (힌트 완전 무시)
에러 → 성공 전환                     → 1.0   (힌트가 문제 해결)
성공 → 성공 (힌트 방향대로 진화)     → 0.7~1.0 (adoption + rank_bonus)
성공 → 성공 (변화 없음)              → 0.5   (관망)
성공 → 성공 (반대 방향)              → 0.0~0.3
```

이 reward 가
1. `ContextualBandit.update(...)` → `ThompsonBandit.update(...)` 의 Beta 분포 갱신
2. `code_logs.reward` 컬럼에 영속화 → policy_trainer 의 RF 학습 데이터로 재사용

---

## 8. DB 스키마 (적용 완료)

### `code_logs` (+7 컬럼)

| 컬럼 | 타입 | 의미 |
|---|---|---|
| `base_score` | FLOAT NULL | calculate_score() 결과 (Layer 1 입력) |
| `personal_score` | FLOAT NULL | personal_delta_score() 결과 |
| `adoption_score` | FLOAT NULL | 직전 힌트의 채택 정도 |
| `antipattern_pen` | FLOAT NULL | 안티패턴 감점 합계 |
| `antipattern_tags` | VARCHAR(255) NULL | 콤마 구분 태그 (`dead_code,over_nesting`) |
| `reward` | FLOAT NULL | 직전 제출의 hint_type 에 대한 보상 (0.0~1.0) |
| `error_type` | VARCHAR(32) NULL | 에러 타입 키워드 (예: `nameerror`) |

### `hint_stats` (+2 컬럼)

| 컬럼 | 타입 | 의미 |
|---|---|---|
| `alpha` | FLOAT NOT NULL DEFAULT 1.0 | Thompson Beta α |
| `beta` | FLOAT NOT NULL DEFAULT 1.0 | Thompson Beta β |

### `user_skill_profile` (신설, 캐시 테이블)

| 컬럼 | 타입 | 의미 |
|---|---|---|
| `user_pk` | INT PK | 유저 PK |
| `context_vec` | JSON NOT NULL | 8차원 컨텍스트 캐시 |
| `updated_at` | DATETIME | 자동 갱신 |

> 현재 `user_skill_profile` 은 캐시용으로 예약된 테이블이며, 매 요청마다 `encode_user_context()` 가 실시간 계산하므로 비어 있어도 동작에 영향 없음. 추후 Redis 캐시처럼 활용 가능.

---

## 9. 새/변경 엔드포인트

### `POST /api/submit_code` (응답 확장)

```json
{
  "status":           "success",
  "score":            67.25,
  "hint":             "[ 일반 학습자형 ] for 루프로 좋은 구조를 …\n[ 성장 중! ] …",
  "cluster_rank":     1,
  "base_score":       80.0,
  "personal_score":   65.0,
  "adoption_score":   95.0,
  "antipattern_pen":  0.0,
  "antipattern_tags": [],
  "hint_type":        "succ_r1_for_B"
}
```

### `GET /api/score_breakdown/{log_id}` (신설)

```json
{
  "weights":       {"base": 0.5, "personal": 0.2, "adoption": 0.15, "antipattern": 0.15},
  "subscores":     {"base": 80, "personal_delta": 65, "adoption": 95, "antipattern_pen": 0},
  "contributions": {"base": 40.0, "personal": 13.0, "adoption": 14.25, "antipattern": -0.0},
  "raw":   67.25,
  "final": 67.25,
  "antipattern_tags": [],
  "error_type":  null,
  "hint_type":   "succ_r1_for_B",
  "reward":      0.85,
  "cluster_rank": 1,
  "is_success":  true
}
```

### `GET /api/hint_stats` (응답 확장)

```json
{
  "status": "success",
  "hint_count": 12,
  "policy_loaded": false,
  "stats": {
    "succ_r0_simple_A": {
      "shown_count": 23,
      "success_count": 14,
      "success_rate": 0.609,
      "alpha": 14.7,
      "beta": 9.3,
      "expected_reward": 0.612
    },
    ...
  }
}
```

### `POST /api/model_reload` (확장)

KMeans + Contextual Policy 둘 다 강제 핫리로드. 응답에 `policy_loaded` 필드 추가.

---

## 10. 운영 가드레일 (graceful fallback 매트릭스)

| 상황 | 시스템 반응 |
|---|---|
| `code_policy_model.pkl` 부재 | ContextualBandit → ThompsonBandit 자동 폴백 |
| Thompson 학습 데이터 < 100 | `train_policy()` 스킵, 기존 정책 유지 |
| 신규 유저 (직전 제출 없음) | adoption=50, reward 미계산, Thompson 업데이트 없음 |
| `extract_features(prev.source)` 실패 | prev_features={} → adoption 50 폴백 |
| 신규 7컬럼 미존재 (롤백 시) | UPDATE try/except → 메인 로직 정상 |
| Thompson α/β 컬럼 미존재 | _persist_thompson_update → legacy success_count 증가로 폴백 |
| pkl 핫리로드 중 mtime 변화 | maybe_reload() 가 자동 재로드, 락 없이도 안전 |

---

## 11. 졸업작품 발표 포인트 (즉시 인용 가능)

1. **"순환 결합 제거로 ML 객관성 확보"**
   `score`를 학습 피처에서 제외 (kmeans_trainer.py 주석 참조)

2. **"힌트 효과를 AST 방향성으로 정량 측정"**
   `HINT_TARGET_VECTORS` × 피처 델타 부호 → 0~100 채택도 (`hint_adoption.py`)
   증거: `code_logs.adoption_score` 컬럼 + `/api/score_breakdown`

3. **"힌트를 무시하면 최대 -20점 감점 + 보상 0"**
   `error_recurrence_penalty` + `compute_reward` 의 0.0 매핑
   증거: `antipattern_tags` 에 `error_recurrence` 포함

4. **"Thompson Sampling 으로 cold-start 공정성 확보"**
   Beta 분포 샘플링 → 표본 적은 변형도 자연스럽게 탐색됨
   증거: `/api/hint_stats` 의 `alpha`, `beta`, `expected_reward`

5. **"같은 점수도 사용자 숙련도에 따라 다르게 평가"**
   z-score · tanh 압축으로 ±3σ를 0~100에 매핑
   증거: 같은 base에 다른 personal_score 출력

6. **"학습형 정책으로 사용자 컨텍스트별 최적 힌트 선택"**
   8차원 context + RandomForest 예측 보상 (`ContextualBandit`)
   ml_worker가 100건 누적 후 자동 학습 → 핫리로드

---

## 12. 운영 체크리스트 (배포 전 검증)

- [x] `migrations/scoring_v2.sql` 적용 완료 (또는 set.py 1회 실행 완료)
- [x] `scoring/`, `ml/` 패키지 업로드
- [x] `main.py` / `ml_worker.py` 새 버전 업로드
- [x] 데드 코드 제거 (`_BANDIT_EPSILON`, `_update_prev_hint_effectiveness`)
- [x] `set.py` (DB 비밀번호 노출) 삭제
- [x] `.gitignore` 보강 (.env, *.pkl, *.log, *.out)
- [ ] 서버 재시작 후 다음 로그 확인:
  - `[Model] 로드 완료 ...`
  - `[HintBandit] N개 힌트 통계 로드 완료 (Thompson 파라미터 N개 동기화)`
  - `[ContextualPolicy] 로드 실패 (Thompson 폴백)` ← 정책 pkl 아직 없음, **정상**
- [ ] 첫 제출 응답에 `base_score / personal_score / adoption_score / antipattern_pen / hint_type` 필드 포함 확인
- [ ] 100건 이상 누적 후 `code_policy_model.pkl` 자동 생성 확인 (다음 ml_worker 사이클)

---

## 13. 향후 확장 여지

- `user_skill_profile` 캐시 활용 (Redis 대체) — 부하 증가 시
- `HINT_TARGET_VECTORS` 에 새 hint_type 추가 시 코드 한 줄로 자동 학습 통합
- `/api/score_breakdown` 데이터를 Unity UI 에서 그래프로 시각화 (subscores 구성요소)
- A/B 테스트: Thompson vs Contextual 정책의 누적 보상 비교 시각화

---

**End of Document**
