"""
server/main.py
ML 서버 (Server B) — 코드 로그 저장 / AI 힌트 생성 / 루프 균형 분석 / Scoring 2.0

엔드포인트:
    POST /api/submit_code                       — 코드 제출 결과 저장 및 AI 힌트 반환
    GET  /api/user_cluster_history/{user_id}    — 유저 군집 이동 이력 조회
    GET  /api/user_loop_balance/{user_id}       — 유저 루프 사용 균형 분석
    GET  /api/score_breakdown/{log_id}          — 다차원 점수 분해(Scoring 2.0)
    GET  /api/hint_stats                        — 힌트 밴딧 통계(α/β + expected_reward)
    GET  /api/model_status                      — ML 모델 상태 확인 (디버깅용)
    POST /api/model_reload                      — ML 모델 + 정책 강제 재로드 (디버깅용)

Scoring 2.0 / Contextual Bandit 모듈:
    scoring/aggregator      — final_score = base*0.5 + personal*0.2 + adoption*0.15 - antipattern*0.15
    scoring/personal_score  — 최근 10개 성공 점수 대비 z-score (개인 성장)
    scoring/hint_adoption   — 직전 힌트 방향과 AST 변화의 일치도
    scoring/antipattern     — 7종 안티패턴 결정론 감지 + 에러 재발 페널티
    ml/bandit_thompson      — Beta(α,β) Thompson Sampling
    ml/contextual_policy    — RandomForest 정책 + Thompson 폴백
    ml/context_encoder      — 유저 상태 8차원 벡터 인코딩
    ml/reward               — 5단계 연속형 보상 산출
    ml/policy_trainer       — 주기 RF 재학습 (ml_worker 가 호출)
"""

from fastapi import FastAPI, HTTPException
from pydantic import BaseModel
import pymysql
import joblib
import os
import ast
import datetime
import re
import difflib
import random
import numpy as np
import pandas as pd

from config import DB_CONFIG
from utils import extract_features, calculate_ast_complexity

# ── Scoring 2.0 (Layer 1) ───────────────────────────────────────
from scoring.aggregator     import final_score, score_breakdown
from scoring.personal_score import personal_delta_score
from scoring.hint_adoption  import compute_adoption
from scoring.antipattern    import antipattern_penalty, error_recurrence_penalty

# ── ML / Contextual Bandit (Layer 3) ────────────────────────────
from ml.bandit_thompson   import ThompsonBandit
from ml.contextual_policy import ContextualBandit
from ml.context_encoder   import encode_user_context
from ml.reward            import compute_reward

app = FastAPI()

_BASE_DIR    = os.path.dirname(os.path.abspath(__file__))
MODEL_PATH   = os.path.join(_BASE_DIR, 'code_cluster_model.pkl')
POLICY_PATH  = os.path.join(_BASE_DIR, 'code_policy_model.pkl')

# DB 스키마는 server/migrations/scoring_v2.sql 로 적용된 상태를 가정합니다.
# 신규 컬럼/테이블에 접근하는 코드는 모두 try/except 로 감싸 마이그레이션 전
# 환경에서도 메인 기능(힌트 반환·기본 score 저장)이 동작하도록 폴백합니다.


# ──────────────────────────────────────────────────────────
# 모델 전역 상태
# ──────────────────────────────────────────────────────────
code_cluster_model = None
scaler             = None

# cluster_rank_map: {raw cluster ID → semantic rank}
#   0=단순 코드형 / 1=성장형 / 2=효율 최적화형
# kmeans_trainer.py 가 학습 후 score 평균 기준으로 정렬해 저장하므로
# 재학습으로 cluster ID 가 뒤바뀌어도 힌트 의미가 그대로 유지됩니다.
cluster_rank_map: dict[int, int] = {}

# pkl 파일 mtime — ml_worker 가 갱신하면 핫리로드 트리거
_model_loaded_mtime: float = 0.0

# kmeans_trainer.py 가 pkl 에 함께 저장한 학습 메타데이터
# /api/model_status 응답 및 디버깅에 사용합니다.
_model_meta: dict = {}

# StandardScaler 이후 적용한 피처 가중치 — predict_cluster_rank() 에서
# 동일하게 곱해야 학습·추론 피처 공간이 일치합니다.
_feature_weights: list = []
_feature_names:   list = []

# ── 힌트 효과성 밴딧 상태 ────────────────────────────────────────
# 성공 힌트(rank 0/1)에 복수 변형을 두고, 어떤 변형이 사용자 개선을
# 실제로 이끌어냈는지 자동으로 학습합니다.
# 선택 정책: ContextualBandit(RF) → ThompsonBandit(Beta α/β) 폴백.
# (구버전의 ε-greedy 는 Thompson Sampling 으로 완전 교체됨)
_hint_stats: dict[str, dict] = {}   # hint_type → {"shown": N, "success": M} (in-memory 노출 카운트)

# 밴딧이 선택할 힌트 변형 풀 — (hint_text, hint_type_id) 쌍의 리스트.
# 각 상황(group_key)에 2개의 변형을 두어 어떤 표현이 더 효과적인지 학습합니다.
#
# 그룹 키 명명 규칙:
#   succ_r{rank}_{서브케이스}
#       rank      0 단순 / 1 학습자 / 2 효율 최적화
#       서브케이스 코드의 구조적 특징 (simple / has_if / for / while / for_count / infinite_while ...)
_HINT_VARIANTS: dict[str, list[tuple[str, str]]] = {
    # ────────────── rank 0: 루프 미사용 ──────────────
    "succ_r0_simple": [
        (
            "[ 단순 코드형 ] "
            "명령을 하나씩 순서대로 실행하는 코드예요. "
            "반복문(for)을 사용하면 같은 명령을 여러 번 한 번에 실행할 수 있어요! "
            "예시: for i in range(5): mining()",
            "succ_r0_simple_A",
        ),
        (
            "[ 단순 코드형 ] "
            "mining()을 한 줄씩 쓰는 대신 for i in range(5): mining() 으로 묶어보세요! "
            "숫자가 클수록 기계가 더 많이 일해요.",
            "succ_r0_simple_B",
        ),
    ],
    "succ_r0_has_if": [
        (
            "[ 단순 코드형 ] "
            "조건문(if)을 활용하고 있어요! "
            "여기에 반복문(for)까지 더하면 훨씬 강력해집니다. "
            "예시: for i in range(5): mining()",
            "succ_r0_has_if_A",
        ),
        (
            "[ 단순 코드형 ] "
            "if 판단을 잘 쓰고 있어요! "
            "while True: 로 기계를 계속 돌리면서 if 로 상황을 판단하면 더 강력해요.",
            "succ_r0_has_if_B",
        ),
    ],

    # ────────────── rank 1: 일반 학습자 (단일 루프) ──────────────
    "succ_r1_for": [
        (
            "[ 일반 학습자형 ] "
            "for 반복문을 잘 쓰고 있어요! "
            "range() 의 숫자를 더 키우거나, while True: + break 조건으로 자동화에 도전해보세요.",
            "succ_r1_for_A",
        ),
        (
            "[ 일반 학습자형 ] "
            "for 루프로 좋은 구조를 만들었어요! "
            "from itertools import count 후 for i in count(): 으로 무한 반복도 도전해보세요.",
            "succ_r1_for_B",
        ),
    ],
    "succ_r1_while": [
        (
            "[ 일반 학습자형 ] "
            "while 반복문을 사용하고 있어요! "
            "while True: 로 변경하면 기계가 멈추지 않고 계속 자동으로 작동해요.",
            "succ_r1_while_A",
        ),
        (
            "[ 일반 학습자형 ] "
            "while 루프를 쓰고 있군요! "
            "조건문 대신 while True: + 내부 break 로 더 명확한 종료 흐름을 만들 수 있어요.",
            "succ_r1_while_B",
        ),
    ],

    # ────────────── rank 2: 효율 최적화 (무한 / 고효율 루프) ──────────────
    # while True: — "고전적" 무한 자동화
    "succ_r2_infinite_while": [
        (
            "[ 효율 최적화형 ] "
            "while True: 로 기계를 완전 자동화했어요! "
            "내부에 if + break 종료 조건이 있으면 더 안전한 코드가 됩니다.",
            "succ_r2_infinite_while_A",
        ),
        (
            "[ 효율 최적화형 ] "
            "무한 루프로 완벽한 자동화 코드예요! "
            "자원량을 확인해서 멈추는 종료 조건을 추가하면 한층 견고해집니다.",
            "succ_r2_infinite_while_B",
        ),
    ],
    # for i in count(...) — itertools 활용한 "파이써닉" 무한 자동화
    "succ_r2_infinite_for_count": [
        (
            "[ 효율 최적화형 ] "
            "from itertools import count 와 for i in count(): 로 파이써닉한 무한 반복을 구현했네요! "
            "i 값을 활용해 단계별 동작을 분기하면 표현력이 훨씬 풍부해져요.",
            "succ_r2_infinite_for_count_A",
        ),
        (
            "[ 효율 최적화형 ] "
            "for i in count(start=, step=): 가 들어간 깔끔한 무한 자동화예요! "
            "while True 보다 의도가 분명한 좋은 선택입니다. break 조건만 챙겨주세요.",
            "succ_r2_infinite_for_count_B",
        ),
    ],
    # for range — 큰 N 의 고효율 유한 루프
    "succ_r2_for_range": [
        (
            "[ 효율 최적화형 ] "
            "큰 횟수의 for range 로 고효율 작업 코드를 만들었어요! "
            "더 나아가 while True: 나 for i in count(): 으로 완전 자동화도 가능합니다.",
            "succ_r2_for_range_A",
        ),
        (
            "[ 효율 최적화형 ] "
            "효율적인 for range 루프예요! "
            "기계를 멈추지 않게 하려면 for i in count(): 처럼 끝이 정해지지 않는 루프를 시도해보세요.",
            "succ_r2_for_range_B",
        ),
    ],
}

# hint_type ID → hint_text 역방향 조회 맵 — 밴딧 선택 결과를 텍스트로 변환할 때 사용
_HINT_VARIANTS_MAP: dict[str, str] = {
    hint_type: text
    for variants in _HINT_VARIANTS.values()
    for (text, hint_type) in variants
}

# ── Thompson Sampling + Contextual Bandit 인스턴스 ──────────────
# Thompson 은 항상 활성화(폴백 포함). Contextual 은 pkl 이 있을 때만 활성.
_thompson_bandit   = ThompsonBandit()
_contextual_bandit = ContextualBandit(POLICY_PATH, fallback=_thompson_bandit, epsilon=0.1)


def _load_model() -> None:
    """pkl 파일을 읽어 전역 모델 변수를 갱신"""
    global code_cluster_model, scaler, cluster_rank_map, \
           _model_loaded_mtime, _model_meta, _feature_weights, _feature_names

    if not os.path.exists(MODEL_PATH):
        print(f"[Model] pkl 파일 없음: {MODEL_PATH}")
        return
    try:
        saved = joblib.load(MODEL_PATH)
        if isinstance(saved, dict):
            code_cluster_model = saved.get('model')
            scaler             = saved.get('scaler')
            # cluster_rank 키가 없는 구 포맷(하위 호환): 항등 매핑으로 대체
            cluster_rank_map   = saved.get('cluster_rank', {0: 0, 1: 1, 2: 2})
            _model_meta        = saved.get('meta', {})
            # 피처 가중치 — kmeans_trainer.py 에서 학습 시 적용한 값
            # 구 포맷 pkl 은 이 키가 없으므로 빈 리스트(가중치 미적용)로 폴백
            _feature_weights   = saved.get('feature_weights', [])
            _feature_names     = saved.get('feature_names',   [])
        else:
            # 이전 포맷(모델 단독 저장) 하위 호환
            code_cluster_model = saved
            cluster_rank_map   = {0: 0, 1: 1, 2: 2}
            _model_meta        = {}
            _feature_weights   = []
            _feature_names     = []

        _model_loaded_mtime = os.path.getmtime(MODEL_PATH)
        loaded_at = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")
        trained_at = _model_meta.get('trained_at', '알 수 없음')
        data_count = _model_meta.get('data_count', '알 수 없음')
        feature_count = scaler.n_features_in_ if scaler is not None else '알 수 없음'
        print(
            f"[Model] 로드 완료  loaded_at={loaded_at} | "
            f"trained_at={trained_at} | data_count={data_count} | "
            f"features={feature_count} | cluster_rank_map={cluster_rank_map}"
        )
    except Exception as e:
        print(f"[Model] 로드 실패: {e}")


def _maybe_reload_model() -> None:
    """
    pkl 파일의 수정 시각이 달라졌으면 모델을 핫리로드
    ml_worker.py 가 매시간 파일을 갱신할 때 서버 재시작 없이 자동 반영
    predict_cluster_rank() 호출 시마다 실행
    """
    if not os.path.exists(MODEL_PATH):
        return
    try:
        current_mtime = os.path.getmtime(MODEL_PATH)
        if current_mtime != _model_loaded_mtime:
            old_trained = _model_meta.get('trained_at', '없음')
            _load_model()
            new_trained = _model_meta.get('trained_at', '없음')
            print(f"[Model] 핫리로드 완료  이전 학습={old_trained} → 새 학습={new_trained}")
    except Exception as e:
        print(f"[Model] 핫리로드 실패: {e}")


# 서버 구동 시 최초 로드
_load_model()


# ──────────────────────────────────────────────────────────
# 힌트 효과성 밴딧 — DB 로드 / 변형 선택
# ──────────────────────────────────────────────────────────

def _load_hint_stats_rows(cursor) -> tuple[list[dict], bool] | None:
    """
    hint_stats 테이블에서 누적 통계 행을 읽어옵니다.

    반환:
        (rows, has_thompson)
            has_thompson=True  → alpha/beta 컬럼이 있는 신규 스키마
            has_thompson=False → 구 스키마 (success_count 만 존재)
        테이블 자체가 없으면 None.
    """
    try:
        cursor.execute(
            "SELECT hint_type, shown_count, success_count, alpha, beta FROM hint_stats"
        )
        return (cursor.fetchall() or []), True
    except Exception as e_new:
        try:
            cursor.execute(
                "SELECT hint_type, shown_count, success_count FROM hint_stats"
            )
            return (cursor.fetchall() or []), False
        except Exception as e_old:
            msg = str(e_old).lower()
            if "hint_stats" in msg or "doesn't exist" in msg:
                return None
            print(f"[HintBandit] 통계 로드 실패(new={e_new} / old={e_old})")
            return None


def _load_hint_stats_from_db() -> None:
    """
    서버 구동 시 hint_stats 테이블에서 밴딧 누적 통계를 메모리에 로드합니다.
    alpha/beta 컬럼이 있으면 Thompson Sampling 파라미터로도 동기화합니다.
    """
    try:
        conn = pymysql.connect(**DB_CONFIG)
    except Exception as e:
        print(f"[HintBandit] DB 연결 실패: {e}")
        return

    try:
        cursor = conn.cursor()
        loaded = _load_hint_stats_rows(cursor)
    finally:
        conn.close()

    if loaded is None:
        print(
            "[HintBandit] hint_stats 테이블 없음 — 빈 통계로 시작 "
            "(server/migrations/scoring_v2.sql 참고)"
        )
        return

    rows, has_thompson = loaded
    thompson_params: dict[str, tuple[float, float]] = {}
    for row in rows:
        _hint_stats[row['hint_type']] = {
            "shown":   row['shown_count'],
            "success": row['success_count'],
        }
        if has_thompson:
            thompson_params[row['hint_type']] = (
                float(row.get('alpha', 1.0) or 1.0),
                float(row.get('beta',  1.0) or 1.0),
            )

    if has_thompson:
        _thompson_bandit.load(thompson_params)
        print(
            f"[HintBandit] {len(_hint_stats)}개 힌트 통계 로드 완료 "
            f"(Thompson 파라미터 {len(thompson_params)}개 동기화)"
        )
    else:
        print(
            f"[HintBandit] {len(_hint_stats)}개 힌트 통계 로드 완료 "
            f"(legacy 스키마 — Thompson 컬럼 없음, 균등 사전분포로 시작)"
        )


def _bandit_select(group_key: str, context: list[float] | None = None) -> tuple[str, str]:
    """
    Contextual Bandit + Thompson Sampling 으로 힌트 변형을 선택합니다.

    선택 흐름:
        ContextualBandit.select(context, candidates)
            → RF 정책 모델이 로드돼 있으면 컨텍스트 기반 예측 보상 최대 변형
            → 모델이 없거나 ε(=0.1) 탐색 발동 시 ThompsonBandit 으로 폴백
        ThompsonBandit.select(candidates)
            → Beta(α,β) 분포에서 1회 샘플링한 값이 가장 큰 변형 선택
            → 표본이 적은 변형은 분산이 커서 자연스럽게 탐색 빈도 증가 (cold-start 강건)

    반환: (hint_text, hint_type_id)
    """
    variants = _HINT_VARIANTS.get(group_key, [])
    if not variants:
        return "코드가 정상 적용되었습니다.", f"succ_unknown_{group_key}"
    if len(variants) == 1:
        return variants[0]

    candidates = [hint_type for (_, hint_type) in variants]
    ctx        = context if context is not None else []

    selected = _contextual_bandit.select(ctx, candidates)
    if not selected:
        return random.choice(variants)

    text = _HINT_VARIANTS_MAP.get(selected)
    if text is None:
        return random.choice(variants)
    return text, selected


_load_hint_stats_from_db()


# ──────────────────────────────────────────────────────────
# Scoring 2.0 헬퍼 — 에러 타입 추출 / 직전 제출 조회 / Thompson DB 동기화
# ──────────────────────────────────────────────────────────

# 감지 가능한 에러 타입 키워드 — 작은 케이스로 통일
_ERROR_TYPE_KEYWORDS: tuple[str, ...] = (
    "syntaxerror", "indentationerror", "taberror",
    "nameerror", "typeerror", "attributeerror",
    "valueerror", "zerodivisionerror", "indexerror", "keyerror",
    "recursionerror", "timeouterror",
)


def _extract_error_type(output_log: str | None) -> str | None:
    """
    output_log 에서 에러 타입 키워드를 소문자로 추출합니다.
    감지 실패 / 빈 로그 → None.
    """
    if not output_log:
        return None
    log = output_log.lower()
    for keyword in _ERROR_TYPE_KEYWORDS:
        if keyword in log:
            return keyword
    return None


def _fetch_prev_submission(cursor, user_pk: int) -> dict | None:
    """
    유저의 직전 제출 1건(가장 최근) 을 반환합니다.
    reward / adoption 계산에 필요한 최소 컬럼만 SELECT.
    오류(컬럼 누락 등) 시 None 반환.
    """
    try:
        cursor.execute(
            """
            SELECT log_id, source_code, output_log, is_success,
                   cluster_rank, hint_type
            FROM   code_logs
            WHERE  user_pk = %s
            ORDER  BY created_at DESC
            LIMIT  1
            """,
            (user_pk,)
        )
        row = cursor.fetchone()
        return row
    except Exception:
        return None


_LEGACY_SUCCESS_REWARD_THRESHOLD = 0.6


def _safe_update_cluster_rank(cursor, conn, log_id: int, cluster_rank: int) -> None:
    """code_logs.cluster_rank 갱신 — 컬럼 없으면 조용히 스킵."""
    try:
        cursor.execute(
            "UPDATE code_logs SET cluster_rank = %s WHERE log_id = %s",
            (cluster_rank, log_id),
        )
        conn.commit()
    except Exception:
        pass


def _safe_update_hint_type(cursor, conn, log_id: int, hint_type: str) -> None:
    """code_logs.hint_type 갱신 + hint_stats 노출 카운트 +1."""
    try:
        cursor.execute(
            "UPDATE code_logs SET hint_type = %s WHERE log_id = %s",
            (hint_type, log_id),
        )
        cursor.execute(
            """
            INSERT INTO hint_stats (hint_type, shown_count, success_count)
            VALUES (%s, 1, 0)
            ON DUPLICATE KEY UPDATE shown_count = shown_count + 1
            """,
            (hint_type,),
        )
        conn.commit()
    except Exception:
        pass


def _safe_update_scoring_v2_columns(
    cursor, conn, log_id: int,
    base_score: float, personal_score: float, adoption_score: float,
    antipattern_pen: float, antipattern_tags: list[str],
    prev_reward: float | None, cur_error_type: str | None,
) -> None:
    """Scoring 2.0 신규 컬럼 일괄 UPDATE — 마이그레이션 전이면 조용히 스킵."""
    tags_str = ",".join(antipattern_tags) if antipattern_tags else None
    try:
        cursor.execute(
            """
            UPDATE code_logs
               SET base_score       = %s,
                   personal_score   = %s,
                   adoption_score   = %s,
                   antipattern_pen  = %s,
                   antipattern_tags = %s,
                   reward           = %s,
                   error_type       = %s
             WHERE log_id = %s
            """,
            (
                base_score, personal_score, adoption_score,
                antipattern_pen, tags_str, prev_reward, cur_error_type, log_id,
            ),
        )
        conn.commit()
    except Exception:
        pass


def _persist_thompson_update(cursor, hint_type: str, reward: float) -> None:
    """
    Thompson 파라미터 (α, β) 를 hint_stats DB 에 영속화합니다.

    신규 스키마(alpha/beta 컬럼 보유) 가 우선이며, 컬럼이 없는 legacy 환경에서는
    reward ≥ _LEGACY_SUCCESS_REWARD_THRESHOLD 일 때만 success_count 를 +1 합니다.
    어떤 단계가 실패해도 호출자 트랜잭션을 깨지 않도록 모든 예외를 흡수합니다.
    """
    a, b = _thompson_bandit.get(hint_type)
    try:
        cursor.execute(
            """
            INSERT INTO hint_stats (hint_type, shown_count, success_count, alpha, beta)
            VALUES (%s, 0, 0, %s, %s)
            ON DUPLICATE KEY UPDATE alpha = VALUES(alpha), beta = VALUES(beta)
            """,
            (hint_type, a, b),
        )
        return
    except Exception:
        pass   # alpha/beta 컬럼이 없는 legacy 스키마 — 아래에서 폴백 시도

    if reward < _LEGACY_SUCCESS_REWARD_THRESHOLD:
        return

    try:
        cursor.execute(
            """
            INSERT INTO hint_stats (hint_type, shown_count, success_count)
            VALUES (%s, 0, 1)
            ON DUPLICATE KEY UPDATE success_count = success_count + 1
            """,
            (hint_type,),
        )
        stats = _hint_stats.setdefault(hint_type, {"shown": 0, "success": 0})
        stats["success"] += 1
    except Exception:
        pass


# ──────────────────────────────────────────────────────────
# 군집 예측 헬퍼 — 모델 핫리로드 포함
# ──────────────────────────────────────────────────────────
def predict_cluster_rank(features: dict, execution_time: float) -> int:
    """
    현재 모델로 코드의 군집 semantic rank(0/1/2)를 예측합니다.

    - 예측 직전에 _maybe_reload_model() 을 호출하여 최신 모델을 보장합니다.
    - 모델 미로드 / 예측 실패 시 -1 을 반환합니다.

    반환값:
        -1 : 모델 없음 또는 예측 오류
         0 : 단순 코드형  (루프 미사용)
         1 : 일반 학습자형 (루프 일부 사용)
         2 : 효율 최적화형 (루프 적극 활용)
    """
    _maybe_reload_model()

    if code_cluster_model is None or scaler is None:
        return -1

    try:
        # score는 학습 피처에서 제외(순환 결합 방지) — kmeans_trainer.py 와 동일하게 맞춤
        feat_with_meta = {**features, 'execution_time': execution_time}
        user_df        = pd.DataFrame([feat_with_meta])
        scaled         = scaler.transform(user_df)

        # 학습 시 적용한 피처 가중치를 추론에도 동일하게 적용 (공간 일치)
        if _feature_weights and len(_feature_weights) == scaled.shape[1]:
            scaled = scaled * np.array(_feature_weights, dtype=float)

        raw_cluster = int(code_cluster_model.predict(scaled)[0])
        return cluster_rank_map.get(raw_cluster, raw_cluster)
    except Exception as e:
        print(f"군집 예측 실패: {e}")
        return -1


# ──────────────────────────────────────────────────────────
# 요청 모델
# ──────────────────────────────────────────────────────────
class CodeSubmitRequest(BaseModel):
    user_id:      str
    machine_type: str    # 실행한 기계 종류 (예: "Miner_Common")
    source_code:  str    # 유저가 작성한 파이썬 코드 원본

    # 제출 시점 자원 보유량 (ML 특징으로 저장)
    res_common:  int = 0
    res_rare:    int = 0
    res_special: int = 0
    res_exotic:  int = 0
    gold:        int = 0

    is_python_valid:  bool   # Unity 서버 A(/execute)의 파이썬 문법 통과 여부
    is_machine_valid: bool   # Unity 클라이언트 측 기계 조건 통과 여부
    is_success:       bool   # 최종 성공 여부 (python_valid AND machine_valid)
    execution_time:   float  # 코드 실행 소요 시간 (초)
    output_log:       str    # 실행 결과 또는 에러 메시지


# ──────────────────────────────────────────────────────────
# 점수 계산 (교육 목표 기반)
# ──────────────────────────────────────────────────────────

def calculate_score(request: CodeSubmitRequest, features: dict) -> float:
    """
    교육 목표 기반 점수 계산.

    [공식]
        base          50점  : 코드가 실행된 것만으로 주어지는 기본 점수
        loop_bonus    +20점 : 반복문(for/while) 1개 이상 사용 시
        efficiency    +10점 : for range(N) 효율 비례 (loop_efficiency × 5, 최대 10)
        while_bonus   +10점 : while 무한루프 사용 시에만
                              일반 while 조건문은 for 와 동급 — 빈도 조절은 loop_balance API 담당
        time_penalty  -25점 : 실행 시간 × 5 (최대 25, 5초 이상 동일 패널티)
        density_bonus  +5점 : 줄당 함수 호출 수 비례 (빽빽하게 쓴 코드 보상)

    [설계 의도]
        - 루프를 안 쓰면 최대 55점 (base + density만)
        - for/while 반복문 사용 시 최대 85점 (loop_efficiency 비례)
        - while True (퀘스트 해금 기능) 사용 시 최대 95점 — 게임 내 특수 기계 동작 보상
          ※ while True 가 유일한 고득점 수단이 아님. 조건부 while 도 for 와 동급으로 평가
    """
    base = 50.0

    loop_bonus = 0.0
    if features['has_loop']:
        loop_bonus += 20.0
        # loop_efficiency = for range(N) 총합 / line_count
        # 예: 4줄에서 range(10) → 10/4=2.5 → +min(10, 2.5*5)=+10
        loop_bonus += min(10.0, features['loop_efficiency'] * 5.0)

    # while True / for i in count() (무한루프) 사용 시에만 보너스
    # 일반 while 조건문(while i < 5 등)은 for 와 동급 취급 — 빈도 균형은 loop_balance API 담당
    while_bonus = 10.0 if features.get('has_infinite_loop', features.get('has_infinite_while', 0)) else 0.0

    # 5초 이상은 동일 페널티로 묶어 지나친 감점 방지
    time_penalty = min(25.0, request.execution_time * 5.0)

    # 줄당 함수 호출 수: 같은 작업을 적은 줄로 표현한 코드를 보상
    density       = features['func_call_count'] / max(1, features['line_count'])
    density_bonus = min(5.0, density * 10.0)

    raw = base + loop_bonus + while_bonus - time_penalty + density_bonus
    return round(max(0.0, min(100.0, raw)), 2)


# ──────────────────────────────────────────────────────────
# 군집 이동 이력 기반 성장/정체 문구 생성
# ──────────────────────────────────────────────────────────

# cluster_rank → 표시 레이블 (성장/정체 문구 + /api/user_cluster_history 공용)
_RANK_LABELS: dict[int, str] = {
    0: "단순 코드형",
    1: "일반 학습자형",
    2: "효율 최적화형",
}

# 정체 감지 시 보여줄 다음 단계 유도 문구
_STAGNATION_NUDGE = {
    # rank 0: 루프 미사용 — 반복문 첫 시도 유도
    0: "for i in range(5): mining() 처럼 반복문을 시작해보세요!",
    # rank 1: 루프 사용 중 — 더 효율적인 구조로 발전 유도
    1: "range()의 숫자를 더 키우거나, while True 로 무한 반복에도 도전해보세요!",
}


def _get_progression_note(cursor, user_pk: int, current_rank: int) -> str:
    """
    직전 제출들의 cluster_rank와 비교해 성장 / 정체 / 하락 문구를 반환합니다.
    반환값은 generate_hint() 결과 뒤에 이어붙입니다.

    감지 케이스:
        성장 (rank ↑) : 이전 rank → 현재 rank 상승 축하
        하락 (rank ↓) : 이전 rank → 현재 rank 하락 경고
                        특히 효율 최적화형(2)에서 내려올 경우 별도 문구
        유지/정체     : 직전 2회 + 현재 = 3연속 동일 rank → 전환 유도
                        단, rank 2(효율 최적화형) 유지는 정체 아님 — 정체 메시지 없음
        데이터 부족   : 빈 문자열 반환 (조용히 처리)

    현재 제출의 cluster_rank 는 아직 DB에 저장되기 전이므로
    이 함수가 읽는 rows 는 순수하게 이전 제출 기록만 포함합니다.
    """
    try:
        cursor.execute(
            """
            SELECT cluster_rank FROM code_logs
            WHERE  user_pk = %s AND is_success = 1 AND cluster_rank >= 0
            ORDER  BY created_at DESC
            LIMIT  5
            """,
            (user_pk,)
        )
        rows = cursor.fetchall()
    except Exception:
        return ""   # cluster_rank 컬럼 없음 등 — 조용히 무시

    if not rows:
        return ""   # 이전 성공 기록 없음

    prev_ranks = [row['cluster_rank'] for row in rows]
    last_rank  = prev_ranks[0]   # 직전 제출의 rank

    cur_label  = _RANK_LABELS.get(current_rank, str(current_rank))
    prev_label = _RANK_LABELS.get(last_rank,    str(last_rank))

    # ── 성장 감지 (rank 상승) ─────────────────────────────────
    if current_rank > last_rank:
        return (
            f"\n[ 성장 중! ] {prev_label}에서 {cur_label}으로 올라섰어요! "
            "이 방향으로 계속 나아가세요."
        )

    # ── 하락 감지 (rank 하락) ─────────────────────────────────
    if current_rank < last_rank:
        if last_rank == 2:
            # 효율 최적화형에서 내려온 경우 — 루프 구조 약화를 명시
            return (
                f"\n[ 효율 하락 ] 이전에는 {prev_label}이었어요. "
                "반복문(for/while)을 더 적극적으로 활용해보세요!"
            )
        return (
            f"\n[ 패턴 단순화 ] 이전보다 코드 구조가 단순해졌어요. "
            "반복문을 계속 활용해보세요!"
        )

    # ── 유지 / 정체 감지 ─────────────────────────────────────
    # rank 2는 이미 최고 효율 코드 — 유지 자체가 좋은 것, 정체 메시지 불필요
    if current_rank == 2:
        return ""

    # 직전 N회 중 연속으로 같은 rank 인 횟수
    consecutive = 0
    for r in prev_ranks:
        if r == current_rank:
            consecutive += 1
        else:
            break

    if consecutive >= 2:   # 직전 2개 + 현재 = 총 3연속
        nudge = _STAGNATION_NUDGE.get(current_rank, "새로운 방식을 시도해보세요.")
        return (
            f"\n[ {cur_label} 유지 중 ] "
            f"{consecutive + 1}번 연속 같은 패턴이에요. {nudge}"
        )

    return ""


# ──────────────────────────────────────────────────────────
# AI 힌트 생성 — 헬퍼 상수 & 함수
# ──────────────────────────────────────────────────────────

# 게임 내 사용 가능한 명령어 목록 (오타 제안에 사용)
_GAME_FUNCTIONS = ["mining", "producting", "move", "name"]

# 자주 오타 나는 파이썬 내장 함수 목록
_PYTHON_BUILTINS = [
    "print", "range", "len", "int", "str", "float", "list",
    "dict", "input", "type", "abs", "sum", "max", "min",
    "round", "sorted", "enumerate", "zip", "map", "filter",
    "True", "False", "None",
    # itertools (화이트리스트로 허용된 심볼) — `from itertools import X` 안내에 활용
    "count",
]

# for / while / if / def … 뒤에 : 가 오는 블록 시작 줄 패턴
_BLOCK_START_RE = re.compile(
    r'^\s*(for|while|if|elif|else|def|class|try|except|finally|with)\b.*:\s*$'
)


def _extract_name_from_nameerror(output_log: str) -> str:
    """NameError 에서 미정의 이름을 원본 대소문자 그대로 추출합니다."""
    m = re.search(r"NameError: name '([^']+)' is not defined", output_log)
    return m.group(1) if m else ""


def _looks_like_unquoted_string(name: str, source_code: str) -> bool:
    """
    해당 이름이 따옴표 없이 문자열 값으로 쓰인 패턴인지 검사합니다.
    예) name = Alice  (따옴표 없음) → NameError: name 'Alice' is not defined
    """
    escaped = re.escape(name)
    patterns = [
        r'=\s*'      + escaped + r'\s*(?:#.*)?$',   # x = Alice
        r'\(\s*'     + escaped + r'\s*\)',            # func(Alice)
        r',\s*'      + escaped + r'\s*[,\)]',         # func(a, Alice)
    ]
    for pat in patterns:
        if re.search(pat, source_code, re.MULTILINE):
            return True
    return False


def _suggest_similar_name(name: str) -> tuple:
    """
    오타일 가능성이 있는 이름에 대해 (제안_이름, 카테고리) 를 반환합니다.
    카테고리: "game" | "builtin" | ""
    """
    lower = name.lower()
    game_matches = difflib.get_close_matches(lower, _GAME_FUNCTIONS, n=1, cutoff=0.6)
    if game_matches:
        return game_matches[0], "game"
    builtin_matches = difflib.get_close_matches(lower, _PYTHON_BUILTINS, n=1, cutoff=0.6)
    if builtin_matches:
        return builtin_matches[0], "builtin"
    return "", ""


def _find_empty_block(source_code: str) -> str:
    """
    블록 헤더(for / while / if … :) 뒤에 들여쓰기된 본문이 없는
    첫 번째 줄의 텍스트(최대 50자)를 반환합니다. 없으면 빈 문자열 반환.
    """
    lines = source_code.split('\n')
    for i, line in enumerate(lines):
        if not _BLOCK_START_RE.match(line):
            continue
        current_indent = len(line) - len(line.lstrip())
        for j in range(i + 1, len(lines)):
            next_line = lines[j]
            if not next_line.strip():
                continue                               # 빈 줄은 건너뜀
            next_indent = len(next_line) - len(next_line.lstrip())
            if next_indent <= current_indent:
                return line.strip()[:50]               # 본문 없음
            break                                      # 정상 들여쓰기 있음
        else:
            return line.strip()[:50]                   # 파일 끝에 본문 없음
    return ""


def _extract_attr_from_attributeerror(output_log: str) -> str:
    """AttributeError 에서 없는 속성명을 추출합니다."""
    m = re.search(
        r"AttributeError: (?:'[^']+' object has no attribute '([^']+)'"
        r"|module '[^']+' has no attribute '([^']+)')",
        output_log,
    )
    if m:
        return m.group(1) or m.group(2) or ""
    return ""


def _extract_fn_from_typeerror(output_log: str) -> str:
    """TypeError: xxx() takes … 패턴에서 함수명을 추출합니다."""
    m = re.search(r"([a-zA-Z_]\w*)\(\) takes", output_log)
    return m.group(1) if m else ""


# ──────────────────────────────────────────────────────────
# move() 함수 — 컨테이너 타일 설치 전용 (Layer 0)
# ──────────────────────────────────────────────────────────
# move() 는 게임 내 컨테이너 타일 설치에만 사용되는 단독 호출 명령어로,
# 반복문(for/while) 안에 넣을 수 없습니다.
#   - 정상 호출 시  → score = 100 만점 부여 (submit_code 에서 처리)
#   - 오타 / 미완성 / 루프 내 사용 시 → Layer 0 힌트로 안내
#
# 감지 패턴:
#   ① 알파벳 변형  : mov / mvoe / moev / moove / movee / moveing 등
#   ② 대소문자 변형: MOVE / Move / MOve / MoVe 등 (move 자체는 제외)
#   ③ 미완성 호출 : "move(" 만 쓰고 ")" 를 빠뜨린 경우
#   ④ 루프 내 사용: for/while 블록 안에 move() 호출이 위치한 경우
# ──────────────────────────────────────────────────────────

# 오타 후보 토큰 — 함수 호출 시도(뒤에 '(' 동반)만 감지하여 문자열/주석 내 단어 오탐 방지
# 정확한 'move(' 는 제외 (단어 경계 + 알파벳 변형 / 대소문자 변형만 매칭)
_MOVE_TYPO_VARIANT_RE = re.compile(
    r'(?<![A-Za-z0-9_])'
    r'(?P<token>'
    r'mov|mvoe|moev|moove|moeve|movee|moveing|moveee'   # 철자 변형 (move 자체는 제외)
    r'|MOVE|Move|MOve|MOVe|MoVe|moVe|movE|mOVE'         # 대소문자 변형 (move 자체는 제외)
    r')'
    r'\s*\('                                            # 반드시 '(' 가 뒤따라야 함
)


def _has_unclosed_move_call(source_code: str) -> bool:
    """
    'move(' 가 있지만 같은 라인 안에서 ')' 로 닫히지 않은 패턴을 감지합니다.
    예) move(            ← 닫는 괄호 누락
        move("fast"      ← 닫는 괄호 누락
    """
    for raw_line in source_code.split('\n'):
        line = re.sub(r'#.*', '', raw_line)        # 라인 주석 제거
        m = re.search(r'\bmove\s*\(', line)
        if not m:
            continue
        depth = 1
        for ch in line[m.end():]:
            if ch == '(':
                depth += 1
            elif ch == ')':
                depth -= 1
                if depth == 0:
                    break
        if depth > 0:
            return True
    return False


def _detect_move_typo(source_code: str) -> str | None:
    """
    move() 함수의 흔한 오타 / 미완성 패턴을 감지하고 안내 메시지를 반환합니다.
    감지 실패 시 None.
    """
    if not source_code:
        return None

    if _has_unclosed_move_call(source_code):
        return (
            "'move(' 의 닫는 괄호 ')' 가 빠진 것 같아요!\n"
            "컨테이너 타일은 'move()' 처럼 빈 괄호로 정확히 입력해야 해요."
        )

    m = _MOVE_TYPO_VARIANT_RE.search(source_code)
    if m:
        token = m.group('token')
        return (
            f"'{token}' 은(는) 'move()' 의 오타로 보여요!\n"
            "컨테이너 타일을 설치하려면 정확히 'move()' 라고 입력해주세요."
        )

    return None


def _ast_contains_move_call(source_code: str) -> bool:
    """AST 파싱 후 move(...) 호출이 한 번이라도 나타나는지 검사합니다."""
    if not source_code or 'move' not in source_code:
        return False
    try:
        tree = ast.parse(source_code)
    except SyntaxError:
        return False
    for node in ast.walk(tree):
        if (isinstance(node, ast.Call)
                and isinstance(node.func, ast.Name)
                and node.func.id == 'move'):
            return True
    return False


def _move_in_loop(source_code: str) -> bool:
    """move() 호출이 for / while 블록 내부에 위치하는지 검사합니다."""
    if not source_code or 'move' not in source_code:
        return False
    try:
        tree = ast.parse(source_code)
    except SyntaxError:
        return False
    for node in ast.walk(tree):
        if not isinstance(node, (ast.For, ast.While)):
            continue
        for child in ast.walk(node):
            if (isinstance(child, ast.Call)
                    and isinstance(child.func, ast.Name)
                    and child.func.id == 'move'):
                return True
    return False


def _is_move_standalone(source_code: str, features: dict) -> bool:
    """
    move() 가 반복문 없이 단독으로 호출되었는지 검사합니다.
    True 면 submit_code 에서 score = 100 만점을 부여합니다.
    """
    if features.get('has_loop', 0):
        return False
    return _ast_contains_move_call(source_code)


# ──────────────────────────────────────────────────────────
# AI 힌트 생성 (3단계 폭포수 구조)
# ──────────────────────────────────────────────────────────

# 기계 타입별 필수 함수 목록
# 새로운 기계 추가 시 이 딕셔너리에만 추가하면 됩니다.
REQUIRED_FUNCTIONS: dict[str, list[str]] = {
    "Miner_Common": ["mining()"],
}


def generate_hint(request: CodeSubmitRequest, score: float, features: dict,
                  cluster_rank: int = -1) -> str:
    """
    제출 결과에 따라 단계별로 힌트를 생성합니다.

    0단계 (move() 전용)               : 컨테이너 타일 설치용 move() 의 오타 / 미완성 /
                                        반복문 내 사용을 가장 먼저 감지 (Layer 0)
    1단계 (is_python_valid == False) : 파이썬 에러 유형별 세분화 힌트
    2단계 (is_machine_valid == False): 기계 조건 미충족 힌트
    3단계 (성공)                      : _generate_hint_typed() 의 Contextual+Thompson 밴딧이 담당
                                        (이 함수는 0·1·2단계만 처리)

    주의: 8000.py 의 format_error_user() 는 IndentationError / TabError 포함
          모든 SyntaxError 계열을 "SyntaxError: {msg}" 로 포맷합니다.
          따라서 error_log 에서 "indentationerror" 문자열은 등장하지 않으며,
          들여쓰기 오류는 SyntaxError 메시지 내용(unexpected indent 등)으로 판별합니다.
    """

    # ══════════════════════════════════════════════════════
    # 0단계: move() 함수 전용 검사 (컨테이너 타일 설치)
    #   - 오타 (mov, MOVE, moove …)
    #   - 미완성 호출 (move( 만 입력)
    #   - 반복문 안에서 호출
    # 일반 NameError / SyntaxError 안내보다 우선 표시되어 더 구체적인 안내를 제공합니다.
    # ══════════════════════════════════════════════════════
    move_typo_msg = _detect_move_typo(request.source_code)
    if move_typo_msg:
        return move_typo_msg

    if _move_in_loop(request.source_code):
        return (
            "move() 는 컨테이너 타일을 설치하는 단독 명령어예요!\n"
            "for / while 반복문 안에서는 사용할 수 없어요."
        )

    # ══════════════════════════════════════════════════════
    # 1단계: 파이썬 문법 / 런타임 에러
    # ══════════════════════════════════════════════════════
    if not request.is_python_valid:
        log       = request.output_log          # 원본 (대소문자 유지, regex 추출용)
        error_log = log.lower()                  # 소문자 검색용
        source    = request.source_code

        # ── Sandbox 보안 차단 ───────────────────────────
        # server/8000.py 의 SecurityVisitor 가 "보안: 외부 모듈 사용 금지 (X.Y)"
        # 형식으로 던지는 메시지를 캐치. 화이트리스트(itertools.count) 외엔 모두 차단됨.
        if "보안: 외부 모듈 사용 금지" in log or "외부 모듈 사용 금지" in log:
            m = re.search(r"외부 모듈 사용 금지\s*\(([^)]+)\)", log)
            target = m.group(1) if m else None
            if target:
                return (
                    f"'{target}' 모듈은 사용할 수 없어요.\n"
                    "이 게임에서 허용된 외부 모듈은 itertools.count 뿐이에요. "
                    "예) from itertools import count"
                )
            return (
                "외부 모듈 import 는 사용할 수 없어요!\n"
                "허용된 항목: from itertools import count"
            )
        if "금지 함수 사용" in log:
            m = re.search(r"금지 함수 사용:\s*(\S+)", log)
            target = m.group(1) if m else "해당 함수"
            return (
                f"'{target}' 는 보안상 사용할 수 없는 함수예요.\n"
                "다른 방법으로 동일한 동작을 만들어보세요."
            )

        # ── SyntaxError 계열 ─────────────────────────────
        # (IndentationError / TabError 도 8000.py 에서 SyntaxError: 로 포맷됨)
        if "syntaxerror" in error_log:

            # 들여쓰기 없는 블록 본문 (IndentationError: expected an indented block)
            if "expected an indented block" in error_log:
                problem_line = _find_empty_block(source)
                if problem_line:
                    return (
                        f"'{problem_line}' 아래에 실행할 코드가 없어요!\n"
                        "콜론(:) 다음 줄을 4칸 들여쓰기 후 명령어를 써주세요."
                    )
                return (
                    "콜론(:) 뒤에 실행할 코드 블록이 없어요.\n"
                    "들여쓰기(4칸) 후 명령어를 추가해보세요."
                )

            # 불필요한 들여쓰기 (IndentationError: unexpected indent)
            if "unexpected indent" in error_log:
                return (
                    "들여쓰기가 필요 없는 곳에 빈칸이 들어가 있어요!\n"
                    "코드 앞의 불필요한 공백을 지워주세요."
                )

            # 탭/스페이스 혼용 (TabError)
            if "inconsistent use of tabs" in error_log or "taberror" in error_log:
                return (
                    "탭(Tab)과 스페이스를 함께 쓰면 안 돼요!\n"
                    "들여쓰기를 모두 스페이스 4칸으로 통일해보세요."
                )

            # 괄호 미닫기
            if ("unexpected eof" in error_log
                    or "never closed" in error_log
                    or "was never closed" in error_log):
                return "괄호 '(' 또는 '[' 를 열고 닫지 않았는지 확인해보세요!"

            # 문자열 미닫기
            if ("unterminated string" in error_log
                    or "eol while scanning" in error_log):
                return "따옴표('' 또는 \"\")를 열고 닫지 않았는지 확인해보세요!"

            # return / break / continue 오용
            if "return outside function" in error_log:
                return (
                    "return 은 def 로 만든 함수 안에서만 쓸 수 있어요!\n"
                    "함수 정의(def) 없이 return 만 쓰지는 않았나요?"
                )
            if "break outside loop" in error_log:
                return "break 는 for / while 반복문 안에서만 쓸 수 있어요!"
            if "continue outside loop" in error_log:
                return "continue 는 for / while 반복문 안에서만 쓸 수 있어요!"

            # = 과 == 혼동
            if ("cannot assign to" in error_log
                    or "maybe you meant '=='" in error_log):
                return (
                    "조건식에서는 비교 연산자 == 을 써야 해요.\n"
                    "대입(=)과 비교(==)를 헷갈린 건 아닌가요? 예) if a == 5:"
                )

            # 보이지 않는 특수문자 (다른 곳에서 복붙 시)
            if "invalid character" in error_log:
                return (
                    "코드에 보이지 않는 특수문자가 섞여 있어요!\n"
                    "다른 곳에서 복사·붙여넣기 했다면 직접 다시 입력해보세요."
                )

            # f-string 오류
            if "f-string" in error_log:
                return (
                    "f-string 문법 오류예요.\n"
                    "f\"...{변수이름}...\" 형식인지, 중괄호 {} 가 제대로 닫혔는지 확인해보세요."
                )

            # 일반 SyntaxError 폴백
            return "명령어에 오타가 있거나, 조건문·반복문 뒤에 콜론(:)을 빠뜨렸을지도 몰라요!"

        # ── NameError ─────────────────────────────────────
        if "nameerror" in error_log:
            undef = _extract_name_from_nameerror(log)

            if undef:
                # 0순위: itertools import 없이 count() 호출
                if undef == "count" and not re.search(
                    r'from\s+itertools\s+import\s+[^#\n]*\bcount\b', source
                ):
                    return (
                        "'count' 를 사용하려면 먼저 import 해야 해요!\n"
                        "코드 맨 윗줄에 다음을 추가해보세요:\n"
                        "from itertools import count"
                    )

                # 1순위: 기계 이름 필드에 따옴표를 빠뜨린 경우
                if re.search(r'\bname\s*=\s*' + re.escape(undef), source):
                    return (
                        f"기계 이름 '{undef}' 을(를) 따옴표로 감싸지 않았어요!\n"
                        f'name = "{undef}" 처럼 수정해보세요.'
                    )

                # 2순위: 일반적인 따옴표 없는 문자열 값
                if _looks_like_unquoted_string(undef, source):
                    return (
                        f"'{undef}' 을(를) 텍스트로 쓰려면 따옴표로 감싸야 해요!\n"
                        f'예시: 변수 = "{undef}"'
                    )

                # 3순위: 오타 제안 (게임 명령어 / 파이썬 내장 함수)
                suggestion, category = _suggest_similar_name(undef)
                if suggestion:
                    ctx = "게임 명령어" if category == "game" else "파이썬 기본 명령어"
                    return (
                        f"'{undef}' 를 찾을 수 없어요.\n"
                        f"{ctx} '{suggestion}' 의 오타는 아닌가요?"
                    )

            return "존재하지 않는 명령어(또는 변수)를 불렀어요. 오타가 발생했는지 확인해보세요!"

        # ── TypeError ─────────────────────────────────────
        if "typeerror" in error_log:
            # 문자열 + 숫자 연결 시도
            if ("can only concatenate str" in error_log
                    or "must be str, not" in error_log):
                return (
                    "문자열(글자)과 숫자를 바로 + 로 연결할 수 없어요!\n"
                    "str(숫자) 로 변환 후 합쳐보세요. 예) \"결과: \" + str(5)"
                )

            # 인자 개수 오류
            if "takes" in error_log and "argument" in error_log:
                fn_name = _extract_fn_from_typeerror(log)
                if fn_name:
                    return (
                        f"'{fn_name}()' 에 잘못된 개수의 값을 넣었어요.\n"
                        "괄호 안에 값이 필요 없는 명령어일 수도 있어요! 예) mining()"
                    )
                return (
                    "함수에 잘못된 개수의 인자를 전달했어요.\n"
                    "괄호 안의 값 개수를 확인해보세요."
                )

            # None 값 오용
            if "'nonetype'" in error_log:
                return (
                    "결과값이 없는(None) 값을 사용하려 했어요.\n"
                    "함수의 반환값이 있는지, 변수에 제대로 저장했는지 확인해보세요."
                )

            # 대괄호 접근 불가
            if "not subscriptable" in error_log:
                return (
                    "대괄호([])로 접근할 수 없는 값이에요.\n"
                    "리스트(list)나 딕셔너리(dict)가 맞는지 확인해보세요."
                )

            # range() 에 문자열 전달
            if "cannot be interpreted as an integer" in error_log:
                return (
                    "range() 안에는 정수(숫자)만 넣을 수 있어요.\n"
                    "문자열이 들어가지는 않았나요? 예) range(5)"
                )

            return "타입 에러가 발생했어요. 숫자가 들어갈 자리에 문자열(글자)을 넣지는 않았나요?"

        # ── AttributeError ───────────────────────────────
        if "attributeerror" in error_log:
            attr = _extract_attr_from_attributeerror(log)
            if attr:
                suggestion, category = _suggest_similar_name(attr)
                if suggestion:
                    ctx = "게임 명령어" if category == "game" else "파이썬 기본 명령어"
                    return (
                        f"'{attr}' 를 찾을 수 없어요.\n"
                        f"{ctx} '{suggestion}' 의 오타는 아닌가요?"
                    )
                return f"'{attr}' 는 존재하지 않는 속성이에요. 오타인지 확인해보세요!"
            return "존재하지 않는 속성이나 메서드를 불렀어요. 오타가 없는지 확인해보세요!"

        # ── ValueError ────────────────────────────────────
        if "valueerror" in error_log:
            if "invalid literal" in error_log and "int()" in error_log:
                return (
                    "숫자로 변환할 수 없는 값을 int()에 넣었어요.\n"
                    "숫자로만 이루어진 문자열인지 확인해보세요. 예) int(\"123\")"
                )
            return (
                "명령어의 형식은 맞지만, 올바르지 않은 값이 들어갔어요.\n"
                "정확한 값을 입력했는지 확인해보세요."
            )

        # ── ZeroDivisionError ─────────────────────────────
        if "zerodivisionerror" in error_log:
            return (
                "0으로 나누기를 시도했어요!\n"
                "나누는 수(분모)가 0이 되지 않도록 코드를 확인해보세요."
            )

        # ── IndexError ────────────────────────────────────
        if "indexerror" in error_log:
            return (
                "리스트의 범위를 벗어난 위치에 접근했어요.\n"
                "인덱스 번호가 리스트 길이(len())를 넘지 않는지 확인해보세요."
            )

        # ── KeyError ─────────────────────────────────────
        if "keyerror" in error_log:
            m = re.search(r"KeyError: (.+)", log)
            key_name = m.group(1).strip() if m else ""
            if key_name:
                return (
                    f"딕셔너리에 {key_name} 키가 없어요!\n"
                    "키 이름의 오타나 존재 여부를 확인해보세요."
                )
            return (
                "딕셔너리에 없는 키에 접근했어요.\n"
                "키 이름의 오타나 존재 여부를 확인해보세요."
            )

        # ── RecursionError ───────────────────────────────
        if "recursionerror" in error_log:
            return (
                "함수가 자기 자신을 너무 많이 호출했어요(재귀 깊이 초과)!\n"
                "함수 안에서 같은 함수를 계속 부르지는 않았나요?"
            )

        # ── TimeoutError ─────────────────────────────────
        if "timeouterror" in error_log:
            return (
                "코드 실행 시간이 너무 오래 걸려요!\n"
                "끝나지 않는 무한 루프에 빠진 건 아닌지 확인해보세요."
            )

        # ── 알 수 없는 에러 폴백 ─────────────────────────
        return (
            "기계가 미지의 파이썬 에러를 뿜어내고 있습니다.\n"
            "로그 창의 에러 메시지를 번역해서 문제를 해결해보세요!"
        )

    # ══════════════════════════════════════════════════════
    # 2단계: 기계별 조건 미충족 (Unity client/-1..-9 매핑과 결을 맞춤)
    # ══════════════════════════════════════════════════════
    if not request.is_machine_valid:
        clean = request.source_code.replace(" ", "")

        # 게임 기믹: 무한 루프 미해금 상태에서 while True / for in count() 시도
        # (클라이언트가 -3 으로 차단하지만, 서버 hint 도 같은 사유로 응답)
        if features.get('has_infinite_loop', 0) or features.get('has_infinite_while', 0):
            return (
                "아직 '무한 루프' 시스템 권한이 잠겨 있어요!\n"
                "while True / for i in count(): 는 게임을 더 진행해 해금 후 사용 가능해요. "
                "지금은 for i in range(N): 으로 횟수 반복을 사용해보세요."
            )

        # 게임 기믹: 일반 루프(level 1) 미해금 상태에서 for/while 시도
        if features.get('has_loop', 0):
            return (
                "아직 '반복문' 시스템 권한이 잠겨 있어요!\n"
                "퀘스트를 더 진행해 for / while 권한을 해금한 뒤 사용해보세요."
            )

        # 기계 이름 누락
        if "name=" not in clean:
            return (
                "기계를 작동시키려면 먼저 이름을 지어줘야 해요!\n"
                "코드 맨 윗줄에 name = \"이름\" 을 추가해보세요."
            )

        # REQUIRED_FUNCTIONS 딕셔너리 기반 체크 — 기계 추가 시 위 딕셔너리만 수정
        for fn in REQUIRED_FUNCTIONS.get(request.machine_type, []):
            if fn.replace(" ", "") not in clean:
                return (
                    f"이 기계는 {fn} 명령어가 필요합니다.\n"
                    "다른 명령어를 입력하지는 않았나요?"
                )

        return "문법은 맞았지만, 이 기계가 수행할 수 없는 명령입니다."

    # 성공 힌트(3단계)는 _generate_hint_typed() 의 Contextual+Thompson 밴딧이 처리합니다.
    # 이 함수는 is_python_valid=False 또는 is_machine_valid=False 일 때만 호출됩니다.


# ──────────────────────────────────────────────────────────
# 힌트 타입 추론 — 에러/기계 힌트 분류 (밴딧 통계 태깅용)
# ──────────────────────────────────────────────────────────

def _infer_hint_type(request: CodeSubmitRequest, features: dict, cluster_rank: int) -> str | None:
    """
    generate_hint() 의 분기 로직과 동일한 기준으로 hint_type 문자열을 결정합니다.
    성공 케이스(rank 0/1/2)는 _generate_hint_typed() 내 밴딧이 처리하므로 None 반환.
    """
    # ── Layer 0: move() 전용 분류 ───────────────────────────
    if _detect_move_typo(request.source_code) is not None:
        return "move_typo"
    if _move_in_loop(request.source_code):
        return "move_in_loop"

    if not request.is_python_valid:
        log = request.output_log.lower()
        if "외부 모듈 사용 금지" in log: return "err_sandbox_import"
        if "금지 함수 사용"      in log: return "err_sandbox_fn"
        if "syntaxerror" in log:
            if "expected an indented block"       in log: return "err_syntax_indent_expected"
            if "unexpected indent"                in log: return "err_syntax_unexpected_indent"
            if "inconsistent use of tabs"         in log \
                    or "taberror"                 in log: return "err_syntax_tab"
            if "unexpected eof"                   in log \
                    or "never closed"             in log: return "err_syntax_unclosed_paren"
            if "unterminated string"              in log \
                    or "eol while scanning"       in log: return "err_syntax_unclosed_string"
            if "return outside function"          in log: return "err_syntax_return_outside"
            if "break outside loop"               in log: return "err_syntax_break_outside"
            if "continue outside loop"            in log: return "err_syntax_continue_outside"
            if "cannot assign to"                 in log \
                    or "maybe you meant '=='"     in log: return "err_syntax_assign_compare"
            if "invalid character"                in log: return "err_syntax_invalid_char"
            if "f-string"                         in log: return "err_syntax_fstring"
            return "err_syntax_generic"
        if "nameerror"        in log: return "err_name"
        if "typeerror"        in log: return "err_type"
        if "attributeerror"   in log: return "err_attr"
        if "valueerror"       in log: return "err_value"
        if "zerodivisionerror" in log: return "err_zerodiv"
        if "indexerror"       in log: return "err_index"
        if "keyerror"         in log: return "err_key"
        if "recursionerror"   in log: return "err_recursion"
        if "timeouterror"     in log: return "err_timeout"
        return "err_unknown"

    if not request.is_machine_valid:
        clean = request.source_code.replace(" ", "")
        if features.get('has_infinite_loop', 0) or features.get('has_infinite_while', 0):
            return "machine_locked_infinite"
        if features.get('has_loop', 0):
            return "machine_locked_loop"
        if "name=" not in clean:
            return "machine_no_name"
        for fn in REQUIRED_FUNCTIONS.get(request.machine_type, []):
            if fn.replace(" ", "") not in clean:
                return "machine_missing_fn"
        return "machine_generic"

    # 성공 케이스 — _generate_hint_typed() 내 밴딧이 처리
    return None


def _generate_hint_typed(
    request: CodeSubmitRequest, score: float,
    features: dict, cluster_rank: int,
    context: list[float] | None = None,
) -> tuple[str, str]:
    """
    힌트 텍스트와 hint_type ID를 함께 반환합니다.

    Layer 0 (move 전용)  : 오타 / 미완성 / 루프 내 사용 감지 — 성공/실패 무관 최우선
    성공 힌트(move 단독): "succ_move" — 컨테이너 타일 설치 만점 케이스
    성공 힌트(rank 0/1) : Contextual Bandit + Thompson Sampling 으로 변형 선택
                          (context 인자가 있으면 RF 정책 사용, 없으면 Thompson 단독)
    성공 힌트(rank 2)   : 단일 최상위 메시지 반환
    에러 / 기계 힌트    : 기존 generate_hint() 로직 유지 + 타입 태그 부여
    """
    # ══════════════════════════════════════════════════════
    # Layer 0: move() 전용 — 성공/실패 모든 경로에서 최우선 평가
    # (오타 / 미완성 / 루프 내 사용은 일반 힌트 / 밴딧을 우회)
    # ══════════════════════════════════════════════════════
    move_typo_msg = _detect_move_typo(request.source_code)
    if move_typo_msg:
        return move_typo_msg, "move_typo"

    if _move_in_loop(request.source_code):
        return (
            "move() 는 컨테이너 타일을 설치하는 단독 명령어예요!\n"
            "for / while 반복문 안에서는 사용할 수 없어요.",
            "move_in_loop",
        )

    if request.is_python_valid and request.is_machine_valid:
        # ── Layer 0-success: move() 단독 호출 — 컨테이너 타일 설치 만점 ────
        # 반복문 없이 move() 만 호출된 경우 클러스터/밴딧을 거치지 않고 고정 메시지를 반환합니다.
        if _is_move_standalone(request.source_code, features):
            return (
                "[ 컨테이너 타일 ] "
                "move() 명령으로 컨테이너 타일을 설치했어요! "
                "단독 호출 전용 명령이라 만점(100점) 처리됩니다.",
                "succ_move",
            )

        if cluster_rank == 0:
            group = "succ_r0_has_if" if features.get('if_count', 0) > 0 else "succ_r0_simple"
            return _bandit_select(group, context)
        if cluster_rank == 1:
            group = (
                "succ_r1_while"
                if (features.get('while_count', 0) > 0
                    and not features.get('has_infinite_while', 0))
                else "succ_r1_for"
            )
            return _bandit_select(group, context)
        if cluster_rank == 2:
            # rank 2 는 "무한 자동화" 형태에 따라 3가지 풀로 라우팅:
            #   while True:           → succ_r2_infinite_while  (대표적 무한 자동화)
            #   for i in count(...):  → succ_r2_infinite_for_count (itertools 활용 파이써닉)
            #   for range 만 사용      → succ_r2_for_range (큰 N 고효율 유한 루프)
            if features.get('has_infinite_for', 0):
                return _bandit_select("succ_r2_infinite_for_count", context)
            if features.get('has_infinite_while', 0):
                return _bandit_select("succ_r2_infinite_while", context)
            return _bandit_select("succ_r2_for_range", context)
        # cluster_rank == -1 (모델 미로드)
        return (
            "코드가 정상 적용되었습니다. 반복문을 활용하면 더 높은 점수를 받을 수 있어요!",
            "succ_unknown",
        )

    # 에러 / 기계 힌트 — 기존 로직 재사용
    hint_text = generate_hint(request, score, features, cluster_rank)
    hint_type = _infer_hint_type(request, features, cluster_rank) or "hint_unknown"
    return hint_text, hint_type


# ──────────────────────────────────────────────────────────
# 엔드포인트 1: 코드 제출 결과 저장 및 AI 힌트 반환
# ──────────────────────────────────────────────────────────
@app.post("/api/submit_code")
async def submit_code(request: CodeSubmitRequest):
    """
    Unity 클라이언트가 코드 실행 후 호출합니다.
    성공/실패 여부에 관계없이 항상 code_logs 에 기록하고,
    AI 힌트와 점수를 응답합니다.

    파이프라인 (Scoring 2.0):
        ① extract_features + calculate_score → base_score
        ② predict_cluster_rank → cluster_rank
        ③ Layer 1: personal_delta + antipattern_pen 산출
        ④ 직전 제출 조회 → adoption_score & reward 계산 → Thompson 업데이트
        ⑤ aggregator.final_score → 가중합으로 최종 score
        ⑥ Contextual Bandit 으로 힌트 변형 선택
        ⑦ INSERT + 신규 컬럼 graceful UPDATE
    """
    conn   = pymysql.connect(**DB_CONFIG)
    cursor = conn.cursor()
    try:
        # ─── ① 피처 / base 점수 / 군집 예측 ─────────────────────
        features   = extract_features(request.source_code)
        base_score = calculate_score(request, features)

        cluster_rank = predict_cluster_rank(features, request.execution_time) \
                       if request.is_success else -1
        # KMeans 가 if/elif·다중 함수 호출 복잡도 때문에 루프 없는 코드를
        # '일반 학습자형'으로 오분류하는 현상을 방지하는 보정 규칙.
        if cluster_rank > 0 and features.get('has_loop', 0) == 0:
            cluster_rank = 0

        ast_complexity = calculate_ast_complexity(request.source_code)
        cur_error_type = (
            _extract_error_type(request.output_log)
            if not request.is_python_valid else None
        )

        # ─── 유저 조회 ───────────────────────────────────────────
        search_id = "guest" if request.user_id.lower() == "guest" else request.user_id
        cursor.execute("SELECT pk_id FROM users WHERE id = %s", (search_id,))
        user_record = cursor.fetchone()
        if not user_record:
            raise HTTPException(status_code=404, detail=f"'{search_id}' 유저를 찾을 수 없습니다.")
        user_pk = user_record['pk_id']

        # ─── ③ Layer 1: 개인 성장 + 안티패턴 ──────────────────────
        personal_score = personal_delta_score(cursor, user_pk, base_score)
        antipattern_pen, antipattern_tags = antipattern_penalty(request.source_code)

        # ─── ④ 직전 제출 기반 adoption + reward 계산 ─────────────
        adoption_score = 50.0   # 신규 유저 / 직전 정보 없음 → 중립
        prev_reward: float | None = None
        prev = _fetch_prev_submission(cursor, user_pk)

        if prev and prev.get('hint_type'):
            try:
                prev_features = extract_features(prev.get('source_code') or "")
            except Exception:
                prev_features = {}

            try:
                adoption_score = compute_adoption(
                    prev_features, features, prev.get('hint_type')
                )
            except Exception:
                adoption_score = 50.0

            prev_was_error  = not bool(prev.get('is_success'))
            prev_error_type = (
                _extract_error_type(prev.get('output_log'))
                if prev_was_error else None
            )

            try:
                prev_reward = compute_reward(
                    prev_features, features, prev.get('hint_type'),
                    int(prev.get('cluster_rank', -1) or -1),
                    int(cluster_rank),
                    prev_was_error, request.is_success,
                    cur_error_type, prev_error_type,
                )
            except Exception as e:
                print(f"[Reward] 계산 실패: {e}")
                prev_reward = None

            # 같은 종류 에러 재발 → antipattern_pen 에 +20 (force tag)
            rec_pen = error_recurrence_penalty(cur_error_type, prev_error_type)
            if rec_pen > 0:
                antipattern_pen += rec_pen
                antipattern_tags.append("error_recurrence")

            # Thompson 업데이트 + DB 영속화
            if prev_reward is not None:
                _contextual_bandit.update(prev.get('hint_type'), prev_reward)
                _persist_thompson_update(cursor, prev.get('hint_type'), prev_reward)
                try:
                    conn.commit()
                except Exception:
                    pass

        # ─── ⑤ Aggregator: 최종 점수 가중합 ──────────────────────
        score = final_score(base_score, personal_score, adoption_score, antipattern_pen)

        # ─── ⑤-bis: move() 단독 호출 — 컨테이너 타일 만점 오버라이드 ──
        # move() 는 반복문 불가 + 단독 사용 함수라 정상 호출 자체로 만점 부여.
        # base_score 도 함께 100 으로 맞춰 응답 / score_breakdown 일관성을 보장합니다.
        is_move_standalone_call = (
            request.is_success
            and _is_move_standalone(request.source_code, features)
        )
        if is_move_standalone_call:
            base_score = 100.0
            score      = 100.0

        # ─── ⑥ Contextual Bandit 으로 힌트 선택 ───────────────────
        try:
            user_context = encode_user_context(cursor, user_pk)
        except Exception:
            user_context = None
        ai_hint, hint_type = _generate_hint_typed(
            request, score, features, cluster_rank, context=user_context,
        )

        if cluster_rank >= 0:
            progression_note = _get_progression_note(cursor, user_pk, cluster_rank)
            if progression_note:
                ai_hint += progression_note

        # ─── ⑦ INSERT 기본 컬럼 ───────────────────────────────────
        cursor.execute(
            """
            INSERT INTO code_logs
                (user_pk, machine_type, source_code, is_success, output_log,
                 execution_time, score, ast_complexity,
                 res_common, res_rare, res_special, res_exotic, gold, created_at)
            VALUES (%s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, NOW())
            """,
            (
                user_pk,
                request.machine_type, request.source_code,
                request.is_success, request.output_log,
                request.execution_time, score, ast_complexity,
                request.res_common, request.res_rare,
                request.res_special, request.res_exotic, request.gold,
            )
        )
        # commit 전에 lastrowid 저장 (pymysql 일부 버전 안전성)
        inserted_id = cursor.lastrowid
        conn.commit()

        if cluster_rank >= 0:
            _safe_update_cluster_rank(cursor, conn, inserted_id, cluster_rank)

        _hint_stats.setdefault(hint_type, {"shown": 0, "success": 0})["shown"] += 1
        _safe_update_hint_type(cursor, conn, inserted_id, hint_type)

        _safe_update_scoring_v2_columns(
            cursor, conn, inserted_id,
            base_score, personal_score, adoption_score,
            antipattern_pen, antipattern_tags, prev_reward, cur_error_type,
        )

        # ─── ⑧ 루프 균형 분석 → 임밸런스 고장 / 복구 신호 ──────────
        # 현재 제출 INSERT 이후에 호출되어 최신 통계를 반영합니다.
        # is_success=1 만 집계하므로 실패 제출에는 자연스럽게 무영향.
        #
        # sample_size=10 — 회복 후 재트리거가 16+ 제출이나 걸리는 문제를 막기 위해
        # 실시간 트리거용 윈도우는 짧게 둡니다.
        # /api/user_loop_balance 는 통계 조회용으로 기본 20 유지.
        try:
            balance = _compute_loop_balance(cursor, user_pk, sample_size=10)
        except Exception as e:
            print(f"[LoopBalance] 분석 실패: {e}")
            balance = {
                "should_break_machine": False,
                "is_balance_fixed":     True,
                "consumed_part_type":   "for",
                "imbalance_score":      0.0,
            }

        return {
            "status":           "success",
            "score":            score,
            "hint":             ai_hint,
            "cluster_rank":     cluster_rank,
            # Scoring 2.0 — 응답에도 분해된 정보 노출 (Unity 디버깅 / 프런트 시각화용)
            "base_score":       base_score,
            "personal_score":   personal_score,
            "adoption_score":   adoption_score,
            "antipattern_pen":  antipattern_pen,
            "antipattern_tags": antipattern_tags,
            "hint_type":        hint_type,
            # 루프 균형 — 클라이언트의 임밸런스 고장 시스템 트리거용
            "should_break_machine": balance.get("should_break_machine", False),
            "is_balance_fixed":     balance.get("is_balance_fixed", True),
            "consumed_part_type":   balance.get("consumed_part_type", "for"),
            "imbalance_score":      balance.get("imbalance_score", 0.0),
        }

    except Exception as e:
        print(f"에러 발생: {e}")
        raise HTTPException(status_code=500, detail=str(e))
    finally:
        conn.close()


# ──────────────────────────────────────────────────────────
# 엔드포인트 2: 유저 군집 이동 이력 조회
# ──────────────────────────────────────────────────────────

@app.get("/api/user_cluster_history/{user_id}")
async def get_user_cluster_history(user_id: str, limit: int = 10):
    """
    유저의 최근 성공 제출에서 군집 이동 이력을 반환합니다.

    사전 조건:
        ALTER TABLE code_logs ADD COLUMN cluster_rank INT DEFAULT -1;

    반환 필드:
        history          : [{created_at, cluster_rank, rank_label, score}, ...]
                           최신 순 → 오래된 순
        rank_distribution: 군집별 제출 횟수 {0: N, 1: N, 2: N}
        current_rank     : 가장 최근 유효 군집 rank (-1 이면 아직 없음)
        trend            : "improving" | "stable" | "declining" | "insufficient_data"
                           최근 절반 vs 앞 절반 평균 rank 비교
        consecutive_same : 최근 제출에서 같은 rank 가 연속된 횟수 (정체 신호)
    """
    conn   = pymysql.connect(**DB_CONFIG)
    cursor = conn.cursor()
    try:
        cursor.execute("SELECT pk_id FROM users WHERE id = %s", (user_id,))
        user_record = cursor.fetchone()
        if not user_record:
            raise HTTPException(status_code=404, detail=f"'{user_id}' 유저를 찾을 수 없습니다.")

        cursor.execute(
            """
            SELECT cluster_rank, score, created_at
            FROM   code_logs
            WHERE  user_pk = %s AND is_success = 1 AND cluster_rank >= 0
            ORDER  BY created_at DESC
            LIMIT  %s
            """,
            (user_record['pk_id'], limit)
        )
        rows = cursor.fetchall()
    except Exception as e:
        # cluster_rank 컬럼이 없는 경우 마이그레이션 안내 반환
        if "cluster_rank" in str(e).lower() or "unknown column" in str(e).lower():
            raise HTTPException(
                status_code=503,
                detail="cluster_rank 컬럼이 없습니다. "
                       "ALTER TABLE code_logs ADD COLUMN cluster_rank INT DEFAULT -1; 를 실행하세요."
            )
        raise
    finally:
        conn.close()

    if not rows:
        return {
            "status":            "no_data",
            "message":           "군집 예측이 기록된 성공 제출이 없습니다.",
            "history":           [],
            "rank_distribution": {0: 0, 1: 0, 2: 0},
            "current_rank":      -1,
            "trend":             "insufficient_data",
            "consecutive_same":  0,
        }

    history = [
        {
            "created_at":   str(row['created_at']),
            "cluster_rank": row['cluster_rank'],
            "rank_label":   _RANK_LABELS.get(row['cluster_rank'], "알 수 없음"),
            "score":        round(row['score'], 2),
        }
        for row in rows
    ]

    ranks = [r['cluster_rank'] for r in history]

    # 군집별 제출 횟수
    dist = {0: ranks.count(0), 1: ranks.count(1), 2: ranks.count(2)}

    # 최근 연속 동일 rank 횟수 (정체 감지)
    consecutive_same = 1
    for i in range(1, len(ranks)):
        if ranks[i] == ranks[0]:
            consecutive_same += 1
        else:
            break

    # 트렌드: 앞 절반 평균 vs 최근 절반 평균 rank 비교
    if len(ranks) < 3:
        trend = "insufficient_data"
    else:
        mid        = len(ranks) // 2
        # history 는 최신 순 → 뒤쪽이 과거, 앞쪽이 최신
        recent_avg = sum(ranks[:mid]) / mid
        early_avg  = sum(ranks[mid:]) / (len(ranks) - mid)
        diff = recent_avg - early_avg
        if diff > 0.3:
            trend = "improving"
        elif diff < -0.3:
            trend = "declining"
        else:
            trend = "stable"

    return {
        "status":            "success",
        "sample_count":      len(history),
        "history":           history,
        "rank_distribution": dist,
        "current_rank":      ranks[0],
        "current_rank_label": _RANK_LABELS.get(ranks[0], "알 수 없음"),
        "trend":             trend,
        "consecutive_same":  consecutive_same,
    }


# ──────────────────────────────────────────────────────────
# 엔드포인트 3: 유저 루프 사용 균형 분석
# ──────────────────────────────────────────────────────────
# imbalance_score 가 이 값 이상이면 한쪽 루프에 과하게 치우친 것으로 간주하여
# 기계를 고장내는 신호(should_break_machine=True) 를 보냅니다.
# 0.6 = 8:2 편향 (for:while 또는 while:for 가 8:2 이상)
#       |0.8 - 0.5| × 2 = 0.6  (또는 for_ratio=0.2 일 때 동일)
_IMBALANCE_BREAK_THRESHOLD: float = 0.6

# imbalance_score 가 이 값 이하면 다시 균형을 회복한 것으로 간주하여
# 클라이언트가 임밸런스 고장을 해제하도록 신호(is_balance_fixed=True) 를 보냅니다.
# 0.3 = 6.5:3.5 비율 (|0.65 - 0.5| × 2 = 0.3)
_IMBALANCE_FIX_THRESHOLD: float = 0.3


def _compute_loop_balance(cursor, user_pk: int, sample_size: int = 20) -> dict:
    """
    유저의 최근 성공 제출(최대 sample_size 개)에서
    for / while 누적 사용량을 분석한 균형 지표 dict 를 반환합니다.

    /api/user_loop_balance/{user_id} 와 /api/submit_code 모두에서 공통 사용.

    반환 키:
        status               : "success" | "no_data" | "no_loops"
        sample_count, total_for_count, total_while_count
        for_ratio, while_ratio (0.0~1.0)
        imbalance_score      (0.0~1.0)  — 0 에 가까울수록 균형
        should_break_machine (bool)     — imbalance_score >= 0.6
        is_balance_fixed     (bool)     — imbalance_score <= 0.3 (6.5:3.5 이하)
        consumed_part_type   ("for"|"while") — 더 많이 사용된 부품 종류
    """
    _no_data_base = {
        "sample_count":         0,
        "total_for_count":      0,
        "total_while_count":    0,
        "for_ratio":            0.0,
        "while_ratio":          0.0,
        "imbalance_score":      0.0,
        "should_break_machine": False,
        "is_balance_fixed":     True,
        "consumed_part_type":   "for",
    }

    try:
        cursor.execute(
            """
            SELECT source_code FROM code_logs
            WHERE  user_pk = %s AND is_success = 1
            ORDER  BY created_at DESC
            LIMIT  %s
            """,
            (user_pk, sample_size)
        )
        rows = cursor.fetchall() or []
    except Exception:
        return {**_no_data_base, "status": "no_data",
                "message": "성공한 제출 기록 조회에 실패했습니다."}

    if not rows:
        return {**_no_data_base, "status": "no_data",
                "message": "성공한 제출 기록이 없습니다."}

    total_for = total_while = 0
    for row in rows:
        f = extract_features(row['source_code'])
        total_for   += f['for_count']
        total_while += f['while_count']

    total_loops = total_for + total_while
    if total_loops == 0:
        return {
            **_no_data_base,
            "status":       "no_loops",
            "message":      "아직 반복문을 사용한 기록이 없습니다.",
            "sample_count": len(rows),
        }

    for_ratio   = total_for   / total_loops
    while_ratio = total_while / total_loops

    # imbalance_score: |for_ratio - 0.5| × 2
    # 0.0 = 완전 균형(for 50% : while 50%) / 1.0 = 한쪽만 사용
    imbalance_score = round(abs(for_ratio - 0.5) * 2, 3)

    # 8:2 이상 치우치면 고장 트리거, 6.5:3.5 이하로 회복하면 해제 신호
    should_break_machine = imbalance_score >= _IMBALANCE_BREAK_THRESHOLD
    is_balance_fixed     = imbalance_score <= _IMBALANCE_FIX_THRESHOLD

    # consumed_part_type: 더 많이 사용된 루프 유형 — 고장 시 소모될 부품 종류
    consumed_part_type = "for" if for_ratio > 0.5 else "while"

    return {
        "status":               "success",
        "sample_count":         len(rows),
        "total_for_count":      total_for,
        "total_while_count":    total_while,
        "for_ratio":            round(for_ratio, 3),
        "while_ratio":          round(while_ratio, 3),
        "imbalance_score":      imbalance_score,
        "should_break_machine": should_break_machine,
        "is_balance_fixed":     is_balance_fixed,
        "consumed_part_type":   consumed_part_type,
    }


@app.get("/api/user_loop_balance/{user_id}")
async def get_user_loop_balance(user_id: str, sample_size: int = 20):
    """
    유저의 최근 성공 제출(최대 sample_size개)에서
    for / while 사용 비율을 분석하여 균형 지표를 반환합니다.

    Unity 클라이언트 활용 가이드:
        should_break_machine (bool)            : True 면 기계 고장 이벤트 발생
                                                 (imbalance_score >= _IMBALANCE_BREAK_THRESHOLD)
        is_balance_fixed     (bool)            : True 면 임밸런스 고장 해제 트리거
                                                 (imbalance_score <= _IMBALANCE_FIX_THRESHOLD)
        consumed_part_type   (str, "for"|"while") : 고장 시 소모될 부품 종류
                                                    (= 더 많이 사용된 루프 유형)
        imbalance_score      (float, 0.0~1.0)  : 0 에 가까울수록 균형 잡힌 코딩 스타일
    """
    conn   = pymysql.connect(**DB_CONFIG)
    cursor = conn.cursor()
    try:
        cursor.execute("SELECT pk_id FROM users WHERE id = %s", (user_id,))
        user_record = cursor.fetchone()
        if not user_record:
            raise HTTPException(status_code=404, detail=f"'{user_id}' 유저를 찾을 수 없습니다.")
        return _compute_loop_balance(cursor, user_record['pk_id'], sample_size)
    finally:
        conn.close()


# ──────────────────────────────────────────────────────────
# 디버깅 엔드포인트 — 모델 상태 확인 / 강제 재로드
# ──────────────────────────────────────────────────────────

@app.get("/api/model_status")
async def get_model_status():
    """
    현재 ML 모델의 상태를 반환합니다.
    AWS 서버에서 브라우저나 curl 로 직접 확인할 수 있습니다.

    확인 항목:
        model_loaded        — 모델이 메모리에 로드되어 있는지
        file.last_modified  — pkl 파일이 마지막으로 갱신된 시각
        file.in_sync        — 메모리의 모델과 파일이 동일한지 (False 면 다음 요청 시 리로드 예정)
        last_training       — 마지막 학습 시각·데이터 수·군집별 통계
        scaler.feature_count— 현재 모델이 기대하는 피처 수 (utils.py 와 일치해야 함)
    """
    now_str  = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    file_exists = os.path.exists(MODEL_PATH)

    # ── 파일 정보 ──────────────────────────────────────────
    file_info: dict = {"path": MODEL_PATH, "exists": file_exists}
    if file_exists:
        mtime = os.path.getmtime(MODEL_PATH)
        file_info["size_kb"]       = round(os.path.getsize(MODEL_PATH) / 1024, 1)
        file_info["last_modified"] = datetime.datetime.fromtimestamp(mtime).strftime("%Y-%m-%d %H:%M:%S")
        file_info["in_sync"]       = (mtime == _model_loaded_mtime)

    # ── 모델 정보 ──────────────────────────────────────────
    model_info: dict = {
        "model_loaded":  code_cluster_model is not None,
        "scaler_loaded": scaler is not None,
    }
    if code_cluster_model is not None:
        model_info["n_clusters"]      = int(code_cluster_model.n_clusters)
        model_info["cluster_rank_map"] = cluster_rank_map
    if scaler is not None:
        model_info["scaler"] = {
            "feature_count": int(scaler.n_features_in_),
            # feature_names_in_ 는 sklearn 1.0+ + DataFrame fit 시 제공
            "feature_names": list(scaler.feature_names_in_)
                              if hasattr(scaler, 'feature_names_in_') else None,
        }

    # ── 마지막 학습 메타데이터 ───────────────────────────────
    last_training: dict = {}
    if _model_meta:
        last_training["trained_at"] = _model_meta.get("trained_at", "알 수 없음")
        last_training["data_count"] = _model_meta.get("data_count", "알 수 없음")
        raw_summary = _model_meta.get("cluster_summary", {})
        last_training["cluster_summary"] = {
            f"rank_{rank}": info for rank, info in raw_summary.items()
        }
    else:
        last_training["note"] = "메타데이터 없음 (구 포맷 pkl 또는 아직 학습 전)"

    return {
        "checked_at":   now_str,
        "file":         file_info,
        "model":        model_info,
        "last_training": last_training,
    }


@app.get("/api/hint_stats")
async def get_hint_stats():
    """
    힌트 효과성 밴딧의 누적 통계를 반환합니다.

    반환 필드 (hint_type 별):
        shown_count     — 해당 힌트가 사용자에게 표시된 횟수
        success_count   — 레거시 이진 평가 누적값 (참고용)
        success_rate    — success_count / shown_count (노출 없으면 null)
        alpha, beta     — Thompson Sampling Beta 분포 파라미터
        expected_reward — α / (α+β), 현재 힌트 변형의 예측 보상 기댓값

    상위 정보:
        policy_loaded   — Contextual Policy(RandomForest) 모델 로드 여부
                          False 면 Thompson Sampling 단독으로 선택 중

    활용:
        성공률이 낮은 변형은 Beta 분포의 평균이 낮아져 선택 빈도가 자연 감소.
        표본이 적은 변형은 분산이 커서 탐색 빈도가 자연 상승 (cold-start 강건).
    """
    result = {}
    # in-memory _hint_stats 와 Thompson 파라미터를 합집합으로 노출
    all_keys = set(_hint_stats.keys()) | set(_thompson_bandit.params.keys())
    for hint_type in sorted(all_keys):
        stats   = _hint_stats.get(hint_type, {"shown": 0, "success": 0})
        shown   = stats.get("shown",   0)
        success = stats.get("success", 0)
        alpha, beta = _thompson_bandit.get(hint_type)
        result[hint_type] = {
            "shown_count":      shown,
            "success_count":    success,
            "success_rate":     round(success / shown, 3) if shown > 0 else None,
            # Thompson Sampling 파라미터
            "alpha":            round(alpha, 3),
            "beta":             round(beta,  3),
            "expected_reward":  round(_thompson_bandit.expected_value(hint_type), 3),
        }
    return {
        "status":     "success",
        "hint_count": len(result),
        "policy_loaded": _contextual_bandit.is_ready(),
        "stats":      result,
    }


@app.get("/api/score_breakdown/{log_id}")
async def get_score_breakdown(log_id: int):
    """
    특정 제출(log_id)의 다차원 점수 분해를 반환합니다.

    응답 구조:
        {
          weights:       {base, personal, adoption, antipattern},
          subscores:     {base, personal_delta, adoption, antipattern_pen},
          contributions: {base, personal, adoption, antipattern},
          raw, final, antipattern_tags, error_type, hint_type, reward,
          cluster_rank, is_success, created_at
        }

    사전 조건:
        server/migrations/scoring_v2.sql 적용 (base_score, personal_score 등 컬럼).
        컬럼이 없을 경우 503 응답으로 마이그레이션 안내.
    """
    conn   = pymysql.connect(**DB_CONFIG)
    cursor = conn.cursor()
    try:
        try:
            cursor.execute(
                """
                SELECT log_id, user_pk, score, base_score, personal_score,
                       adoption_score, antipattern_pen, antipattern_tags,
                       reward, error_type, hint_type, cluster_rank,
                       is_success, created_at
                FROM   code_logs
                WHERE  log_id = %s
                """,
                (log_id,)
            )
            row = cursor.fetchone()
        except Exception as e:
            msg = str(e).lower()
            if "unknown column" in msg or "base_score" in msg:
                raise HTTPException(
                    status_code=503,
                    detail="Scoring 2.0 컬럼 없음. "
                           "server/migrations/scoring_v2.sql 을 적용하세요."
                )
            raise

        if not row:
            raise HTTPException(status_code=404, detail=f"log_id={log_id} 를 찾을 수 없습니다.")

        # 누락 값 폴백 (마이그레이션 후 누적 전 데이터)
        base   = float(row.get("base_score")      or 0.0)
        person = float(row.get("personal_score")  or 50.0)
        adopt  = float(row.get("adoption_score")  or 50.0)
        anti   = float(row.get("antipattern_pen") or 0.0)

        breakdown = score_breakdown(base, person, adopt, anti)
        breakdown.update({
            "log_id":          row["log_id"],
            "user_pk":         row.get("user_pk"),
            "score_in_db":     float(row["score"]) if row.get("score") is not None else None,
            "antipattern_tags": (row.get("antipattern_tags") or "").split(",") if row.get("antipattern_tags") else [],
            "error_type":      row.get("error_type"),
            "hint_type":       row.get("hint_type"),
            "reward":          float(row["reward"]) if row.get("reward") is not None else None,
            "cluster_rank":    row.get("cluster_rank"),
            "is_success":      bool(row.get("is_success", 0)),
            "created_at":      str(row.get("created_at")),
        })
        return breakdown
    finally:
        conn.close()


@app.post("/api/model_reload")
async def force_model_reload():
    """
    pkl 파일을 강제로 재로드합니다.
    ml_worker 가 방금 학습을 완료했는데 아직 핫리로드가 트리거되지 않은 경우 사용합니다.

    사용 예:
        curl -X POST http://<AWS_IP>:8001/api/model_reload
    """
    before_trained = _model_meta.get('trained_at', '없음')
    before_loaded  = datetime.datetime.fromtimestamp(_model_loaded_mtime).strftime("%Y-%m-%d %H:%M:%S") \
                     if _model_loaded_mtime else '없음'
    _load_model()
    after_trained  = _model_meta.get('trained_at', '없음')

    # Contextual policy 도 함께 핫리로드 시도
    policy_before = _contextual_bandit.is_ready()
    _contextual_bandit.maybe_reload()
    policy_after  = _contextual_bandit.is_ready()

    return {
        "status":         "reloaded",
        "model_loaded":   code_cluster_model is not None,
        "policy_loaded":  policy_after,
        "before": {"trained_at": before_trained, "loaded_at": before_loaded,
                   "policy_loaded": policy_before},
        "after":  {"trained_at": after_trained,
                   "loaded_at": datetime.datetime.fromtimestamp(_model_loaded_mtime)
                                .strftime("%Y-%m-%d %H:%M:%S") if _model_loaded_mtime else '없음',
                   "policy_loaded": policy_after},
    }


# ──────────────────────────────────────────────────────────
# 서버 실행
# 로컬 테스트와 AWS 배포 중 사용할 블록의 주석을 해제하세요.
# ──────────────────────────────────────────────────────────

# # 로컬 테스트용
# if __name__ == "__main__":
#     import uvicorn
#     uvicorn.run("main:app", host="127.0.0.1", port=8001, reload=True)

# AWS 배포용
if __name__ == "__main__":
    import uvicorn
    uvicorn.run("main:app", host="0.0.0.0", port=8001, reload=True)
