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
cluster_rank_map: dict = {}

# 마지막으로 로드한 pkl 파일의 수정 시각 (hot-reload 기준)
_model_loaded_mtime: float = 0.0

# kmeans_trainer.py 가 pkl 에 저장한 학습 메타데이터
# /api/model_status 에서 사용합니다.
_model_meta: dict = {}


def _load_model() -> None:
    """pkl 파일을 읽어 전역 모델 변수를 갱신"""
    global code_cluster_model, scaler, cluster_rank_map, _model_loaded_mtime, _model_meta

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
        else:
            # 이전 포맷(모델 단독 저장) 하위 호환
            code_cluster_model = saved
            cluster_rank_map   = {0: 0, 1: 1, 2: 2}
            _model_meta        = {}

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
# 군집 예측 헬퍼 — 모델 핫리로드 포함
# ──────────────────────────────────────────────────────────
def predict_cluster_rank(features: dict, execution_time: float, score: float) -> int:
    """
    현재 모델로 코드의 군집 semantic rank(0/1/2)를 예측합니다.

    - 예측 직전에 _maybe_reload_model() 을 호출하여 최신 모델을 보장합니다.
    - 모델 미로드 / 예측 실패 시 -1 을 반환합니다.

    반환값:
        -1 : 모델 없음 또는 예측 오류
         0 : 단순 코드형  (루프 미사용, 낮은 점수)
         1 : 성장형       (루프 일부 사용)
         2 : 효율 최적화형 (루프 적극 활용, 높은 점수)
    """
    _maybe_reload_model()

    if code_cluster_model is None or scaler is None:
        return -1

    try:
        feat_with_meta = {**features, 'execution_time': execution_time, 'score': score}
        user_df        = pd.DataFrame([feat_with_meta])
        scaled         = scaler.transform(user_df)
        raw_cluster    = int(code_cluster_model.predict(scaled)[0])
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

_RANK_LABELS_SHORT = {0: "단순 코드형", 1: "성장형", 2: "효율 최적화형"}

_STAGNATION_NUDGE = {
    # rank 0: 아직 루프를 안 쓰는 유저 — 어떤 반복문이든 시작을 유도
    0: "for i in range(5): mining() 처럼 반복문을 활용해보세요!",
    # rank 1: 루프를 쓰지만 효율이 중간 — 루프 자체를 더 잘 쓰도록 유도 (while 강요 X)
    1: "range()의 숫자를 더 키우거나, 작업에 맞는 조건식(while count < 10 등)을 시도해보세요.",
    # rank 2: 이미 효율적 — 정체 감지 메시지 없음 (아래 _get_progression_note 에서 스킵)
}


def _get_progression_note(cursor, user_pk: int, current_rank: int) -> str:
    """
    직전 제출들의 cluster_rank와 비교하여 성장 또는 정체 문구를 반환합니다.
    반환값은 기존 힌트 뒤에 그대로 이어붙입니다.

    - 성장 감지: 이전 rank < 현재 rank  →  축하 문구
    - 정체 감지: 직전 2회 이상 동일 rank  →  전환 유도 문구
    - 데이터 부족 / cluster_rank 컬럼 미존재  →  빈 문자열 반환 (조용히 처리)

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

    # ── 성장 감지 ────────────────────────────────────────────
    if current_rank > last_rank:
        return (
            f"\n[ {_RANK_LABELS_SHORT.get(current_rank, str(current_rank))} 달성! ] "
            f"{_RANK_LABELS_SHORT.get(last_rank, str(last_rank))}에서 "
            f"{_RANK_LABELS_SHORT.get(current_rank, str(current_rank))}으로 올라섰어요! "
            "계속 이 방향으로 나아가세요."
        )

    # ── 정체 감지: 직전 2회 + 현재 = 3회 연속 동일 rank ─────
    # rank 2 는 이미 효율적인 코드 — 잘하고 있는데 "바꿔보세요" 메시지는 불필요
    if current_rank == 2:
        return ""

    consecutive = 0
    for r in prev_ranks:
        if r == current_rank:
            consecutive += 1
        else:
            break

    if consecutive >= 2:   # 직전 2개가 같고 현재도 같으면 총 3연속
        label = _RANK_LABELS_SHORT.get(current_rank, str(current_rank))
        nudge = _STAGNATION_NUDGE.get(current_rank, "새로운 방식을 시도해보세요.")
        return f"\n[ {label} 지속 중 ] {consecutive + 1}번 연속 같은 스타일이에요. {nudge}"

    return ""


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

    1단계 (is_python_valid == False) : 파이썬 에러 유형별 힌트
    2단계 (is_machine_valid == False): 기계 조건 미충족 힌트
    3단계 (성공)                      : ML 군집 기반 맞춤 힌트

    cluster_rank : predict_cluster_rank() 의 결과를 submit_code() 에서 미리 계산해 전달합니다.
                   -1 이면 모델 미로드 상태로 간주해 기본 힌트를 반환합니다.
    features     : submit_code() 에서 미리 추출해 전달합니다 (중복 ast.parse 방지).
    """

    # ── 1단계: 파이썬 문법 / 런타임 에러 ─────────────────────
    if not request.is_python_valid:
        error_log = request.output_log.lower()

        if "indentation" in error_log or "taberror" in error_log:
            return "파이썬은 들여쓰기가 생명이에요! 들여쓰기가 제대로 되었는지 확인해보세요. (4칸)"

        if "syntax" in error_log:
            if "unexpected eof" in error_log or "never closed" in error_log:
                return "괄호 '(' 또는 '{', '[' 를 열고 닫지 않았는지 확인해 보세요!"
            if "unterminated string" in error_log or "eol while scanning" in error_log:
                return "따옴표('' 나 \"\")를 열고 닫지 않았는지 확인해 보세요!"
            return "명령어에 오타가 있거나, 조건문/반복문 뒤에 콜론(:)을 빠뜨렸을지도 몰라요!"

        if "nameerror" in error_log:
            return "존재하지 않는 명령어(또는 변수)를 불렀어요. 오타가 발생했는지 확인해보세요!"

        if "typeerror" in error_log:
            return "타입 에러가 발생했어요. 숫자가 들어갈 자리에 문자열(글자)을 넣지는 않았나요?"

        if "valueerror" in error_log:
            return "명령어의 형식은 맞지만, 올바르지 않은 값이 들어갔어요. 정확한 값을 입력했는지 확인해보세요."

        if "timeouterror" in error_log or "recursionerror" in error_log:
            return "코드가 너무 오래 실행되고 있어요. 무한 루프에 빠진 건 아닌지 확인해보세요!"

        return "기계가 미지의 파이썬 에러를 뿜어내고 있습니다. 로그 창의 글씨를 번역해서 문제를 해결해 보세요!"

    # ── 2단계: 기계별 조건 미충족 ────────────────────────────
    if not request.is_machine_valid:
        clean = request.source_code.replace(" ", "")

        if "name=" not in clean:
            return "기계를 작동시키려면 먼저 이름을 지어줘야 해요! 코드 맨 윗줄에 'name = \"이름\"'을 추가해보세요."

        # REQUIRED_FUNCTIONS 딕셔너리 기반 체크 — 기계 추가 시 위 딕셔너리만 수정
        for fn in REQUIRED_FUNCTIONS.get(request.machine_type, []):
            if fn.replace(" ", "") not in clean:
                return f"이 기계는 {fn} 명령어가 필요합니다. 다른 명령어를 입력하지는 않았나요?"

        return "문법은 맞았지만, 이 기계가 수행할 수 없는 명령입니다."

    # ── 3단계: 성공 — ML 군집 기반 힌트 ─────────────────────
    # cluster_rank 는 submit_code() 에서 predict_cluster_rank() 로 미리 계산됩니다.
    if cluster_rank == 0:
        return (
            "[ 단순 코드형 ] "
            "이 기계는 매번 명령을 하나씩 실행하고 있습니다. "
            "반복문(for)을 사용하면 한 줄로 여러 번 채굴할 수 있어요! "
            "예시: for i in range(5): mining()"
        )
    if cluster_rank == 1:
        return (
            "[ 성장형 ] "
            "반복문을 사용하고 있지만 더 효율적으로 만들 수 있어요! "
            "range()의 범위를 더 크게 늘리거나, "
            "작업 조건에 맞는 while 문을 활용해보세요."
        )
    if cluster_rank == 2:
        return (
            "[ 효율 최적화형 ] "
            "이 기계의 코드는 매우 효율적으로 최적화되어 있습니다! "
            "훌륭한 코드예요."
        )

    # cluster_rank == -1: 모델 미로드 또는 예측 실패 시 기본 힌트
    return "코드가 정상 적용되었습니다. 반복문을 활용하면 더 높은 점수를 받을 수 있어요!"


# ──────────────────────────────────────────────────────────
# 엔드포인트 1: 코드 제출 결과 저장 및 AI 힌트 반환
# ──────────────────────────────────────────────────────────
@app.post("/api/submit_code")
async def submit_code(request: CodeSubmitRequest):
    """
    Unity 클라이언트가 코드 실행 후 호출합니다.
    성공/실패 여부에 관계없이 항상 code_logs 에 기록하고,
    AI 힌트와 점수를 응답합니다.

    [변경 사항]
    - extract_features() 를 1회만 호출 후 score 계산·힌트 생성에 공유
      (기존: generate_hint 내부에서 성공 시 별도 추출 → ast.parse 중복 호출)
    - calculate_score() 분리 → 교육 목표 기반 공식 적용
    """
    conn   = pymysql.connect(**DB_CONFIG)
    cursor = conn.cursor()
    try:
        # 특징을 먼저 추출하여 score 계산·군집 예측·힌트 생성에 재사용 (ast.parse 1회)
        features     = extract_features(request.source_code)
        score        = calculate_score(request, features)
        # 성공한 제출만 군집 예측 (실패 시 -1 저장)
        cluster_rank = predict_cluster_rank(features, request.execution_time, score) \
                       if request.is_success else -1
        ai_hint      = generate_hint(request, score, features, cluster_rank)

        # ast_complexity 는 별도 지표 (사이클로매틱 복잡도 + 최대 중첩 깊이)
        ast_complexity = calculate_ast_complexity(request.source_code)

        # guest 계정은 소문자 정규화
        search_id = "guest" if request.user_id.lower() == "guest" else request.user_id

        cursor.execute("SELECT pk_id FROM users WHERE id = %s", (search_id,))
        user_record = cursor.fetchone()
        if not user_record:
            raise Exception(f"DB에 '{search_id}' 라는 유저가 없습니다!")

        # 성공 + 유효한 군집 예측인 경우에만 이동 이력 문구를 힌트에 추가
        # 현재 제출은 아직 INSERT 전이므로 이전 기록만 비교합니다.
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

_RANK_LABELS = {0: "단순 코드형", 1: "성장형", 2: "효율 최적화형"}


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
#     uvicorn.run("main:app", host="127.0.0.1", port=8000, reload=True)

# AWS 배포용
if __name__ == "__main__":
    import uvicorn
    uvicorn.run("main:app", host="0.0.0.0", port=8001, reload=True)
