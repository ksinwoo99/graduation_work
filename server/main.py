from fastapi import FastAPI, HTTPException
from pydantic import BaseModel
import pymysql
import joblib
import os
import pandas as pd
from config import DB_CONFIG, MODEL_PATH
from utils import extract_features

app = FastAPI()
BASE_DIR = os.path.dirname(os.path.abspath(__file__))
MODEL_PATH = os.path.join(BASE_DIR, 'code_cluster_model.pkl')
code_cluster_model = None
scaler = None  # 👈 스케일러 변수 추가!
# ML 모델을 서버 구동 시 한 번만 메모리에 로드
if os.path.exists(MODEL_PATH):
    try:
        saved_data = joblib.load(MODEL_PATH)
        # 새로 바꾼 모델(dict 형태)이라면 해체해서 로드
        if isinstance(saved_data, dict):
            code_cluster_model = saved_data['model']
            scaler = saved_data['scaler']
        else:
            # 예전 모델일 경우 임시 호환
            code_cluster_model = saved_data
        print("ML Model & Scaler 로드 성공")
    except Exception as e:
        print(f"ML 로드 실패: {e}")

class CodeSubmitRequest(BaseModel):
    user_id : str 
    machine_type: str        # 어떤 기계에서 실행했는지 
    source_code: str         # 유저가 작성한 코드 원본
    
    # 자원 보유량 변수
    # res_common: int
    # res_rare: int
    # res_special: int
    # res_exotic: int

    is_python_valid: bool    # 파이썬 문법 검사 및 통과 여부
    is_machine_valid: bool   # 유니티 기계 조건 통과 여부
    is_success: bool         # 최종 성공 여부
    execution_time: float    # 코드 실행에 걸린 시간 (초)
    output_log: str          # 파이썬 에러 로그 또는 정상 출력 결과물

def generate_hint(request: CodeSubmitRequest, calculated_score: float):
    # 파이썬 문법 에러
    # 1단계 실패 : 파이썬 문법 및 런타임 에러
    if not request.is_python_valid:
        error_log = request.output_log.lower()
        
        # 들여쓰기 에러
        if "indentation" in error_log or "taberror" in error_log:
            return "파이썬은 들여쓰기가 생명이에요! 들여쓰기가 제대로 되었는지 확인해보세요.(4칸)"
            
        # 오타, 괄호, 콜론(:) 누락
        elif "syntax" in error_log:
            return "명령어에 오타가 있거나, 괄호() 또는 따옴표('')를 닫지 않은 것 같아요. 조건문 뒤에 콜론(:)을 빠뜨렸을지도 몰라요!"
            
        # 정의되지 않은 변수/함수 사용 (함수 이름 오타)
        elif "nameerror" in error_log:
            return "존재하지 않는 명령어(또는 변수)를 불렀어요. 오타가 발생했는지 확인해보세요!"
            
        # 타입 에러 
        elif "typeerror" in error_log:
            return "타입 에러가 발생했어요. 숫자가 들어갈 자리에 문자열(글자)을 넣지는 않았나요?"

        # 값 에러 
        elif "valueerror" in error_log:
            return "명령어의 형식은 맞지만, 올바르지 않은 값이 들어갔어요. 정확한 값을 입력했는지 확인해보세요."

            
        # 무한 루프 또는 시간 초과 (게임 엔진에서 처리하는 방식보고 수정)
        elif "timeouterror" in error_log or "recursionerror" in error_log:
            return ""
            
        # 그 외의 알 수 없는 에러
        else:
            return "기계가 미지의 파이썬 에러를 뿜어내고 있습니다. 로그 창의 글씨를 번역해서 문제를 해결해 보세요!"
        
    # 2단계 실패 : 기계별 문법에 따른 힌트 (추가 예정)
    if not request.is_machine_valid:
        if request.machine_type == "":
            return "이 기계는 채굴(mining)에 특화되어 있습니다. 다른 명령어를 입력하지는 않았나요?"
        elif request.machine_type == "":
            return "이 기계는..."
        return "문법은 맞았지만, 이 기계가 수행할 수 없는 명령입니다."
    
    # 글자 자체에서 뽑아낸 특징
    features = extract_features(request.source_code)

    # 동적 실행 결과의 특징
    features['execution_time'] = request.execution_time
    features['score'] = calculated_score

    # 스케일러까지 잘 로드되어 있을 때만 ML 예측 실행
    if code_cluster_model is not None and scaler is not None:
        try:
            user_df = pd.DataFrame([features])
            
            # ✨ 2. 학습할 때 썼던 스케일러로 똑같이 변환(압축)해줍니다!
            scaled_features = scaler.transform(user_df)
            
            # ✨ 3. 변환된 데이터를 모델에 넣습니다!
            cluster_id = code_cluster_model.predict(scaled_features)[0]
            
            # 방금 분석한 결과에 맞춰서 멘트
            if cluster_id == 0:
                return "군집 0 힌트: ..."
            elif cluster_id == 1:
                return "군집 1 힌트: ..."
            elif cluster_id == 2:
                return "군집 2 힌트: ..."
        except Exception as e:
            print(f"Ai 힌트 생성 중 에러 발생 : {e}")
            
    return "힌트 생성 완료"
        
@app.post("/api/submit_code")
async def submit_code(request: CodeSubmitRequest):
    conn = pymysql.connect(**DB_CONFIG)
    cursor = conn.cursor()
    try:
        # AI 힌트 및 점수 계산 (데이터 쌓이기 전 임시)
        calculated_score = 100 - (request.execution_time * 10) - (len(request.source_code) * 0.1)
        if calculated_score < 0: calculated_score = 0
        ai_hint = generate_hint(request, calculated_score)

        find_pk_sql = "SELECT pk_id FROM users WHERE id = %s"
        search_id = request.user_id.lower() if request.user_id.lower() == "guest" else request.user_id
        
        cursor.execute(find_pk_sql, (search_id,))
        user_record = cursor.fetchone()

        if not user_record:
            raise Exception(f"DB에 '{search_id}' 라는 유저가 없습니다!")
            
        real_user_pk = user_record['pk_id'] 

        # 로그 저장
        insert_sql = """
            INSERT INTO code_logs 
            (user_pk, machine_type, source_code, is_success, output_log, execution_time, score, created_at)
            VALUES (%s, %s, %s, %s, %s, %s, %s, NOW())
        """

        cursor.execute(insert_sql, (
            real_user_pk, request.machine_type, request.source_code, 
            request.is_success, request.output_log, request.execution_time, calculated_score
        ))
        
        conn.commit()
        
        return {
            "status": "success", 
            "score": round(calculated_score, 2),
            "hint": ai_hint 
        }

    except Exception as e:
        print(f"에러 발생: {e}")
        raise HTTPException(status_code=500, detail=str(e))
    finally:
        conn.close()

if __name__ == "__main__":
    import uvicorn
    uvicorn.run("main:app", host="0.0.0.0", port=8000, reload=True) 