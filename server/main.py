"""
server/main.py
ML 서버 (Server B) — 코드 로그 저장 / AI 힌트 생성 / 루프 균형 분석 / Scoring 2.0

사용자 표시 문구(AI 힌트·오류 안내·API message)는 user_messages.py 에만 두고,
이 파일은 로직만 담당합니다. 문구 수정 시 user_messages.py 를 편집하세요.

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
    ml/context_encoder      — 유저 상태 14차원 벡터 (히스토리 8 + 현재 제출 6)
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
from user_messages import (
    HINT_VARIANTS,
    HINT_VARIANTS_MAP,
    RANK_LABELS,
    STAGNATION_NUDGE,
    STAGNATION_NUDGE_DEFAULT,
    BANDIT_FALLBACK_OK,
    SUCCESS_UNKNOWN_CLUSTER,
    SUCCESS_MOVE_STANDALONE,
    RANK_LABEL_UNKNOWN,
    PROGRESSION_GROWTH,
    PROGRESSION_DECLINE_FROM_RANK2,
    PROGRESSION_DECLINE_GENERIC,
    PROGRESSION_STAGNATION,
    Move,
    Err,
    Machine,
    Api,
    msg,
)

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
from ml.hint_routing      import (
    filter_variants,
    resolve_success_hint_group,
    effective_hint_rank,
)

app = FastAPI()

_BASE_DIR    = os.path.dirname(os.path.abspath(__file__))
MODEL_PATH   = os.path.join(_BASE_DIR, 'code_cluster_model.pkl')
POLICY_PATH  = os.path.join(_BASE_DIR, 'code_policy_model.pkl')


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

# 밴딧 힌트 변형 풀·문구 → user_messages.py (HINT_VARIANTS, HINT_VARIANTS_MAP)

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


def _bandit_select(
    group_key: str,
    context: list[float] | None = None,
    features: dict | None = None,
) -> tuple[str, str]:
    """
    Contextual Bandit + Thompson Sampling 으로 힌트 변형을 선택합니다.

    features 가 주어지면 hint_routing.filter_variants 로
    현재 코드에 맞지 않는 변형(이미 달성한 upsell 등)을 제외합니다.
    """
    variants = filter_variants(group_key, features or {})
    if not variants and group_key != "succ_r2_ceiling":
        variants = filter_variants("succ_r2_ceiling", features or {})
    if not variants:
        return BANDIT_FALLBACK_OK, f"succ_unknown_{group_key}"
    if len(variants) == 1:
        return variants[0]

    candidates = [hint_type for (_, hint_type) in variants]
    ctx        = context if context is not None else []

    selected = _contextual_bandit.select(ctx, candidates)
    if not selected:
        return random.choice(variants)

    text = HINT_VARIANTS_MAP.get(selected)
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
        feat_with_meta = {**features, 'execution_time': execution_time}
        if _feature_names:
            user_df = pd.DataFrame(
                [{k: feat_with_meta.get(k, 0) for k in _feature_names}]
            )
        else:
            user_df = pd.DataFrame([feat_with_meta])
        scaled = scaler.transform(user_df)

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
        loop_bonus    +30점 : 반복문 사용 +20, range 효율 최대 +10
        infinite_bonus +10점: 무한 루프(for count / while True) 기본 +6, break +2, if in loop +2
        time_penalty  -25점 : 실행 시간 × 5 (최대 25, 5초 이상 동일 패널티)
        density_bonus  +5점 : 줄당 함수 호출 수 비례 (빽빽하게 쓴 코드 보상)

    [설계 의도]
        - 루프를 안 쓰면 최대 55점 (base + density만)
        - for/while 반복문 사용 시 최대 85점 (loop_efficiency 비례)
        - 무한 루프만으로 만점이 아님 — break·if in loop 로 추가 보너스
    """
    base = 50.0

    loop_bonus = 0.0
    if features['has_loop']:
        loop_bonus += 20.0
        loop_bonus += min(10.0, features['loop_efficiency'] * 5.0)

    infinite_bonus = 0.0
    if features.get('has_infinite_for') or features.get('has_infinite_while'):
        infinite_bonus += 6.0
        if features.get('has_break_in_loop'):
            infinite_bonus += 2.0
        if features.get('has_if_inside_loop'):
            infinite_bonus += 2.0
    infinite_bonus = min(10.0, infinite_bonus)

    # 5초 이상은 동일 페널티로 묶어 지나친 감점 방지
    time_penalty = min(25.0, request.execution_time * 5.0)

    # 줄당 함수 호출 수: 같은 작업을 적은 줄로 표현한 코드를 보상
    density       = features['func_call_count'] / max(1, features['line_count'])
    density_bonus = min(5.0, density * 10.0)

    raw = base + loop_bonus + infinite_bonus - time_penalty + density_bonus
    return round(max(0.0, min(100.0, raw)), 2)


# ──────────────────────────────────────────────────────────
# 군집 이동 이력 기반 성장/정체 문구 생성
# ──────────────────────────────────────────────────────────

# 군집 레이블·정체 유도 문구 → user_messages.py (RANK_LABELS, STAGNATION_NUDGE)


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

    cur_label  = RANK_LABELS.get(current_rank, str(current_rank))
    prev_label = RANK_LABELS.get(last_rank,    str(last_rank))

    # ── 성장 감지 (rank 상승) ─────────────────────────────────
    if current_rank > last_rank:
        return msg(PROGRESSION_GROWTH, prev_label=prev_label, cur_label=cur_label)

    # ── 하락 감지 (rank 하락) ─────────────────────────────────
    if current_rank < last_rank:
        if last_rank == 2:
            return msg(PROGRESSION_DECLINE_FROM_RANK2, prev_label=prev_label)
        return msg(PROGRESSION_DECLINE_GENERIC)

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
        nudge = STAGNATION_NUDGE.get(current_rank, STAGNATION_NUDGE_DEFAULT)
        return msg(
            PROGRESSION_STAGNATION,
            cur_label=cur_label,
            consecutive=consecutive + 1,
            nudge=nudge,
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
        return msg(Move.UNCLOSED_PAREN)

    m = _MOVE_TYPO_VARIANT_RE.search(source_code)
    if m:
        return msg(Move.TYPO, token=m.group('token'))

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

# 기계 타입별 필수 함수 목록 (공백 제거 후 부분 문자열 매칭)
REQUIRED_FUNCTIONS: dict[str, list[str]] = {
    "Miner_Common":       ["mining(resCommon)"],
    "Miner_Advanced":     ["mining(resRare)"],
    "Miner_Hightech":     ["mining(resSpecial)"],
    "Miner_Superior":     ["mining(resExotic)"],
    "Productor_Common":   ["producting(Common,"],
    "Productor_Advanced": ["producting(Rare,"],
    "Productor_Hightech": ["producting(Special,"],
    "Productor_Superior": ["producting(Exotic,"],
}

# 2차 게임 문법 — 기계 등급별 허용 mining / producting 인자
_MACHINE_CALL_RULES: dict[str, dict[str, str]] = {
    "Miner_Common":       {"kind": "mining", "arg": "resCommon"},
    "Miner_Advanced":     {"kind": "mining", "arg": "resRare"},
    "Miner_Hightech":     {"kind": "mining", "arg": "resSpecial"},
    "Miner_Superior":     {"kind": "mining", "arg": "resExotic"},
    "Productor_Common":   {"kind": "producting", "tier": "Common"},
    "Productor_Advanced": {"kind": "producting", "tier": "Rare"},
    "Productor_Hightech": {"kind": "producting", "tier": "Special"},
    "Productor_Superior": {"kind": "producting", "tier": "Exotic"},
}

_MACHINE_CALL_HINT: dict[str, str] = {
    "Miner_Common":       "mining(resCommon)",
    "Miner_Advanced":     "mining(resRare)",
    "Miner_Hightech":     "mining(resSpecial)",
    "Miner_Superior":     "mining(resExotic)",
    "Productor_Common":   "producting(Common, 'A' 또는 'B')",
    "Productor_Advanced": "producting(Rare, 'A' 또는 'B')",
    "Productor_Hightech": "producting(Special, 'A' 또는 'B')",
    "Productor_Superior": "producting(Exotic, 'A' 또는 'B')",
}

_MINING_CALL_RE = re.compile(r"mining\s*\(\s*([^)]*)\s*\)", re.IGNORECASE)
_PRODUCTING_CALL_RE = re.compile(
    r"producting\s*\(\s*([^,)]+)\s*,\s*['\"]?([ab])['\"]?\s*\)",
    re.IGNORECASE,
)


def _strip_comments_for_game_check(source_code: str) -> str:
    """클라이언트 GameCodeValidator 와 동일한 단순 주석/문자열 제거."""
    if not source_code:
        return ""
    out: list[str] = []
    for line in source_code.split("\n"):
        buf: list[str] = []
        in_single = in_double = False
        for ch in line:
            if not in_single and not in_double and ch == "#":
                break
            if not in_double and ch == "'":
                in_single = not in_single
                buf.append(" ")
                continue
            if not in_single and ch == '"':
                in_double = not in_double
                buf.append(" ")
                continue
            buf.append(" " if (in_single or in_double) else ch)
        out.append("".join(buf))
    return "\n".join(out)


def _detect_wrong_machine_call(source_code: str, machine_type: str) -> str | None:
    """
    등급에 맞지 않는 mining / producting 인자가 있으면 안내용 expected 문자열 반환.
    위반 없으면 None.
    """
    rule = _MACHINE_CALL_RULES.get(machine_type)
    if not rule:
        return None

    src = _strip_comments_for_game_check(source_code)
    expected_hint = _MACHINE_CALL_HINT.get(machine_type, "")

    if rule["kind"] == "mining":
        expected_arg = re.sub(r"\s+", "", rule["arg"]).lower()
        calls = _MINING_CALL_RE.findall(src)
        if not calls:
            return None
        for raw_arg in calls:
            if re.sub(r"\s+", "", raw_arg).lower() != expected_arg:
                return expected_hint or f"mining({rule['arg']})"
        return None

    expected_tier = rule["tier"].lower()
    calls = _PRODUCTING_CALL_RE.findall(src)
    if not calls:
        return None
    for raw_tier, _ in calls:
        if re.sub(r"\s+", "", raw_tier).lower() != expected_tier:
            return expected_hint or f"producting({rule['tier']}, 'A' 또는 'B')"
    return None


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
        return msg(Move.IN_LOOP)

    # ══════════════════════════════════════════════════════
    # 1단계: 파이썬 문법 / 런타임 에러
    # ══════════════════════════════════════════════════════
    if not request.is_python_valid:
        log       = request.output_log          # 원본 (대소문자 유지, regex 추출용)
        error_log = log.lower()                  # 소문자 검색용
        source    = request.source_code

        # ── Sandbox 보안 차단 ───────────────────────────
        # server/8000.py 의 SecurityVisitor 가 "보안: 외부 모듈 사용 금지 (X.Y)"
        # 형식으로 던지는 메시지를 캐치. 외부 import 는 모두 차단됨 (count 는 샌드박스 기본 제공).
        if "보안: 외부 모듈 사용 금지" in log or "외부 모듈 사용 금지" in log:
            m = re.search(r"외부 모듈 사용 금지\s*\(([^)]+)\)", log)
            target = m.group(1) if m else None
            if target:
                return msg(Err.SANDBOX_IMPORT_MODULE, target=target)
            return msg(Err.SANDBOX_IMPORT_GENERIC)
        if "금지 함수 사용" in log:
            m = re.search(r"금지 함수 사용:\s*(\S+)", log)
            target = m.group(1) if m else Err.SANDBOX_FORBIDDEN_FN_FALLBACK_TARGET
            return msg(Err.SANDBOX_FORBIDDEN_FN, target=target)

        # ── SyntaxError 계열 ─────────────────────────────
        # (IndentationError / TabError 도 8000.py 에서 SyntaxError: 로 포맷됨)
        if "syntaxerror" in error_log:

            # 들여쓰기 없는 블록 본문 (IndentationError: expected an indented block)
            if "expected an indented block" in error_log:
                problem_line = _find_empty_block(source)
                if problem_line:
                    return msg(Err.SYNTAX_INDENT_BLOCK_LINE, problem_line=problem_line)
                return msg(Err.SYNTAX_INDENT_BLOCK)

            if "unexpected indent" in error_log:
                return msg(Err.SYNTAX_UNEXPECTED_INDENT)

            if "inconsistent use of tabs" in error_log or "taberror" in error_log:
                return msg(Err.SYNTAX_TAB_MIX)

            if ("unexpected eof" in error_log
                    or "never closed" in error_log
                    or "was never closed" in error_log):
                return msg(Err.SYNTAX_UNCLOSED_PAREN)

            if ("unterminated string" in error_log
                    or "eol while scanning" in error_log):
                return msg(Err.SYNTAX_UNCLOSED_STRING)

            if "return outside function" in error_log:
                return msg(Err.SYNTAX_RETURN_OUTSIDE)
            if "break outside loop" in error_log:
                return msg(Err.SYNTAX_BREAK_OUTSIDE)
            if "continue outside loop" in error_log:
                return msg(Err.SYNTAX_CONTINUE_OUTSIDE)

            if ("cannot assign to" in error_log
                    or "maybe you meant '=='" in error_log):
                return msg(Err.SYNTAX_ASSIGN_VS_COMPARE)

            if "invalid character" in error_log:
                return msg(Err.SYNTAX_INVALID_CHAR)

            if "f-string" in error_log:
                return msg(Err.SYNTAX_FSTRING)

            return msg(Err.SYNTAX_GENERIC)

        # ── NameError ─────────────────────────────────────
        if "nameerror" in error_log:
            undef = _extract_name_from_nameerror(log)

            if undef:
                if re.search(r'\bname\s*=\s*' + re.escape(undef), source):
                    return msg(Err.NAME_MACHINE_UNQUOTED, undef=undef)

                if _looks_like_unquoted_string(undef, source):
                    return msg(Err.NAME_STRING_UNQUOTED, undef=undef)

                suggestion, category = _suggest_similar_name(undef)
                if suggestion:
                    ctx = Err.CTX_GAME_CMD if category == "game" else Err.CTX_PYTHON_BUILTIN
                    return msg(
                        Err.NAME_TYPO_SUGGEST,
                        undef=undef, ctx=ctx, suggestion=suggestion,
                    )

            return msg(Err.NAME_GENERIC)

        # ── TypeError ─────────────────────────────────────
        if "typeerror" in error_log:
            # 문자열 + 숫자 연결 시도
            if ("can only concatenate str" in error_log
                    or "must be str, not" in error_log):
                return msg(Err.TYPE_STR_INT_CONCAT)

            if "takes" in error_log and "argument" in error_log:
                fn_name = _extract_fn_from_typeerror(log)
                if fn_name:
                    return msg(Err.TYPE_WRONG_ARGS_FN, fn_name=fn_name)
                return msg(Err.TYPE_WRONG_ARGS_GENERIC)

            if "'nonetype'" in error_log:
                return msg(Err.TYPE_NONE)

            if "not subscriptable" in error_log:
                return msg(Err.TYPE_NOT_SUBSCRIPTABLE)

            if "cannot be interpreted as an integer" in error_log:
                return msg(Err.TYPE_RANGE_NOT_INT)

            return msg(Err.TYPE_GENERIC)

        # ── AttributeError ───────────────────────────────
        if "attributeerror" in error_log:
            attr = _extract_attr_from_attributeerror(log)
            if attr:
                suggestion, category = _suggest_similar_name(attr)
                if suggestion:
                    ctx = Err.CTX_GAME_CMD if category == "game" else Err.CTX_PYTHON_BUILTIN
                    return msg(
                        Err.ATTR_TYPO_SUGGEST,
                        attr=attr, ctx=ctx, suggestion=suggestion,
                    )
                return msg(Err.ATTR_UNKNOWN, attr=attr)
            return msg(Err.ATTR_GENERIC)

        # ── ValueError ────────────────────────────────────
        if "valueerror" in error_log:
            if "invalid literal" in error_log and "int()" in error_log:
                return msg(Err.VALUE_INT_LITERAL)
            return msg(Err.VALUE_GENERIC)

        if "zerodivisionerror" in error_log:
            return msg(Err.ZERO_DIVISION)

        if "indexerror" in error_log:
            return msg(Err.INDEX)

        if "keyerror" in error_log:
            m = re.search(r"KeyError: (.+)", log)
            key_name = m.group(1).strip() if m else ""
            if key_name:
                return msg(Err.KEY_WITH_NAME, key_name=key_name)
            return msg(Err.KEY_GENERIC)

        if "recursionerror" in error_log:
            return msg(Err.RECURSION)

        if "timeouterror" in error_log:
            return msg(Err.TIMEOUT)

        return msg(Err.UNKNOWN)

    # ══════════════════════════════════════════════════════
    # 2단계: 기계별 조건 미충족 (Unity client/-1..-9 매핑과 결을 맞춤)
    # ══════════════════════════════════════════════════════
    if not request.is_machine_valid:
        clean = request.source_code.replace(" ", "")

        # 게임 기믹: 무한 루프 미해금 상태에서 while True / for in count() 시도
        # (클라이언트가 -3 으로 차단하지만, 서버 hint 도 같은 사유로 응답)
        if features.get('has_infinite_loop', 0) or features.get('has_infinite_while', 0):
            return msg(Machine.LOCKED_INFINITE)

        if features.get('has_loop', 0):
            return msg(Machine.LOCKED_LOOP)

        if "name=" not in clean:
            return msg(Machine.NO_NAME)

        wrong_call = _detect_wrong_machine_call(request.source_code, request.machine_type)
        if wrong_call:
            return msg(Machine.WRONG_ARGS, expected=wrong_call)

        for fn in REQUIRED_FUNCTIONS.get(request.machine_type, []):
            if fn.replace(" ", "") not in clean:
                return msg(Machine.MISSING_FN, fn=fn)

        return msg(Machine.GENERIC)

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
        if _detect_wrong_machine_call(request.source_code, request.machine_type):
            return "machine_wrong_args"
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
    antipattern_tags: list[str] | None = None,
) -> tuple[str, str]:
    """
    힌트 텍스트와 hint_type ID를 함께 반환합니다.

    성공 힌트: effective_hint_rank + resolve_success_hint_group 으로 풀 결정,
               filter_variants 로 현재 코드에 맞지 않는 upsell 변형 제외 후 밴딧 선택.
    """
    # ══════════════════════════════════════════════════════
    # Layer 0: move() 전용 — 성공/실패 모든 경로에서 최우선 평가
    # (오타 / 미완성 / 루프 내 사용은 일반 힌트 / 밴딧을 우회)
    # ══════════════════════════════════════════════════════
    move_typo_msg = _detect_move_typo(request.source_code)
    if move_typo_msg:
        return move_typo_msg, "move_typo"

    if _move_in_loop(request.source_code):
        return msg(Move.IN_LOOP), "move_in_loop"

    if request.is_python_valid and request.is_machine_valid:
        # ── Layer 0-success: move() 단독 호출 — 컨테이너 타일 설치 만점 ────
        # 반복문 없이 move() 만 호출된 경우 클러스터/밴딧을 거치지 않고 고정 메시지를 반환합니다.
        if _is_move_standalone(request.source_code, features):
            return msg(SUCCESS_MOVE_STANDALONE), "succ_move"

        hint_rank = effective_hint_rank(cluster_rank, features)
        if hint_rank >= 0:
            group = resolve_success_hint_group(
                hint_rank, features, antipattern_tags,
            )
            return _bandit_select(group, context, features)
        return msg(SUCCESS_UNKNOWN_CLUSTER), "succ_unknown"

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
        if cluster_rank == 1 and features.get('has_infinite_loop', 0):
            cluster_rank = 2

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
            raise HTTPException(
                status_code=404, detail=msg(Api.USER_NOT_FOUND, user_id=search_id),
            )
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
            user_context = encode_user_context(cursor, user_pk, features)
        except Exception:
            user_context = None
        ai_hint, hint_type = _generate_hint_typed(
            request, score, features, cluster_rank,
            context=user_context,
            antipattern_tags=antipattern_tags,
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
        # installed_machines(채굴기 4 + 가공기 4) 에 저장된 코드 기준.
        # 디버깅 연타만으로 균형이 풀리지 않도록 code_logs 가 아닌 세이브 DB 를 사용합니다.
        try:
            balance = _compute_loop_balance(cursor, user_pk)
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
            raise HTTPException(
                status_code=404, detail=msg(Api.USER_NOT_FOUND, user_id=user_id),
            )

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
                detail=msg(Api.CLUSTER_RANK_COLUMN_MISSING),
            )
        raise
    finally:
        conn.close()

    if not rows:
        return {
            "status":            "no_data",
            "message":           msg(Api.CLUSTER_HISTORY_NO_DATA),
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
            "rank_label":   RANK_LABELS.get(row['cluster_rank'], RANK_LABEL_UNKNOWN),
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
        "current_rank_label": RANK_LABELS.get(ranks[0], RANK_LABEL_UNKNOWN),
        "trend":             trend,
        "consecutive_same":  consecutive_same,
    }


# ──────────────────────────────────────────────────────────
# 엔드포인트 3: 유저 루프 사용 균형 분석
# ──────────────────────────────────────────────────────────
# imbalance_score 가 이 값 이상이면 한쪽 루프에 과하게 치우친 것으로 간주하여
# 기계를 고장내는 신호(should_break_machine=True) 를 보냅니다.
# 0.5 = 75:25 편향 (한쪽 루프 비율 75% 이상) — |0.75 - 0.5| × 2 = 0.5
_IMBALANCE_BREAK_THRESHOLD: float = 0.5

# imbalance_score 가 이 값 이하면 다시 균형을 회복한 것으로 간주하여
# 클라이언트가 임밸런스 고장을 해제하도록 신호(is_balance_fixed=True) 를 보냅니다.
# 0.3 = 65:35 이하 (한쪽 루프 비율 65% 이하) — |0.65 - 0.5| × 2 = 0.3
_IMBALANCE_FIX_THRESHOLD: float = 0.3

# Unity Ingame_System_Save.GetMachineTypeInt 와 동일 (채굴기 1~4, 가공기 5~8)
_LOOP_BALANCE_MACHINE_TYPES: tuple[int, ...] = (1, 2, 3, 4, 5, 6, 7, 8)


def _fetch_installed_machine_codes(cursor, user_pk: int) -> list[dict]:
    """유저 세이브 DB 의 채굴기·가공기(최대 8종) source_code 를 조회합니다."""
    placeholders = ",".join(["%s"] * len(_LOOP_BALANCE_MACHINE_TYPES))
    cursor.execute(
        f"""
        SELECT machine_type, source_code
        FROM   installed_machines
        WHERE  user_pk = %s
          AND  machine_type IN ({placeholders})
        """,
        (user_pk, *_LOOP_BALANCE_MACHINE_TYPES),
    )
    return cursor.fetchall() or []


def _compute_loop_balance(cursor, user_pk: int) -> dict:
    """
    유저의 installed_machines(채굴기 4 + 가공기 4) 에 저장된 코드에서
    for / while 누적 사용량을 분석한 균형 지표 dict 를 반환합니다.

    code_logs(디버깅 성공 이력)가 아닌 세이브 슬롯 코드를 쓰므로,
    여러 기계의 코드를 직접 수정·저장해야만 편향도가 회복됩니다.

    /api/user_loop_balance/{user_id} 와 /api/submit_code 모두에서 공통 사용.

    반환 키:
        status               : "success" | "no_data" | "no_loops"
        sample_count, total_for_count, total_while_count
        for_ratio, while_ratio (0.0~1.0)
        imbalance_score      (0.0~1.0)  — 0 에 가까울수록 균형
        should_break_machine (bool)     — imbalance_score >= 0.5 (75% 편향 이상)
        is_balance_fixed     (bool)     — imbalance_score <= 0.3 (65% 편향 이하)
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
        rows = _fetch_installed_machine_codes(cursor, user_pk)
    except Exception:
        return {**_no_data_base, "status": "no_data",
                "message": msg(Api.LOOP_BALANCE_QUERY_FAIL)}

    if not rows:
        return {**_no_data_base, "status": "no_data",
                "message": msg(Api.LOOP_BALANCE_NO_MACHINES)}

    total_for = total_while = 0
    machines_with_code = 0
    for row in rows:
        source = (row.get('source_code') or '').strip()
        if not source:
            continue
        machines_with_code += 1
        f = extract_features(source)
        total_for   += f['for_count']
        total_while += f['while_count']

    if machines_with_code == 0:
        return {**_no_data_base, "status": "no_data",
                "message": msg(Api.LOOP_BALANCE_NO_MACHINES)}

    total_loops = total_for + total_while
    if total_loops == 0:
        return {
            **_no_data_base,
            "status":       "no_loops",
            "message":      msg(Api.LOOP_BALANCE_NO_LOOPS),
            "sample_count": machines_with_code,
        }

    for_ratio   = total_for   / total_loops
    while_ratio = total_while / total_loops

    # imbalance_score: |for_ratio - 0.5| × 2
    # 0.0 = 완전 균형(for 50% : while 50%) / 1.0 = 한쪽만 사용
    imbalance_score = round(abs(for_ratio - 0.5) * 2, 3)

    # 75:25 이상 치우치면 고장, 65:35 이하로 회복하면 해제 신호
    should_break_machine = imbalance_score >= _IMBALANCE_BREAK_THRESHOLD
    is_balance_fixed     = imbalance_score <= _IMBALANCE_FIX_THRESHOLD

    # consumed_part_type: 더 많이 사용된 루프 유형 — 고장 시 소모될 부품 종류
    consumed_part_type = "for" if for_ratio > 0.5 else "while"

    return {
        "status":               "success",
        "sample_count":         machines_with_code,
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
    유저의 installed_machines(채굴기·가공기 8종) 저장 코드에서
    for / while 사용 비율을 분석하여 균형 지표를 반환합니다.

    sample_size 는 하위 호환용으로만 남아 있으며 집계에 사용하지 않습니다.

    Unity 클라이언트 활용 가이드:
        should_break_machine (bool)            : True 면 기계 고장 이벤트 발생
                                                 (한쪽 루프 75% 이상, score >= 0.5)
        is_balance_fixed     (bool)            : True 면 임밸런스 고장 해제 트리거
                                                 (한쪽 루프 65% 이하, score <= 0.3)
        consumed_part_type   (str, "for"|"while") : 고장 시 소모될 부품 종류
                                                    (= 더 많이 사용된 루프 유형)
        imbalance_score      (float, 0.0~1.0)  : 0 에 가까울수록 균형 잡힌 코딩 스타일
    """
    del sample_size  # code_logs 윈도우 방식 폐기 — installed_machines 만 사용
    conn   = pymysql.connect(**DB_CONFIG)
    cursor = conn.cursor()
    try:
        cursor.execute("SELECT pk_id FROM users WHERE id = %s", (user_id,))
        user_record = cursor.fetchone()
        if not user_record:
            raise HTTPException(
                status_code=404, detail=msg(Api.USER_NOT_FOUND, user_id=user_id),
            )
        return _compute_loop_balance(cursor, user_record['pk_id'])
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
                    detail=msg(Api.SCORING_V2_COLUMN_MISSING),
                )
            raise

        if not row:
            raise HTTPException(
                status_code=404, detail=msg(Api.LOG_NOT_FOUND, log_id=log_id),
            )

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
