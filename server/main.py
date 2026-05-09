"""
server/main.py
ML 서버 (Server B) — 코드 로그 저장 / AI 힌트 생성 / 루프 균형 분석

엔드포인트:
    POST /api/submit_code                       — 코드 제출 결과 저장 및 AI 힌트 반환
    GET  /api/user_cluster_history/{user_id}    — 유저 군집 이동 이력 조회
    GET  /api/user_loop_balance/{user_id}       — 유저 루프 사용 균형 분석
    GET  /api/model_status                      — ML 모델 상태 확인 (디버깅용)
    POST /api/model_reload                      — ML 모델 강제 재로드 (디버깅용)
"""

from fastapi import FastAPI, HTTPException
from pydantic import BaseModel
import pymysql
import joblib
import os
import datetime
import re
import difflib
import random
import numpy as np
import pandas as pd

from config import DB_CONFIG
from utils import extract_features, calculate_ast_complexity

app = FastAPI()

_BASE_DIR  = os.path.dirname(os.path.abspath(__file__))
MODEL_PATH = os.path.join(_BASE_DIR, 'code_cluster_model.pkl')

# ── DB 마이그레이션 안내 ─────────────────────────────────────
# 군집 이동 이력 추적 기능을 사용하려면 아래 SQL을 한 번 실행하세요.
#
#   ALTER TABLE code_logs ADD COLUMN cluster_rank INT DEFAULT -1;
#
#   cluster_rank 값:
#     -1 = 모델 미로드 또는 예측 실패
#      0 = 단순 코드형  (루프 미사용, score 최하위)
#      1 = 성장형       (루프 일부 사용, score 중간)
#      2 = 효율 최적화형 (루프 적극 활용, score 최상위)
#
# 힌트 효과성 밴딧 기능을 사용하려면 아래 SQL도 실행하세요.
#
#   ALTER TABLE code_logs ADD COLUMN hint_type VARCHAR(64) DEFAULT NULL;
#
#   CREATE TABLE IF NOT EXISTS hint_stats (
#       hint_type     VARCHAR(64) NOT NULL PRIMARY KEY,
#       shown_count   INT         NOT NULL DEFAULT 0,
#       success_count INT         NOT NULL DEFAULT 0,
#       updated_at    DATETIME    DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
#   );
#
#   두 컬럼/테이블이 없어도 메인 기능(힌트 반환, 점수 저장)은 정상 동작합니다.
# ────────────────────────────────────────────────────────────


# ──────────────────────────────────────────────────────────
# 모델 전역 상태
# ──────────────────────────────────────────────────────────
code_cluster_model = None
scaler             = None

# cluster_rank_map: {raw cluster ID → semantic rank}
#   rank 0 = 단순 코드형  (score 평균 최하위)
#   rank 1 = 성장형       (score 평균 중간)
#   rank 2 = 효율 최적화형 (score 평균 최상위)
# kmeans_trainer.py 가 학습 후 score 기준으로 정렬해 저장하므로
# 재학습 후 cluster ID 가 뒤바뀌어도 힌트 의미가 유지됩니다.
cluster_rank_map: dict[int, int] = {}

# 마지막으로 로드한 pkl 파일의 수정 시각 (hot-reload 기준)
_model_loaded_mtime: float = 0.0

# kmeans_trainer.py 가 pkl 에 저장한 학습 메타데이터
# /api/model_status 에서 사용합니다.
_model_meta: dict = {}

# 학습 시 StandardScaler 이후에 적용한 피처 가중치 배열 (numpy 호환 list)
# predict_cluster_rank() 에서 동일하게 곱해야 학습·추론 피처 공간이 일치합니다.
_feature_weights: list = []
_feature_names:   list = []

# ── 힌트 효과성 밴딧(ε-greedy) 상태 ──────────────────────────────
# 성공 힌트(rank 0/1)에 복수 변형을 두고, 어떤 변형이 사용자 개선을
# 실제로 이끌어냈는지 자동으로 학습합니다.
# 탐색률 ε=0.2: 20%는 랜덤 탐색, 80%는 누적 성공률 최고 변형 선택.
_BANDIT_EPSILON = 0.2
_hint_stats: dict[str, dict] = {}   # hint_type → {"shown": N, "success": M}

# 밴딧이 선택할 힌트 변형 풀 — (hint_text, hint_type_id) 쌍의 리스트.
# 각 상황(group_key)에 2개의 변형을 두어 어떤 표현이 더 효과적인지 학습합니다.
_HINT_VARIANTS: dict[str, list[tuple[str, str]]] = {
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
    "succ_r1_for": [
        (
            "[ 일반 학습자형 ] "
            "for 반복문을 활용하고 있어요! "
            "range() 안의 숫자를 더 크게 늘려보거나, "
            "while True 무한 반복에도 도전해보세요.",
            "succ_r1_for_A",
        ),
        (
            "[ 일반 학습자형 ] "
            "for 루프로 좋은 구조를 만들었어요! "
            "while True: 를 사용하면 기계가 멈추지 않고 계속 일해요. 한번 바꿔보세요!",
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
            "while True: 와 break 조합으로 무한 자동화를 구현해보세요.",
            "succ_r1_while_B",
        ),
    ],
}


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

def _load_hint_stats_from_db() -> None:
    """서버 구동 시 hint_stats 테이블에서 밴딧 누적 통계를 메모리에 로드합니다."""
    global _hint_stats
    try:
        conn   = pymysql.connect(**DB_CONFIG)
        cursor = conn.cursor()
        try:
            cursor.execute("SELECT hint_type, shown_count, success_count FROM hint_stats")
            for row in cursor.fetchall():
                _hint_stats[row['hint_type']] = {
                    "shown":   row['shown_count'],
                    "success": row['success_count'],
                }
            print(f"[HintBandit] {len(_hint_stats)}개 힌트 통계 로드 완료")
        except Exception as e:
            if "hint_stats" in str(e).lower() or "doesn't exist" in str(e).lower():
                print("[HintBandit] hint_stats 테이블 없음 — 빈 통계로 시작 (마이그레이션 SQL 참고)")
            else:
                print(f"[HintBandit] 통계 로드 실패: {e}")
        finally:
            conn.close()
    except Exception as e:
        print(f"[HintBandit] DB 연결 실패: {e}")


def _bandit_select(group_key: str) -> tuple[str, str]:
    """
    ε-greedy 밴딧으로 힌트 변형을 선택합니다.

    탐색(explore, 확률 ε=0.2) : 변형 중 랜덤 선택 — 탐색 부족 변형에 기회 부여
    활용(exploit, 확률 1-ε)   : 누적 성공률 최고 변형 선택
    아직 노출 데이터 없는 변형에는 낙관적 초기값 1.0 부여 (미탐색 우선 시도)

    반환: (hint_text, hint_type_id)
    """
    variants = _HINT_VARIANTS.get(group_key, [])
    if not variants:
        return "코드가 정상 적용되었습니다.", f"succ_unknown_{group_key}"
    if len(variants) == 1:
        return variants[0]

    if random.random() < _BANDIT_EPSILON:
        return random.choice(variants)

    best_variant = variants[0]
    best_rate    = -1.0
    for variant in variants:
        _, hint_type = variant
        stats = _hint_stats.get(hint_type, {"shown": 0, "success": 0})
        shown = stats["shown"]
        rate  = 1.0 if shown == 0 else stats["success"] / shown
        if rate > best_rate:
            best_rate    = rate
            best_variant = variant

    return best_variant


_load_hint_stats_from_db()


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

    # while True / while 1 (무한루프) 사용 시에만 보너스
    # 일반 while 조건문(while i < 5 등)은 for 와 동급 취급 — 빈도 균형은 loop_balance API 담당
    while_bonus = 10.0 if features['has_infinite_while'] else 0.0

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

_RANK_LABELS_SHORT = {0: "단순 코드형", 1: "일반 학습자형", 2: "효율 최적화형"}

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

    cur_label  = _RANK_LABELS_SHORT.get(current_rank, str(current_rank))
    prev_label = _RANK_LABELS_SHORT.get(last_rank,    str(last_rank))

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

    1단계 (is_python_valid == False) : 파이썬 에러 유형별 세분화 힌트
    2단계 (is_machine_valid == False): 기계 조건 미충족 힌트
    3단계 (성공)                      : _generate_hint_typed() 의 ε-greedy 밴딧이 담당
                                        (이 함수는 1·2단계만 처리)

    주의: 8000.py 의 format_error_user() 는 IndentationError / TabError 포함
          모든 SyntaxError 계열을 "SyntaxError: {msg}" 로 포맷합니다.
          따라서 error_log 에서 "indentationerror" 문자열은 등장하지 않으며,
          들여쓰기 오류는 SyntaxError 메시지 내용(unexpected indent 등)으로 판별합니다.
    """

    # ══════════════════════════════════════════════════════
    # 1단계: 파이썬 문법 / 런타임 에러
    # ══════════════════════════════════════════════════════
    if not request.is_python_valid:
        log       = request.output_log          # 원본 (대소문자 유지, regex 추출용)
        error_log = log.lower()                  # 소문자 검색용
        source    = request.source_code

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
    # 2단계: 기계별 조건 미충족
    # ══════════════════════════════════════════════════════
    if not request.is_machine_valid:
        clean = request.source_code.replace(" ", "")

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

    # 성공 힌트(3단계)는 _generate_hint_typed() 의 ε-greedy 밴딧이 처리합니다.
    # 이 함수는 is_python_valid=False 또는 is_machine_valid=False 일 때만 호출됩니다.


# ──────────────────────────────────────────────────────────
# 힌트 타입 추론 — 에러/기계 힌트 분류 (밴딧 통계 태깅용)
# ──────────────────────────────────────────────────────────

def _infer_hint_type(request: CodeSubmitRequest, features: dict, cluster_rank: int) -> str | None:
    """
    generate_hint() 의 분기 로직과 동일한 기준으로 hint_type 문자열을 결정합니다.
    성공 케이스(rank 0/1/2)는 _generate_hint_typed() 내 밴딧이 처리하므로 None 반환.
    """
    if not request.is_python_valid:
        log = request.output_log.lower()
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
) -> tuple[str, str]:
    """
    힌트 텍스트와 hint_type ID를 함께 반환합니다.

    성공 힌트(rank 0/1) : ε-greedy 밴딧으로 변형 선택 → 효과적인 표현 자동 학습
    성공 힌트(rank 2)   : 단일 최상위 메시지 반환
    에러 / 기계 힌트    : 기존 generate_hint() 로직 유지 + 타입 태그 부여
    """
    if request.is_python_valid and request.is_machine_valid:
        if cluster_rank == 0:
            group = "succ_r0_has_if" if features.get('if_count', 0) > 0 else "succ_r0_simple"
            return _bandit_select(group)
        if cluster_rank == 1:
            group = (
                "succ_r1_while"
                if (features.get('while_count', 0) > 0
                    and not features.get('has_infinite_while', 0))
                else "succ_r1_for"
            )
            return _bandit_select(group)
        if cluster_rank == 2:
            if features.get('has_infinite_while', 0):
                return (
                    "[ 효율 최적화형 ] "
                    "while True 로 기계를 완전 자동화했어요! "
                    "최고 등급의 코드입니다.",
                    "succ_r2_infinite",
                )
            return (
                "[ 효율 최적화형 ] "
                "효율적인 반복문 구조로 잘 최적화된 코드예요! "
                "훌륭한 코드입니다.",
                "succ_r2_for",
            )
        # cluster_rank == -1 (모델 미로드)
        return (
            "코드가 정상 적용되었습니다. 반복문을 활용하면 더 높은 점수를 받을 수 있어요!",
            "succ_unknown",
        )

    # 에러 / 기계 힌트 — 기존 로직 재사용
    hint_text = generate_hint(request, score, features, cluster_rank)
    hint_type = _infer_hint_type(request, features, cluster_rank) or "hint_unknown"
    return hint_text, hint_type


def _update_prev_hint_effectiveness(
    cursor, user_pk: int, current_rank: int, is_success: bool,
) -> None:
    """
    사용자 직전 제출에 표시된 힌트가 효과적이었는지 평가하여 hint_stats 를 갱신합니다.

    효과 판정 기준:
        직전 실패 → 현재 성공         : 에러 힌트가 문제 해결을 도운 것으로 간주
        직전 rank X → 현재 rank Y > X : 성장 힌트가 개선을 이끈 것으로 간주

    이 함수는 현재 제출의 INSERT 전에 호출되므로
    조회하는 rows 는 순수하게 이전 제출 기록만 포함합니다.
    """
    try:
        cursor.execute(
            """
            SELECT hint_type, cluster_rank, is_success
            FROM   code_logs
            WHERE  user_pk = %s AND hint_type IS NOT NULL
            ORDER  BY created_at DESC
            LIMIT  1
            """,
            (user_pk,)
        )
        prev = cursor.fetchone()
        if not prev or not prev['hint_type']:
            return

        prev_hint_type = prev['hint_type']
        prev_rank      = prev.get('cluster_rank', -1)
        prev_success   = bool(prev.get('is_success', False))

        is_effective = (
            (not prev_success and is_success)
            or (is_success and prev_rank >= 0 and current_rank > prev_rank)
        )

        if not is_effective:
            return

        # in-memory 갱신
        stats = _hint_stats.setdefault(prev_hint_type, {"shown": 0, "success": 0})
        stats["success"] += 1

        # DB 갱신 — hint_stats 테이블이 없으면 조용히 건너뜀
        cursor.execute(
            """
            INSERT INTO hint_stats (hint_type, shown_count, success_count)
            VALUES (%s, 0, 1)
            ON DUPLICATE KEY UPDATE success_count = success_count + 1
            """,
            (prev_hint_type,)
        )
    except Exception as e:
        print(f"[HintBandit] 효과성 업데이트 실패: {e}")


# ──────────────────────────────────────────────────────────
# 엔드포인트 1: 코드 제출 결과 저장 및 AI 힌트 반환
# ──────────────────────────────────────────────────────────
@app.post("/api/submit_code")
async def submit_code(request: CodeSubmitRequest):
    """
    Unity 클라이언트가 코드 실행 후 호출합니다.
    성공/실패 여부에 관계없이 항상 code_logs 에 기록하고,
    AI 힌트와 점수를 응답합니다.
    """
    conn   = pymysql.connect(**DB_CONFIG)
    cursor = conn.cursor()
    try:
        # ast.parse 1회 호출로 score 계산·군집 예측·힌트 생성에 재사용
        features     = extract_features(request.source_code)
        score        = calculate_score(request, features)
        # 성공한 제출만 군집 예측 (실패 시 -1 저장)
        cluster_rank = predict_cluster_rank(features, request.execution_time) \
                       if request.is_success else -1

        # ── 보정 규칙: 반복문 전무 코드는 무조건 rank 0 ──────────
        # KMeans가 if/elif, 다중 함수 호출 등의 복잡도로 인해
        # 루프 없는 코드를 '일반 학습자형'으로 오분류하는 현상을 방지합니다.
        if cluster_rank > 0 and features.get('has_loop', 0) == 0:
            cluster_rank = 0

        # ast_complexity 는 별도 지표 (사이클로매틱 복잡도 + 최대 중첩 깊이)
        ast_complexity = calculate_ast_complexity(request.source_code)

        # guest 계정은 소문자 정규화
        search_id = "guest" if request.user_id.lower() == "guest" else request.user_id

        cursor.execute("SELECT pk_id FROM users WHERE id = %s", (search_id,))
        user_record = cursor.fetchone()
        if not user_record:
            raise HTTPException(status_code=404, detail=f"'{search_id}' 유저를 찾을 수 없습니다.")

        # ── 힌트 효과성 갱신: 직전 힌트가 이번 제출 결과로 효과적이었는지 평가 ──
        # 현재 제출은 아직 INSERT 전이므로 이전 기록만 조회됩니다.
        _update_prev_hint_effectiveness(cursor, user_record['pk_id'], cluster_rank, request.is_success)

        # ── 밴딧으로 힌트 선택 + hint_type ID 획득 ──────────────
        ai_hint, hint_type = _generate_hint_typed(request, score, features, cluster_rank)

        # 성공 + 유효한 군집 예측인 경우에만 이동 이력 문구를 힌트에 추가
        if cluster_rank >= 0:
            progression_note = _get_progression_note(cursor, user_record['pk_id'], cluster_rank)
            if progression_note:
                ai_hint += progression_note

        cursor.execute(
            """
            INSERT INTO code_logs
                (user_pk, machine_type, source_code, is_success, output_log,
                 execution_time, score, ast_complexity,
                 res_common, res_rare, res_special, res_exotic, gold, created_at)
            VALUES (%s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, NOW())
            """,
            (
                user_record['pk_id'],
                request.machine_type, request.source_code,
                request.is_success, request.output_log,
                request.execution_time, score, ast_complexity,
                request.res_common, request.res_rare,
                request.res_special, request.res_exotic, request.gold,
            )
        )
        # lastrowid 는 commit 이전에 저장해야 합니다.
        # pymysql 일부 버전에서 commit 후 cursor.lastrowid 가 리셋될 수 있습니다.
        inserted_id = cursor.lastrowid
        conn.commit()

        # cluster_rank 저장 — ALTER TABLE 마이그레이션이 완료된 경우에만 실행됩니다.
        # 컬럼이 없어도 메인 로직(힌트 반환, 점수 저장)은 정상 동작합니다.
        if cluster_rank >= 0:
            try:
                cursor.execute(
                    "UPDATE code_logs SET cluster_rank = %s WHERE log_id = %s",
                    (cluster_rank, inserted_id)
                )
                conn.commit()
            except Exception:
                pass

        # hint_type 저장 + 밴딧 노출 횟수 갱신 (in-memory 및 DB)
        # 마이그레이션 전이어도 메인 로직에는 영향 없습니다.
        _hint_stats.setdefault(hint_type, {"shown": 0, "success": 0})["shown"] += 1
        try:
            cursor.execute(
                "UPDATE code_logs SET hint_type = %s WHERE log_id = %s",
                (hint_type, inserted_id)
            )
            cursor.execute(
                """
                INSERT INTO hint_stats (hint_type, shown_count, success_count)
                VALUES (%s, 1, 0)
                ON DUPLICATE KEY UPDATE shown_count = shown_count + 1
                """,
                (hint_type,)
            )
            conn.commit()
        except Exception:
            pass

        return {
            "status":       "success",
            "score":        score,
            "hint":         ai_hint,
            "cluster_rank": cluster_rank,
        }

    except Exception as e:
        print(f"에러 발생: {e}")
        raise HTTPException(status_code=500, detail=str(e))
    finally:
        conn.close()


# ──────────────────────────────────────────────────────────
# 엔드포인트 2: 유저 군집 이동 이력 조회
# ──────────────────────────────────────────────────────────

_RANK_LABELS = {0: "단순 코드형", 1: "일반 학습자형", 2: "효율 최적화형"}


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
@app.get("/api/user_loop_balance/{user_id}")
async def get_user_loop_balance(user_id: str, sample_size: int = 20):
    """
    유저의 최근 성공 제출(최대 sample_size개)에서
    for / while 사용 비율을 분석하여 균형 지표를 반환합니다.

    Unity 클라이언트 활용 가이드:
        obstacle_intensity    (int,  0~100) : 높을수록 강한 제약 장애물 부여
        recommended_loop_type (str, "for"|"while") : 부족한 루프 유형
        imbalance_score       (float, 0.0~1.0) : 0 에 가까울수록 균형 잡힌 코딩 스타일
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
            SELECT source_code FROM code_logs
            WHERE  user_pk = %s AND is_success = 1
            ORDER  BY created_at DESC
            LIMIT  %s
            """,
            (user_record['pk_id'], sample_size)
        )
        rows = cursor.fetchall()
    finally:
        conn.close()

    _no_data_base = {
        "sample_count":          0,
        "total_for_count":       0,
        "total_while_count":     0,
        "for_ratio":             0.0,
        "while_ratio":           0.0,
        "imbalance_score":       0.0,
        "recommended_loop_type": "for",
        "obstacle_intensity":    0,
    }

    if not rows:
        return {**_no_data_base, "status": "no_data", "message": "성공한 제출 기록이 없습니다."}

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

    # 부족한 쪽 루프를 권장
    recommended = "while" if for_ratio > 0.5 else "for"

    # obstacle_intensity: 0~100 정수
    obstacle_intensity = int(min(imbalance_score * 100, 100))

    return {
        "status":                "success",
        "sample_count":          len(rows),
        "total_for_count":       total_for,
        "total_while_count":     total_while,
        "for_ratio":             round(for_ratio, 3),
        "while_ratio":           round(while_ratio, 3),
        "imbalance_score":       imbalance_score,
        "recommended_loop_type": recommended,
        "obstacle_intensity":    obstacle_intensity,
    }


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
        shown_count   — 해당 힌트가 사용자에게 표시된 횟수
        success_count — 해당 힌트 이후 사용자가 개선된 횟수 (rank 상승 또는 에러 해결)
        success_rate  — success_count / shown_count (노출 없으면 null)

    활용:
        어떤 힌트 표현이 실제로 사용자 행동 변화를 이끌어내는지 확인할 수 있습니다.
        성공률이 낮은 변형은 자동으로 노출 빈도가 줄어듭니다 (ε-greedy exploit 단계).
    """
    result = {}
    for hint_type, stats in sorted(_hint_stats.items()):
        shown   = stats.get("shown",   0)
        success = stats.get("success", 0)
        result[hint_type] = {
            "shown_count":   shown,
            "success_count": success,
            "success_rate":  round(success / shown, 3) if shown > 0 else None,
        }
    return {
        "status":     "success",
        "hint_count": len(result),
        "stats":      result,
    }


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

    return {
        "status":         "reloaded",
        "model_loaded":   code_cluster_model is not None,
        "before": {"trained_at": before_trained, "loaded_at": before_loaded},
        "after":  {"trained_at": after_trained,
                   "loaded_at": datetime.datetime.fromtimestamp(_model_loaded_mtime)
                                .strftime("%Y-%m-%d %H:%M:%S") if _model_loaded_mtime else '없음'},
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
