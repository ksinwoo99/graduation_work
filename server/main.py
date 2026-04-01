"""
server/main.py
ML 서버 (Server B) — 코드 로그 저장 / AI 힌트 생성 / 루프 균형 분석

엔드포인트:
    POST /api/submit_code           — 코드 제출 결과 저장 및 AI 힌트 반환
    GET  /api/user_loop_balance/{user_id} — 유저 루프 사용 균형 분석
"""

from fastapi import FastAPI, HTTPException
from pydantic import BaseModel
import pymysql
import joblib
import os
import pandas as pd

from config import DB_CONFIG          # DB 접속 설정 (server/config.py)
from utils import extract_features, calculate_ast_complexity

app = FastAPI()

# 모델 파일은 main.py 와 같은 디렉터리에 위치
_BASE_DIR  = os.path.dirname(os.path.abspath(__file__))
MODEL_PATH = os.path.join(_BASE_DIR, 'code_cluster_model.pkl')

# 서버 구동 시 모델을 메모리에 한 번만 로드
code_cluster_model = None
scaler = None

if os.path.exists(MODEL_PATH):
    try:
        saved = joblib.load(MODEL_PATH)
        if isinstance(saved, dict):          # 현재 포맷: {'model': ..., 'scaler': ...}
            code_cluster_model = saved['model']
            scaler             = saved['scaler']
        else:                                # 이전 포맷(모델 단독 저장) 하위 호환
            code_cluster_model = saved
        print("ML Model & Scaler 로드 성공")
    except Exception as e:
        print(f"ML 로드 실패: {e}")


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
# AI 힌트 생성 (3단계 폭포수 구조)
# ──────────────────────────────────────────────────────────
def generate_hint(request: CodeSubmitRequest, calculated_score: float) -> str:
    """
    제출 결과에 따라 단계별로 힌트를 생성합니다.

    1단계 (is_python_valid == False): 파이썬 에러 유형별 힌트
    2단계 (is_machine_valid == False): 기계 조건 미충족 힌트
    3단계 (성공):                      ML 군집 기반 맞춤 힌트
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

        # 기계 타입별 필수 함수 체크 (기계 추가 시 여기에 elif 블록 추가)
        if request.machine_type == "Miner_Common":
            if "mining()" not in clean:
                return "이 기계는 채굴(mining)에 특화되어 있습니다. 다른 명령어를 입력하지는 않았나요?"

        return "문법은 맞았지만, 이 기계가 수행할 수 없는 명령입니다."

    # ── 3단계: 성공 — ML 군집 기반 힌트 ─────────────────────
    features = extract_features(request.source_code)
    features['execution_time'] = request.execution_time
    features['score']          = calculated_score

    if code_cluster_model is not None and scaler is not None:
        try:
            user_df          = pd.DataFrame([features])
            scaled_features  = scaler.transform(user_df)
            cluster_id       = code_cluster_model.predict(scaled_features)[0]

            # KMeans 3군집 힌트
            # Cluster 0 — 단순 코드형  : 반복문 없음, 느린 실행, 낮은 점수  → for 사용 유도
            # Cluster 1 — 성장형       : 반복문 일부 사용, 중간 효율        → 코드 다이어트 + while 실험 유도
            # Cluster 2 — 효율 최적화형: 반복문 적극 활용, 빠른 실행, 고점수 → 긍정 강화 + 무한 루프 도전 유도
            # ※ 실제 군집 특성을 확인한 후 힌트 텍스트 보정 필요
            if cluster_id == 0:
                return (
                    "[ AI 분석 리포트 ] "
                    "이 기계는 매번 명령을 하나씩 실행하고 있습니다. "
                    "반복문(for)을 사용하면 한 줄로 여러 번 채굴할 수 있어요! "
                    "예시: for i in range(5): mining()"
                )
            if cluster_id == 1:
                return (
                    "[ AI 분석 리포트 ] "
                    "반복문을 사용한 흔적이 감지되었습니다. 좋은 시작이에요! "
                    "코드를 더 짧게 줄이거나, for 대신 while로도 작성해보면 "
                    "시스템 숙련도가 더욱 빠르게 올라갈 거예요."
                )
            if cluster_id == 2:
                return (
                    "[ AI 분석 리포트 ] "
                    "이 기계의 코드는 매우 효율적으로 최적화되어 있습니다! "
                    "다음 목표: 상위 시스템 해금 후 while True: 무한 루프로 "
                    "공장을 완전 자동화해 보세요."
                )

        except Exception as e:
            print(f"AI 힌트 생성 중 에러 발생: {e}")

    # ML 모델 미로드 상태의 기본 힌트
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
    """
    conn   = pymysql.connect(**DB_CONFIG)
    cursor = conn.cursor()
    try:
        # 점수 계산: 빠르고 짧은 코드일수록 고점수 (최솟값 0)
        score = max(0.0, 100 - (request.execution_time * 10) - (len(request.source_code) * 0.1))

        ai_hint        = generate_hint(request, score)
        ast_complexity = calculate_ast_complexity(request.source_code)
        # ast_complexity: 사이클로매틱 복잡도 + 최대 중첩 깊이
        # 파싱 불가(문법 오류 코드) 시 -1 이 저장됨

        # guest 계정은 소문자 정규화
        search_id = "guest" if request.user_id.lower() == "guest" else request.user_id

        cursor.execute("SELECT pk_id FROM users WHERE id = %s", (search_id,))
        user_record = cursor.fetchone()
        if not user_record:
            raise Exception(f"DB에 '{search_id}' 라는 유저가 없습니다!")

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
        conn.commit()

        return {
            "status": "success",
            "score":  round(score, 2),
            "hint":   ai_hint,
        }

    except Exception as e:
        print(f"에러 발생: {e}")
        raise HTTPException(status_code=500, detail=str(e))
    finally:
        conn.close()


# ──────────────────────────────────────────────────────────
# 엔드포인트 2: 유저 루프 사용 균형 분석
# ──────────────────────────────────────────────────────────
@app.get("/api/user_loop_balance/{user_id}")
async def get_user_loop_balance(user_id: str, sample_size: int = 20):
    """
    유저의 최근 성공 제출(최대 sample_size개)에서
    for / while 사용 비율을 분석하여 균형 지표를 반환합니다.

    Unity 클라이언트 활용 가이드:
        obstacle_intensity   (int,  0~100) : 높을수록 강한 제약 장애물 부여
        recommended_loop_type (str, "for"|"while") : 부족한 루프 유형
        imbalance_score      (float, 0.0~1.0) : 0 에 가까울수록 균형 잡힌 코딩 스타일
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
    # 권장 임계값 — 30 미만: 제약 없음 / 30~59: 경고 / 60 이상: 차단
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
